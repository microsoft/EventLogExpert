// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.EventTable;
using EventLogExpert.UI.Store.FilterPane;
using EventLogExpert.UI.Store.StatusBar;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Security;
using System.Threading.Channels;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.UI.Store.EventLog;

public sealed class EventLogEffects(
    IState<EventLogState> eventLogState,
    IFilterService filterService,
    ITraceLogger logger,
    ILogWatcherService logWatcherService,
    IEventResolverCache resolverCache,
    IServiceScopeFactory serviceScopeFactory)
{
    private static readonly int s_maxGlobalConcurrency = Math.Max(1, Environment.ProcessorCount - 1);
    private static readonly SemaphoreSlim s_resolutionThrottle = new(s_maxGlobalConcurrency, s_maxGlobalConcurrency);

    private readonly IState<EventLogState> _eventLogState = eventLogState;
    private readonly IFilterService _filterService = filterService;
    private readonly Lock _globalCtsLock = new();
    private readonly ConcurrentDictionary<EventLogId, CancellationTokenSource> _logCts = new();
    private readonly ITraceLogger _logger = logger;
    private readonly ILogWatcherService _logWatcherService = logWatcherService;
    private readonly IEventResolverCache _resolverCache = resolverCache;
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;

    private long _cancelGeneration;
    private CancellationTokenSource _globalCts = new();

    [EffectMethod]
    public Task HandleAddEvent(EventLogAction.AddEvent action, IDispatcher dispatcher)
    {
        // Sometimes the watcher doesn't stop firing events immediately. Let's
        // make sure the events being added are for a log that is still "open".
        if (!_eventLogState.Value.ActiveLogs.ContainsKey(action.NewEvent.OwningLog)) { return Task.CompletedTask; }

        if (_eventLogState.Value.ContinuouslyUpdate)
        {
            var activeLogs = DistributeEventsToManyLogs(
                _eventLogState.Value.ActiveLogs,
                [action.NewEvent]);

            var filteredActiveLogs = _filterService.FilterActiveLogs(activeLogs.Values, _eventLogState.Value.AppliedFilter);

            dispatcher.Dispatch(new EventLogAction.AddEventSuccess(activeLogs));
            dispatcher.Dispatch(new EventTableAction.UpdateDisplayedEvents(filteredActiveLogs));
        }
        else
        {
            var updatedBuffer = new List<DisplayEventModel>(_eventLogState.Value.NewEventBuffer.Count + 1)
            {
                action.NewEvent
            };

            updatedBuffer.AddRange(_eventLogState.Value.NewEventBuffer);

            var full = updatedBuffer.Count >= EventLogState.MaxNewEvents;

            dispatcher.Dispatch(new EventLogAction.AddEventBuffered(updatedBuffer.AsReadOnly(), full));
        }

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(EventLogAction.CloseAll))]
    public Task HandleCloseAll(IDispatcher dispatcher)
    {
        CancelAllLoads();

        _logWatcherService.RemoveAll();

        dispatcher.Dispatch(new EventTableAction.CloseAll());
        dispatcher.Dispatch(new StatusBarAction.CloseAll());

        _resolverCache.ClearAll();

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleCloseLog(EventLogAction.CloseLog action, IDispatcher dispatcher)
    {
        // Only cancel — don't remove or dispose. HandleOpenLog's finally block
        // owns CTS disposal after the load has fully unwound, avoiding a race
        // where disposal here causes ObjectDisposedException in the running load.
        if (_logCts.TryGetValue(action.LogId, out var cts))
        {
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        _logWatcherService.RemoveLog(action.LogName);

        dispatcher.Dispatch(new EventTableAction.CloseLog(action.LogId));

        if (_eventLogState.Value.ActiveLogs.IsEmpty)
        {
            _resolverCache.ClearAll();
        }

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleLoadEvents(EventLogAction.LoadEvents action, IDispatcher dispatcher)
    {
        var filteredEvents = _filterService.GetFilteredEvents(action.Events, _eventLogState.Value.AppliedFilter);

        dispatcher.Dispatch(new EventTableAction.UpdateTable(action.LogData.Id, filteredEvents));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleLoadEventsPartial(EventLogAction.LoadEventsPartial action, IDispatcher dispatcher)
    {
        var filteredEvents = _filterService.GetFilteredEvents(action.Events, _eventLogState.Value.AppliedFilter);

        dispatcher.Dispatch(new EventTableAction.AppendTableEvents(action.LogData.Id, filteredEvents));

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(EventLogAction.LoadNewEvents))]
    public Task HandleLoadNewEvents(IDispatcher dispatcher)
    {
        ProcessNewEventBuffer(_eventLogState.Value, dispatcher);

        return Task.CompletedTask;
    }

    [EffectMethod]
    public async Task HandleOpenLog(EventLogAction.OpenLog action, IDispatcher dispatcher)
    {
        // Snapshot the cancel generation before any state checks. If CancelAllLoads
        // increments this before we finish linking, we know our load must be canceled
        // (it may have linked to the post-swap uncanceled global CTS).
        long startGeneration = Volatile.Read(ref _cancelGeneration);

        using var serviceScope = _serviceScopeFactory.CreateScope();

        var eventResolver = serviceScope.ServiceProvider.GetService<IEventResolver>();

        if (eventResolver is null)
        {
            dispatcher.Dispatch(new StatusBarAction.SetResolverStatus("Error: No event resolver available"));

            return;
        }

        if (!_eventLogState.Value.ActiveLogs.TryGetValue(action.LogName, out var logData))
        {
            dispatcher.Dispatch(new StatusBarAction.SetResolverStatus($"Error: Failed to open {action.LogName}"));

            return;
        }

        // Create a per-load CTS linked to the global CTS and the caller's token so that
        // either individual HandleCloseLog, a global cancel (CloseAll), or the caller can
        // stop this load. The lock ensures we always link to the current global CTS.
        // Not using `using` — disposal is handled by the finally block below.
        CancellationTokenSource perLoadCts;

        using (_globalCtsLock.EnterScope())
        {
            perLoadCts = CancellationTokenSource.CreateLinkedTokenSource(_globalCts.Token, action.Token);
        }

        _logCts[logData.Id] = perLoadCts;

        // If CancelAllLoads ran between our generation snapshot and now, we may have
        // linked to the new (uncanceled) global CTS after the swap. CancelAllLoads's
        // _logCts iteration may also have missed us. Cancel ourselves to be safe.
        if (Volatile.Read(ref _cancelGeneration) != startGeneration)
        {
            try { perLoadCts.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        var token = perLoadCts.Token;

        try
        {
            await LoadLogAsync(action, logData, eventResolver, serviceScope, dispatcher, token);
        }
        finally
        {
            // This finally block is the sole owner of per-load CTS removal and
            // disposal. Cancel/close paths (HandleCloseLog, CancelAllLoads) only
            // request cancellation — they never remove or dispose entries.
            if (_logCts.TryRemove(logData.Id, out var removedCts))
            {
                removedCts.Dispose();
            }
        }
    }

    [EffectMethod]
    public Task HandleSetContinuouslyUpdate(EventLogAction.SetContinuouslyUpdate action, IDispatcher dispatcher)
    {
        if (action.ContinuouslyUpdate)
        {
            ProcessNewEventBuffer(_eventLogState.Value, dispatcher);
        }

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleSetFilters(EventLogAction.SetFilters action, IDispatcher dispatcher)
    {
        var filteredActiveLogs = _filterService.FilterActiveLogs(_eventLogState.Value.ActiveLogs.Values, action.EventFilter);

        dispatcher.Dispatch(new EventTableAction.UpdateDisplayedEvents(filteredActiveLogs));

        return Task.CompletedTask;
    }

    /// <summary>Adds new events to the currently opened log</summary>
    private static EventLogData AddEventsToOneLog(EventLogData logData, List<DisplayEventModel> eventsToAdd)
    {
        eventsToAdd.AddRange(logData.Events);

        return logData with { Events = eventsToAdd.AsReadOnly() };
    }

    private static ImmutableDictionary<string, EventLogData> DistributeEventsToManyLogs(
        ImmutableDictionary<string, EventLogData> logsToUpdate,
        IEnumerable<DisplayEventModel> eventsToDistribute)
    {
        // Group events by owning log once to avoid repeated enumeration
        var eventsByLog = new Dictionary<string, List<DisplayEventModel>>();

        foreach (var e in eventsToDistribute)
        {
            if (!logsToUpdate.ContainsKey(e.OwningLog)) { continue; }

            if (!eventsByLog.TryGetValue(e.OwningLog, out var list))
            {
                list = [];
                eventsByLog[e.OwningLog] = list;
            }

            list.Add(e);
        }

        var newLogs = logsToUpdate;

        foreach (var (logName, newEvents) in eventsByLog)
        {
            var log = logsToUpdate[logName];
            var newLogData = AddEventsToOneLog(log, newEvents);
            newLogs = newLogs.SetItem(logName, newLogData);
        }

        return newLogs;
    }

    /// <summary>
    ///     Awaits the producer task, suppressing all exceptions.
    ///     The sole purpose is to ensure the producer has fully stopped before
    ///     the reader is disposed. Any meaningful errors are handled by the caller.
    /// </summary>
    private static async Task StopProducerAsync(Task producerTask)
    {
        try { await producerTask; }
        catch { /* Intentionally swallowed — caller handles error reporting */ }
    }

    private void CancelAllLoads()
    {
        CancellationTokenSource oldGlobalCts;

        // Atomically swap the global CTS so any HandleOpenLog that links after
        // this point gets a fresh (uncanceled) token, while loads already linked
        // to the old CTS are canceled. Increment the generation so that any
        // HandleOpenLog in the window between its ActiveLogs check and linking
        // can detect the cancel-all and abort.
        using (_globalCtsLock.EnterScope())
        {
            oldGlobalCts = _globalCts;
            _globalCts = new CancellationTokenSource();
            Interlocked.Increment(ref _cancelGeneration);
        }

        oldGlobalCts.Cancel();

        // Don't dispose oldGlobalCts here — per-load linked tokens still
        // reference it. It will be collected by GC after all linked tokens
        // are disposed by their HandleOpenLog finally blocks.

        // Cancel per-load tokens for immediate effect (redundant when they are
        // linked to oldGlobalCts, but ensures cancellation for edge cases).
        // Don't remove or dispose — HandleOpenLog's finally block owns cleanup
        // after each load has fully unwound, avoiding a race where disposal
        // here causes ObjectDisposedException in running async operations.
        foreach (var key in _logCts.Keys)
        {
            if (_logCts.TryGetValue(key, out var cts))
            {
                try { cts.Cancel(); }
                catch (ObjectDisposedException) { }
            }
        }
    }

    private async Task LoadLogAsync(
        EventLogAction.OpenLog action,
        EventLogData logData,
        IEventResolver eventResolver,
        IServiceScope serviceScope,
        IDispatcher dispatcher,
        CancellationToken token)
    {
        // Detect locale metadata files for exported logs.
        // Filesystem probing is best-effort — failures must not abort opening the log.
        if (action.PathType == PathType.FilePath)
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
                            _logger?.Info($"Using locale metadata from: {localeDir} ({mtaFiles.Length} file(s))");
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or SecurityException or ArgumentException or NotSupportedException)
            {
                _logger?.Warn($"Failed to probe locale metadata for {action.LogName}: {ex.Message}");
            }
        }

        var filterState = serviceScope.ServiceProvider.GetService<IState<FilterPaneState>>();

        var activityId = Guid.NewGuid();
        string? lastEvent;
        int failed = 0;
        int resolved = 0;
        int lastPartialIndex = 0;
        int timerTick = 0;

        dispatcher.Dispatch(new EventTableAction.AddTable(logData));

        var channel = Channel.CreateBounded<EventRecord[]>(new BoundedChannelOptions(s_maxGlobalConcurrency * 2)
        {
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        List<DisplayEventModel> events = [];

        await using var timer = new Timer(
            _ =>
            {
                dispatcher.Dispatch(new StatusBarAction.SetEventsLoading(activityId, Volatile.Read(ref resolved), Volatile.Read(ref failed)));

                // Skip the immediate first tick (dueTime = 0) so the first partial
                // is dispatched after ~3 seconds of loading.
                if (Interlocked.Increment(ref timerTick) <= 1) { return; }

                List<DisplayEventModel> delta;

                lock (events)
                {
                    int fromIndex = Volatile.Read(ref lastPartialIndex);

                    if (events.Count <= fromIndex) { return; }

                    delta = events.GetRange(fromIndex, events.Count - fromIndex);
                    Volatile.Write(ref lastPartialIndex, events.Count);
                }

                dispatcher.Dispatch(new EventLogAction.LoadEventsPartial(logData, delta.AsReadOnly()));
            },
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(3));

        using var reader = new EventLogReader(action.LogName, action.PathType, filterState?.Value.IsXmlEnabled ?? false);

        // Producer: single thread reads batches from EventLogReader
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
            // Consumers: parallel resolution of event batches from the channel.
            // The global semaphore limits total concurrent resolution threads across
            // all HandleOpenLog calls, preventing CPU saturation when loading multiple logs.
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
                        List<DisplayEventModel> localBatch = new(batch.Length);
                        int localResolved = 0;

                        foreach (var @event in batch)
                        {
                            innerToken.ThrowIfCancellationRequested();

                            try
                            {
                                if (!@event.IsSuccess)
                                {
                                    Interlocked.Increment(ref failed);

                                    _logger?.Warn($"{@event.PathName}: Bad Event: {@event.Error}");

                                    continue;
                                }

                                localBatch.Add(eventResolver.ResolveEvent(@event));
                                localResolved++;
                            }
                            catch (Exception ex)
                            {
                                _logger?.Warn($"Failed to resolve RecordId: {@event.RecordId}, {ex.Message}");
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
            await StopProducerAsync(producerTask);

            // Only close the log if it still exists with the same ID —
            // prevents stale cancellation from closing a newly reopened log.
            if (_eventLogState.Value.ActiveLogs.TryGetValue(logData.Name, out var currentLog)
                && currentLog.Id == logData.Id)
            {
                dispatcher.Dispatch(new EventLogAction.CloseLog(logData.Id, logData.Name));
            }

            dispatcher.Dispatch(new StatusBarAction.ClearStatus(activityId));

            return;
        }
        catch (Exception ex)
        {
            _logger?.Error($"Failed to load log {action.LogName}: {ex.Message}");

            await StopProducerAsync(producerTask);

            // Only close the log if it still exists with the same ID.
            if (_eventLogState.Value.ActiveLogs.TryGetValue(logData.Name, out var currentLog)
                && currentLog.Id == logData.Id)
            {
                dispatcher.Dispatch(new EventLogAction.CloseLog(logData.Id, logData.Name));
            }

            dispatcher.Dispatch(new StatusBarAction.ClearStatus(activityId));
            dispatcher.Dispatch(new StatusBarAction.SetResolverStatus($"Error: Failed to load {action.LogName}"));

            return;
        }

        events.Sort((a, b) => Comparer<long?>.Default.Compare(b.RecordId, a.RecordId));

        // Re-check cancellation and log identity before committing results —
        // a close may have arrived after the producer/consumer loop completed.
        token.ThrowIfCancellationRequested();

        if (!_eventLogState.Value.ActiveLogs.TryGetValue(logData.Name, out var activeLog)
            || activeLog.Id != logData.Id)
        {
            return;
        }

        dispatcher.Dispatch(new EventLogAction.LoadEvents(logData, events.AsReadOnly()));

        dispatcher.Dispatch(new StatusBarAction.SetEventsLoading(activityId, 0, 0));

        if (action.PathType == PathType.LogName)
        {
            _logWatcherService.AddLog(action.LogName, lastEvent);
        }

        dispatcher.Dispatch(new StatusBarAction.SetResolverStatus(string.Empty));
    }

    private void ProcessNewEventBuffer(EventLogState state, IDispatcher dispatcher)
    {
        var activeLogs = DistributeEventsToManyLogs(state.ActiveLogs, state.NewEventBuffer);

        var filteredActiveLogs = _filterService.FilterActiveLogs(activeLogs.Values, state.AppliedFilter);

        dispatcher.Dispatch(new EventTableAction.UpdateDisplayedEvents(filteredActiveLogs));
        dispatcher.Dispatch(new EventLogAction.AddEventSuccess(activeLogs));
        dispatcher.Dispatch(new EventLogAction.AddEventBuffered([], false));
    }
}
