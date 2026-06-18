// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.StatusBar;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security;
using System.Threading.Channels;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Runtime.EventLog;

internal sealed class OpenLogEffects(
    IState<EventLogState> eventLogState,
    ITraceLogger logger,
    ILogWatcherService logWatcherService,
    IEventResolverCache resolverCache,
    IEventXmlResolver xmlResolver,
    IServiceScopeFactory serviceScopeFactory,
    IDatabaseService databaseService,
    ICriticalErrorService criticalErrorService,
    LogCloseCoordinator closeCoordinator,
    EventLogConcurrencyState concurrencyState,
    PartialLoadCoordinator coordinator)
{
    private static readonly int s_maxGlobalConcurrency = Math.Max(1, Environment.ProcessorCount - 1);
    private static readonly SemaphoreSlim s_resolutionThrottle = new(s_maxGlobalConcurrency, s_maxGlobalConcurrency);

    private readonly LogCloseCoordinator _closeCoordinator = closeCoordinator;
    private readonly EventLogConcurrencyState _concurrencyState = concurrencyState;
    private readonly PartialLoadCoordinator _coordinator = coordinator;
    private readonly ICriticalErrorService _criticalErrorService = criticalErrorService;
    private readonly IDatabaseService _databaseService = databaseService;
    private readonly IState<EventLogState> _eventLogState = eventLogState;
    private readonly Lock _globalCtsLock = new();
    private readonly ConcurrentDictionary<EventLogId, CancellationTokenSource> _logCts = new();
    private readonly ITraceLogger _logger = logger;
    private readonly ConcurrentDictionary<EventLogId, TaskCompletionSource> _logLoadCompletions = new();
    private readonly ILogWatcherService _logWatcherService = logWatcherService;
    private readonly IEventResolverCache _resolverCache = resolverCache;
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly IEventXmlResolver _xmlResolver = xmlResolver;

    private long _cancelToken;
    private CancellationTokenSource _globalCts = new();

    [EffectMethod(typeof(CloseAllLogsAction))]
    public async Task HandleCloseAll(IDispatcher dispatcher)
    {
        _logger.Trace($"{nameof(HandleCloseAll)} requested ({_eventLogState.Value.OpenLogs.Count} active logs).");

        _coordinator.DiscardAll();

        _concurrencyState.InvalidateInFlightFilters();
        _concurrencyState.InvalidateInFlightReloads();

        CancelAllLoads();

        _resolverCache.ClearAll();
        _xmlResolver.ClearAll();
        _concurrencyState.ClearAllLoadedWithXml();
        _closeCoordinator.ClearAllPendingRestore();

        await _logWatcherService.RemoveAllAsync();
    }

    [EffectMethod]
    public async Task HandleCloseLog(CloseLogAction action, IDispatcher dispatcher)
    {
        _logger.Trace($"{nameof(HandleCloseLog)} requested for '{action.LogName}' (id: {action.LogId}).");

        _coordinator.Discard(action.LogId);

        try
        {
            if (_logCts.TryGetValue(action.LogId, out var cts))
            {
                try { cts.Cancel(); }
                catch (ObjectDisposedException) { /* CTS already disposed; cancel is moot. */ }
            }

            if (_logLoadCompletions.TryGetValue(action.LogId, out var loadCompletion))
            {
                try
                {
                    await loadCompletion.Task.WaitAsync(LogCloseCoordinator.LogCloseTimeout);
                }
                catch (TimeoutException)
                {
                    _logger.Trace($"{nameof(HandleCloseLog)}: load task for '{action.LogName}' did not unwind within {LogCloseCoordinator.LogCloseTimeout}.");
                }
            }

            await _logWatcherService.RemoveLogAsync(action.LogName);

            _concurrencyState.ClearLoadedWithXml(action.LogId);
            _closeCoordinator.ClearPendingRestore(action.LogName);

            _xmlResolver.ClearXmlCacheForLog(action.LogName);

            dispatcher.Dispatch(new LogTable.CloseLogAction(action.LogId));

            if (_eventLogState.Value.OpenLogs.IsEmpty)
            {
                _resolverCache.ClearAll();
            }
        }
        finally
        {
            _closeCoordinator.CompleteCloseFor(action.LogId);
        }
    }

    [EffectMethod]
    public async Task HandleOpenLog(OpenLogAction action, IDispatcher dispatcher)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.Information($"{nameof(HandleOpenLog)} '{action.LogName}' (path type: {action.LogPathType}).");

        long cancelTokenAtStart = Volatile.Read(ref _cancelToken);

        if (!_eventLogState.Value.OpenLogs.TryGetValue(action.LogName, out var openInfo))
        {
            _logger.Warning($"Open '{action.LogName}' aborted: log not found in OpenLogs (no prior AddLog dispatch).");

            dispatcher.Dispatch(new SetResolverStatusAction($"Error: Failed to open {action.LogName}"));

            return;
        }

        var logData = new EventLogData(action.LogName, openInfo.Type) { Id = openInfo.Id };

        CancellationTokenSource perLoadCts;

        using (_globalCtsLock.EnterScope())
        {
            perLoadCts = CancellationTokenSource.CreateLinkedTokenSource(_globalCts.Token, action.Token);
        }

        _logCts[logData.Id] = perLoadCts;
        _logLoadCompletions[logData.Id] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        if (Volatile.Read(ref _cancelToken) != cancelTokenAtStart)
        {
            _logger.Trace($"Open '{action.LogName}': cancel token changed during CTS link; aborting load.");

            try { perLoadCts.Cancel(); }
            catch (ObjectDisposedException) { /* CTS already disposed; cancel is moot. */ }
        }

        var token = perLoadCts.Token;

        try
        {
            try
            {
                await _databaseService.InitialClassificationTask;
            }
            catch (Exception ex)
            {
                _logger.Trace($"InitialClassificationTask faulted unexpectedly during HandleOpenLog: {ex}");
            }

            if (!_eventLogState.Value.OpenLogs.TryGetValue(action.LogName, out var current)
                || current.Id != logData.Id)
            {
                _logger.Trace($"Open '{action.LogName}': log was closed or replaced before resolver scope creation; aborting after {stopwatch.ElapsedMilliseconds}ms.");

                return;
            }

            using var serviceScope = _serviceScopeFactory.CreateScope();

            IEventResolver? eventResolver;

            try
            {
                eventResolver = serviceScope.ServiceProvider.GetService<IEventResolver>();
            }
            catch (Exception ex)
            {
                _criticalErrorService.ReportCritical(ex);

                return;
            }

            if (eventResolver is null)
            {
                _logger.Warning($"Open '{action.LogName}' aborted: no IEventResolver registered.");

                dispatcher.Dispatch(new SetResolverStatusAction("Error: No event resolver available"));

                return;
            }

            await LoadLogAsync(action, logData, eventResolver, dispatcher, token, stopwatch);
        }
        finally
        {
            if (_logCts.TryRemove(logData.Id, out var removedCts))
            {
                removedCts.Dispose();
            }

            if (_logLoadCompletions.TryRemove(logData.Id, out var loadComplete))
            {
                loadComplete.TrySetResult();
            }
        }
    }

    private void CancelAllLoads()
    {
        CancellationTokenSource oldGlobalCts;

        using (_globalCtsLock.EnterScope())
        {
            oldGlobalCts = _globalCts;
            _globalCts = new CancellationTokenSource();
            Interlocked.Increment(ref _cancelToken);
        }

        oldGlobalCts.Cancel();
        oldGlobalCts.Dispose();

        foreach (var key in _logCts.Keys)
        {
            if (_logCts.TryGetValue(key, out var cts))
            {
                try { cts.Cancel(); }
                catch (ObjectDisposedException) { /* CTS already disposed; cancel is moot. */ }
            }
        }
    }

    private async Task LoadLogAsync(
        OpenLogAction action,
        EventLogData logData,
        IEventResolver eventResolver,
        IDispatcher dispatcher,
        CancellationToken token,
        Stopwatch stopwatch)
    {
        if (action.LogPathType == LogPathType.File)
        {
            try
            {
                var logDir = Path.GetDirectoryName(action.LogName);

                if (logDir is not null)
                {
                    var localeDir = Path.Combine(logDir, "LocaleMetaData");

                    if (Directory.Exists(localeDir))
                    {
                        var mtaFiles = Directory.GetFiles(localeDir, "*.MTA");

                        if (mtaFiles.Length > 0)
                        {
                            Array.Sort(mtaFiles, StringComparer.Ordinal);
                            eventResolver.SetMetadataPaths(mtaFiles);
                            _logger.Information($"Using locale metadata from: {localeDir} ({mtaFiles.Length} file(s))");
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or SecurityException or ArgumentException or NotSupportedException)
            {
                _logger.Warning($"Failed to probe locale metadata for {action.LogName}: {ex.Message}");
            }
        }

        var activityId = StatusActivityId.Create();
        string? lastEvent;
        int failed = 0;
        int resolved = 0;
        int lastPartialIndex = 0;
        int timerTick = 0;

        dispatcher.Dispatch(new AddTableAction(logData));

        var channel = Channel.CreateBounded<EventRecord[]>(new BoundedChannelOptions(s_maxGlobalConcurrency * 2)
        {
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        List<ResolvedEvent> events = [];

        await using var timer = new Timer(
            _ =>
            {
                dispatcher.Dispatch(new SetEventsLoadingAction(activityId, Volatile.Read(ref resolved), Volatile.Read(ref failed)));

                if (Interlocked.Increment(ref timerTick) <= 1) { return; }

                List<ResolvedEvent> delta;

                lock (events)
                {
                    int fromIndex = Volatile.Read(ref lastPartialIndex);

                    if (events.Count <= fromIndex) { return; }

                    delta = events.GetRange(fromIndex, events.Count - fromIndex);
                    Volatile.Write(ref lastPartialIndex, events.Count);
                }

                dispatcher.Dispatch(new LoadEventsPartialAction(logData, delta.AsReadOnly()));
            },
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(3));

        bool renderXml = _eventLogState.Value.AppliedFilter.RequiresXml;

        using var reader = new EventLogReader(action.LogName, action.LogPathType, renderXml);

        var producerTask = Task.Run(async () =>
        {
            try
            {
                while (reader.TryGetEvents(out EventRecord[] batch))
                {
                    token.ThrowIfCancellationRequested();

                    if (batch.Length == 0) { continue; }

                    await channel.Writer.WriteAsync(batch, token);
                }
            }
            catch (Exception ex)
            {
                channel.Writer.Complete(ex);

                throw;
            }

            channel.Writer.Complete();
        }, token);

        try
        {
            await Parallel.ForEachAsync(
                channel.Reader.ReadAllAsync(token),
                new ParallelOptions
                {
                    CancellationToken = token,
                    MaxDegreeOfParallelism = s_maxGlobalConcurrency
                },
                async (batch, innerToken) =>
                {
                    await s_resolutionThrottle.WaitAsync(innerToken);

                    try
                    {
                        List<ResolvedEvent> localBatch = new(batch.Length);
                        int localResolved = 0;

                        foreach (var @event in batch)
                        {
                            innerToken.ThrowIfCancellationRequested();

                            try
                            {
                                if (!@event.IsSuccess)
                                {
                                    Interlocked.Increment(ref failed);

                                    _logger.Warning($"{@event.PathName}: Bad Event: {@event.Error}");

                                    continue;
                                }

                                localBatch.Add(eventResolver.ResolveEvent(@event));
                                localResolved++;
                            }
                            catch (Exception ex)
                            {
                                _logger.Warning($"Failed to resolve RecordId: {@event.RecordId}, {ex.Message}");
                            }
                        }

                        if (localBatch.Count > 0)
                        {
                            lock (events) { events.AddRange(localBatch); }

                            Interlocked.Add(ref resolved, localResolved);
                        }
                    }
                    finally
                    {
                        s_resolutionThrottle.Release();
                    }
                });

            await producerTask;

            lastEvent = reader.LastBookmark;
        }
        catch (OperationCanceledException)
        {
            await EventLogEffectsUtility.StopProducerAsync(producerTask);

            _closeCoordinator.ClearPendingRestore(logData.Name);

            _logger.Trace($"Open '{action.LogName}': canceled after {stopwatch.ElapsedMilliseconds}ms ({Volatile.Read(ref resolved)} resolved, {Volatile.Read(ref failed)} failed).");

            if (_eventLogState.Value.OpenLogs.TryGetValue(logData.Name, out var currentLog)
                && currentLog.Id == logData.Id)
            {
                dispatcher.Dispatch(new CloseLogAction(logData.Id, logData.Name));
            }

            dispatcher.Dispatch(new ClearStatusAction(activityId));

            return;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to load log {action.LogName} after {stopwatch.ElapsedMilliseconds}ms: {ex.Message}");

            await EventLogEffectsUtility.StopProducerAsync(producerTask);

            _closeCoordinator.ClearPendingRestore(logData.Name);

            if (_eventLogState.Value.OpenLogs.TryGetValue(logData.Name, out var currentLog)
                && currentLog.Id == logData.Id)
            {
                dispatcher.Dispatch(new CloseLogAction(logData.Id, logData.Name));
            }

            dispatcher.Dispatch(new ClearStatusAction(activityId));
            dispatcher.Dispatch(new SetResolverStatusAction($"Error: Failed to load {action.LogName}"));

            return;
        }

        // Stop the timer before sorting/dispatching to prevent a stale
        // LoadEventsPartialAction from being dispatched after the final LoadEventsAction.
        // DisposeAsync waits for any in-flight callback to complete; the second disposal
        // at await-using scope exit is idempotent.
        await timer.DisposeAsync();

        events.Sort((a, b) => Comparer<long?>.Default.Compare(b.RecordId, a.RecordId));

        token.ThrowIfCancellationRequested();

        if (!_eventLogState.Value.OpenLogs.TryGetValue(logData.Name, out var activeLog)
            || activeLog.Id != logData.Id)
        {
            _logger.Trace($"Open '{action.LogName}': log was closed or replaced after producer completed; discarding {events.Count} resolved events after {stopwatch.ElapsedMilliseconds}ms.");

            _closeCoordinator.ClearPendingRestore(logData.Name);

            return;
        }

        if (renderXml)
        {
            _concurrencyState.MarkLoadedWithXml(logData.Id);
        }

        dispatcher.Dispatch(new LoadEventsAction(logData, events.AsReadOnly()));

        dispatcher.Dispatch(new SetEventsLoadingAction(activityId, 0, 0));

        if (action.LogPathType == LogPathType.Channel)
        {
            _logWatcherService.AddLog(action.LogName, lastEvent, renderXml);
        }

        dispatcher.Dispatch(new SetResolverStatusAction(string.Empty));

        _logger.Information($"Loaded '{action.LogName}': {events.Count} events ({failed} failed) in {stopwatch.ElapsedMilliseconds}ms.");
    }
}
