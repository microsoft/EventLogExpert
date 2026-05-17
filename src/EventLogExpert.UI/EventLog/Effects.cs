// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Logging;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Filtering.Runtime;
using EventLogExpert.UI.Banner;
using EventLogExpert.UI.Common.Lifecycle;
using EventLogExpert.UI.Database;
using EventLogExpert.UI.FilterProgress;
using EventLogExpert.UI.Filters;
using EventLogExpert.UI.LogTable;
using EventLogExpert.UI.StatusBar;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Security;
using System.Threading.Channels;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.UI.EventLog;

internal sealed class Effects(
    IState<EventLogState> eventLogState,
    IFilterService filterService,
    ITraceLogger logger,
    ILogWatcherService logWatcherService,
    IEventResolverCache resolverCache,
    IEventXmlResolver xmlResolver,
    IServiceScopeFactory serviceScopeFactory,
    IDatabaseService databaseService,
    IBannerService bannerService,
    IDispatcher dispatcher) : ILogReloadCoordinator
{
    public static readonly TimeSpan LogCloseTimeout = TimeSpan.FromSeconds(30);

    private static readonly int s_maxGlobalConcurrency = Math.Max(1, Environment.ProcessorCount - 1);
    private static readonly SemaphoreSlim s_resolutionThrottle = new(s_maxGlobalConcurrency, s_maxGlobalConcurrency);

    private readonly IBannerService _bannerService = bannerService;
    private readonly IDatabaseService _databaseService = databaseService;
    private readonly IDispatcher _dispatcher = dispatcher;
    private readonly IState<EventLogState> _eventLogState = eventLogState;
    private readonly IFilterService _filterService = filterService;
    private readonly Lock _globalCtsLock = new();
    private readonly ConcurrentDictionary<EventLogId, TaskCompletionSource> _logCloseCompletions = new();
    private readonly SemaphoreSlim _logCloseCoordinatorLock = new(1, 1);
    private readonly ConcurrentDictionary<EventLogId, CancellationTokenSource> _logCts = new();
    private readonly ITraceLogger _logger = logger;
    private readonly ConcurrentDictionary<EventLogId, TaskCompletionSource> _logLoadCompletions = new();
    private readonly ConcurrentDictionary<EventLogId, byte> _logsLoadedWithXml = new();
    private readonly ILogWatcherService _logWatcherService = logWatcherService;
    private readonly ConcurrentDictionary<string, PendingSelectionRestore> _pendingSelectionRestore = new();
    private readonly IEventResolverCache _resolverCache = resolverCache;
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly IEventXmlResolver _xmlResolver = xmlResolver;

    private long _cancelGeneration;
    private long _filterGeneration;
    private CancellationTokenSource _globalCts = new();

    [EffectMethod]
    public Task HandleAddEvent(AddEventAction action, IDispatcher dispatcher)
    {
        // Sometimes the watcher doesn't stop firing events immediately. Let's
        // make sure the events being added are for a log that is still "open".
        if (!_eventLogState.Value.ActiveLogs.ContainsKey(action.NewEvent.OwningLog)) { return Task.CompletedTask; }

        if (_eventLogState.Value.ContinuouslyUpdate)
        {
            var activeLogs = DistributeEventsToManyLogs(
                _eventLogState.Value.ActiveLogs,
                [action.NewEvent]);

            dispatcher.Dispatch(new AddEventSuccessAction(activeLogs));

            // Filter just the new event and append to the table; previous displayed
            // events are unchanged so a full re-filter is unnecessary.
            var filteredNew = _filterService.GetFilteredEvents(
                [action.NewEvent],
                _eventLogState.Value.AppliedFilter);

            if (filteredNew.Count > 0 &&
                activeLogs.TryGetValue(action.NewEvent.OwningLog, out var owningLog))
            {
                dispatcher.Dispatch(new AppendTableEventsAction(owningLog.Id, filteredNew));
            }
        }
        else
        {
            var updatedBuffer = new List<ResolvedEvent>(_eventLogState.Value.NewEventBuffer.Count + 1)
            {
                action.NewEvent
            };

            updatedBuffer.AddRange(_eventLogState.Value.NewEventBuffer);

            var full = updatedBuffer.Count >= EventLogState.MaxNewEvents;

            dispatcher.Dispatch(new EventBufferedAction(updatedBuffer.AsReadOnly(), full));
        }

        return Task.CompletedTask;
    }

    [EffectMethod]
    public async Task HandleApplyFilter(ApplyFilterAction action, IDispatcher dispatcher)
    {
        bool newRequiresXml = action.Filter.RequiresXml;

        // Identify open logs that lack pre-rendered XML. Disabling/removing an XML filter is a
        // no-op: XML already in memory is harmless. Re-enabling against logs that were
        // previously loaded with XML is also a no-op for the same reason. Only logs that
        // are actually missing XML need to be re-read; logs that already have it are left
        // intact to avoid unnecessary reloads (and the selection-loss / latency they cause).
        var logsNeedingReload = newRequiresXml && !_eventLogState.Value.ActiveLogs.IsEmpty
            ? _eventLogState.Value.ActiveLogs.Values
                .Where(d => !_logsLoadedWithXml.ContainsKey(d.Id))
                .Select(d => (d.Id, d.Name, d.Type))
                .ToList()
            : [];

        // Supersede any in-flight filter-only run. The Fluxor reducer for this action already
        // updated AppliedFilter, so an older filter-only run is now working against a stale
        // filter and must not publish UpdateDisplayedEvents on top of the new one — bump the
        // generation here (not only in the filter-only branch) so the reload path is also a
        // valid supersede event.
        long generation = Interlocked.Increment(ref _filterGeneration);

        if (logsNeedingReload.Count > 0)
        {
            // The reload path doesn't run a filter pass; per-table IsLoading takes over while
            // the closed logs re-read with XML. Clear any leftover filter-only spinner from
            // a superseded run so the UI doesn't show stuck "filtering".
            dispatcher.Dispatch(new SetFilterProgressAction(false));

            // TODO: WITH-XML logs that aren't reloaded keep their (now-stale) DisplayedEvents
            // slice. A separate follow-up commit should also publish a filter pass for those
            // logs so the UI reflects the new filter without waiting for a live event arrival.
            await ReloadLogsWithXmlAsync(logsNeedingReload, dispatcher);

            return;
        }

        await ApplyFilterAndPublishAsync(action.Filter, generation, dispatcher);
    }

    private async Task ReloadLogsWithXmlAsync(
        List<(EventLogId Id, string Name, LogPathType Type)> logsNeedingReload,
        IDispatcher dispatcher)
    {
        var reloadNames = logsNeedingReload.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        var selectionByLog = _eventLogState.Value.SelectedEvents
            .Where(e => e.RecordId.HasValue && reloadNames.Contains(e.OwningLog))
            .GroupBy(e => e.OwningLog)
            .ToDictionary(g => g.Key, g => (IReadOnlySet<long>)g.Select(e => e.RecordId!.Value).ToHashSet());

        var selectedEvent = _eventLogState.Value.SelectedEvent;
        long? selectedRecordId = selectedEvent?.RecordId;
        string? selectedLogName = selectedEvent?.OwningLog;

        // The focused row may live in a reloading log even when no rows of
        // that log are selected (Explorer-style cursor without selection).
        // Ensure a pending entry exists so HandleLoadEvents can restore focus.
        if (selectedRecordId.HasValue
            && !string.IsNullOrEmpty(selectedLogName)
            && reloadNames.Contains(selectedLogName)
            && !selectionByLog.ContainsKey(selectedLogName))
        {
            selectionByLog[selectedLogName] = new HashSet<long>();
        }

        // Close + reopen only the logs that lack XML. Other open logs keep their state.
        // HandleCloseLog clears _pendingSelectionRestore for the log name as part of its
        // async cleanup (after awaiting watcher shutdown). Pre-register a close-completion
        // TCS for each log, dispatch the closes, then AWAIT each completion before
        // populating the restore map — otherwise an in-flight HandleCloseLog can wipe the
        // freshly-written entry and silently lose the user's selection.
        //
        // The semaphore serializes this path with PrepareForDatabaseRemovalAsync: both
        // write into _logCloseCompletions with raw assignment, so concurrent overlap on
        // the same log id would orphan the first caller's TCS (HandleCloseLog signals
        // whichever entry happens to be in the dict at the time).
        await _logCloseCoordinatorLock.WaitAsync();

        try
        {
            var closeWaiters = new List<(EventLogId Id, string Name, Task Task)>(logsNeedingReload.Count);

            foreach (var (id, name, _) in logsNeedingReload)
            {
                var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _logCloseCompletions[id] = tcs;
                closeWaiters.Add((id, name, tcs.Task));
            }

            foreach (var (id, name, _) in logsNeedingReload)
            {
                dispatcher.Dispatch(new CloseLogAction(id, name));
            }

            var timedOutLogs = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (id, name, task) in closeWaiters)
            {
                try
                {
                    await task.WaitAsync(LogCloseTimeout);
                }
                catch (TimeoutException)
                {
                    _logCloseCompletions.TryRemove(id, out _);
                    timedOutLogs.Add(name);
                    _logger.Trace($"{nameof(HandleApplyFilter)}: close for log '{name}' did not complete within {LogCloseTimeout}; selection will not be restored to avoid race with the delayed close wiping the entry.");
                }
            }

            foreach (var (name, ids) in selectionByLog)
            {
                if (timedOutLogs.Contains(name))
                {
                    // The delayed HandleCloseLog will eventually call _pendingSelectionRestore
                    // .TryRemove(name) — writing here would be silently wiped. Skip.
                    continue;
                }

                long? selectedIdForLog = string.Equals(name, selectedLogName, StringComparison.Ordinal) ? selectedRecordId : null;
                _pendingSelectionRestore[name] = new PendingSelectionRestore(ids, selectedIdForLog);
            }

            foreach (var (_, name, type) in logsNeedingReload)
            {
                dispatcher.Dispatch(new OpenLogAction(name, type));
            }
        }
        finally
        {
            _logCloseCoordinatorLock.Release();
        }
    }

    private async Task ApplyFilterAndPublishAsync(Filter filter, long generation, IDispatcher dispatcher)
    {
        var activeLogsSnapshot = _eventLogState.Value.ActiveLogs.Values.ToList();

        dispatcher.Dispatch(new SetFilterProgressAction(true));

        try
        {
            var filteredActiveLogs = await Task.Run(
                () => _filterService.FilterActiveLogs(activeLogsSnapshot, filter));

            if (Interlocked.Read(ref _filterGeneration) != generation) { return; }

            // Pass 1: keep slices whose source Events ref is still current. Logs whose
            // Events ref changed (live event arrived during the filter run) get re-filtered
            // once below so the new filter is applied to the post-mutation rows too.
            var snapshotById = activeLogsSnapshot.ToDictionary(d => d.Id);
            var currentByName = _eventLogState.Value.ActiveLogs;
            var fresh = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>(filteredActiveLogs.Count);
            var staleLogs = new List<EventLogData>();

            foreach (var (logId, filteredEvents) in filteredActiveLogs)
            {
                if (!snapshotById.TryGetValue(logId, out var snapshotData)) { continue; }

                if (!currentByName.TryGetValue(snapshotData.Name, out var currentData)) { continue; }

                if (ReferenceEquals(snapshotData.Events, currentData.Events))
                {
                    fresh[logId] = filteredEvents;
                }
                else
                {
                    staleLogs.Add(currentData);
                }
            }

            // Pass 2: single retry. If a log is still stale after this pass, leave it omitted —
            // the reducer preserves existing rows so live events are not lost.
            if (staleLogs.Count > 0)
            {
                var refiltered = await Task.Run(
                    () => _filterService.FilterActiveLogs(staleLogs, filter));

                if (Interlocked.Read(ref _filterGeneration) != generation) { return; }

                var pass2InputById = staleLogs.ToDictionary(d => d.Id);
                currentByName = _eventLogState.Value.ActiveLogs;

                foreach (var (logId, filteredEvents) in refiltered)
                {
                    if (!pass2InputById.TryGetValue(logId, out var pass2Input)) { continue; }

                    if (!currentByName.TryGetValue(pass2Input.Name, out var nowCurrent)) { continue; }

                    // pass2Input.Events IS the list ref pass 2 consumed; new ref = stale.
                    if (ReferenceEquals(pass2Input.Events, nowCurrent.Events))
                    {
                        fresh[logId] = filteredEvents;
                    }
                }
            }

            dispatcher.Dispatch(new UpdateDisplayedEventsAction(fresh));
        }
        finally
        {
            if (Interlocked.Read(ref _filterGeneration) == generation)
            {
                dispatcher.Dispatch(new SetFilterProgressAction(false));
            }
        }
    }

    [EffectMethod(typeof(CloseAllLogsAction))]
    public async Task HandleCloseAll(IDispatcher dispatcher)
    {
        _logger.Trace($"{nameof(HandleCloseAll)} requested ({_eventLogState.Value.ActiveLogs.Count} active logs).");

        CancelAllLoads();

        _resolverCache.ClearAll();
        _xmlResolver.ClearAll();
        _logsLoadedWithXml.Clear();
        _pendingSelectionRestore.Clear();

        await _logWatcherService.RemoveAllAsync();
    }

    [EffectMethod]
    public async Task HandleCloseLog(CloseLogAction action, IDispatcher dispatcher)
    {
        _logger.Trace($"{nameof(HandleCloseLog)} requested for '{action.LogName}' (id: {action.LogId}).");

        try
        {
            if (_logCts.TryGetValue(action.LogId, out var cts))
            {
                try { cts.Cancel(); }
                catch (ObjectDisposedException) { /* CTS already disposed; cancel is moot. */ }
            }

            // Wait for the load task to fully unwind. Cancellation only requests stop;
            // LoadLogAsync's service scope (which owns the IEventResolver and its SQLite
            // handles) is not disposed until HandleOpenLog's outer using/finally runs.
            // Defensive timeout so a wedged load can't deadlock close forever.
            if (_logLoadCompletions.TryGetValue(action.LogId, out var loadCompletion))
            {
                try
                {
                    await loadCompletion.Task.WaitAsync(LogCloseTimeout);
                }
                catch (TimeoutException)
                {
                    _logger.Trace($"{nameof(HandleCloseLog)}: load task for '{action.LogName}' did not unwind within {LogCloseTimeout}.");
                }
            }

            // Drain watcher callbacks. RemoveLogAsync's Task only completes after
            // EventLogWatcher.Unsubscribe blocks for all in-flight ReadAndRaiseEvents
            // callbacks → all per-event resolver scopes disposed → all DbContexts
            // disposed → connections returned to the SQLite pool.
            await _logWatcherService.RemoveLogAsync(action.LogName);

            _logsLoadedWithXml.TryRemove(action.LogId, out _);
            _pendingSelectionRestore.TryRemove(action.LogName, out _);

            // Drop any cached XML for this log; if the same name reopens later (e.g., post-rotation)
            // a fresh resolve must occur instead of returning stale text from a different file.
            _xmlResolver.ClearXmlCacheForLog(action.LogName);

            dispatcher.Dispatch(new LogTable.CloseLogAction(action.LogId));

            if (_eventLogState.Value.ActiveLogs.IsEmpty)
            {
                _resolverCache.ClearAll();
            }
        }
        finally
        {
            // Signal the coordinator (if it pre-registered for this log id) that all close
            // work — load-await + watcher-drain + side effects — is done. Removing keeps
            // the dictionary from leaking entries from non-coordinator close paths.
            if (_logCloseCompletions.TryRemove(action.LogId, out var closeCompletion))
            {
                closeCompletion.TrySetResult();
            }
        }
    }

    [EffectMethod]
    public Task HandleLoadEvents(LoadEventsAction action, IDispatcher dispatcher)
    {
        var filteredEvents = _filterService.GetFilteredEvents(action.Events, _eventLogState.Value.AppliedFilter);

        dispatcher.Dispatch(new UpdateTableAction(action.LogData.Id, filteredEvents));

        // Restore selection if this load was triggered by a filter-driven reload.
        // A pending entry can carry a focused row (SelectedId) without any selected
        // rows (SelectedIds), so we must keep going as long as either is present.
        if (!_pendingSelectionRestore.TryRemove(action.LogData.Name, out var pending)
            || (pending.SelectedIds.Count <= 0 && !pending.SelectedId.HasValue))
        {
            return Task.CompletedTask;
        }

        var restored = action.Events
            .Where(e => e.RecordId.HasValue && pending.SelectedIds.Contains(e.RecordId.Value))
            .ToList();

        ResolvedEvent? selectedRestored = pending.SelectedId.HasValue
            ? action.Events.FirstOrDefault(e => e.RecordId == pending.SelectedId.Value)
            : null;

        if (restored.Count <= 0 && selectedRestored is null) { return Task.CompletedTask; }

        // Use SetSelectedEvents (not SelectEvents) so we can restore both
        // selection and the active focus row atomically.
        var focused = selectedRestored ?? (restored.Count > 0 ? restored[^1] : null);
        dispatcher.Dispatch(new SetSelectedEventsAction(restored, focused));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleLoadEventsPartial(LoadEventsPartialAction action, IDispatcher dispatcher)
    {
        var filteredEvents = _filterService.GetFilteredEvents(action.Events, _eventLogState.Value.AppliedFilter);

        dispatcher.Dispatch(new AppendTableEventsAction(action.LogData.Id, filteredEvents));

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(LoadNewEventsAction))]
    public Task HandleLoadNewEvents(IDispatcher dispatcher)
    {
        ProcessNewEventBuffer(_eventLogState.Value, dispatcher);

        return Task.CompletedTask;
    }

    [EffectMethod]
    public async Task HandleOpenLog(OpenLogAction action, IDispatcher dispatcher)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.Information($"{nameof(HandleOpenLog)} '{action.LogName}' (path type: {action.LogPathType}).");

        long startGeneration = Volatile.Read(ref _cancelGeneration);

        if (!_eventLogState.Value.ActiveLogs.TryGetValue(action.LogName, out var logData))
        {
            _logger.Warning($"Open '{action.LogName}' aborted: log not found in ActiveLogs (no prior AddLog dispatch).");

            dispatcher.Dispatch(new SetResolverStatusAction($"Error: Failed to open {action.LogName}"));

            return;
        }

        CancellationTokenSource perLoadCts;

        using (_globalCtsLock.EnterScope())
        {
            perLoadCts = CancellationTokenSource.CreateLinkedTokenSource(_globalCts.Token, action.Token);
        }

        _logCts[logData.Id] = perLoadCts;
        _logLoadCompletions[logData.Id] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // If CancelAllLoads ran between our generation snapshot and now, we may have
        // linked to the new (uncanceled) global CTS after the swap. CancelAllLoads's
        // _logCts iteration may also have missed us. Cancel ourselves to be safe.
        if (Volatile.Read(ref _cancelGeneration) != startGeneration)
        {
            _logger.Trace($"Open '{action.LogName}': cancel generation changed during CTS link; aborting load.");

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

            if (!_eventLogState.Value.ActiveLogs.TryGetValue(action.LogName, out var current)
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
                _bannerService.ReportCritical(ex);

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

            // Signal load completion AFTER the using/finally above has disposed the
            // service scope. HandleCloseLog awaits this TCS so it can guarantee the
            // resolver and its SQLite handles are released before the watcher is
            // drained and the file is deleted.
            if (_logLoadCompletions.TryRemove(logData.Id, out var loadCompletion))
            {
                loadCompletion.TrySetResult();
            }
        }
    }

    [EffectMethod]
    public Task HandleSetContinuouslyUpdate(SetContinuouslyUpdateAction action, IDispatcher dispatcher)
    {
        if (action.ContinuouslyUpdate)
        {
            ProcessNewEventBuffer(_eventLogState.Value, dispatcher);
        }

        return Task.CompletedTask;
    }

    public async Task PrepareForDatabaseRemovalAsync(LogReopenSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var activeLogs = _eventLogState.Value.ActiveLogs.Values.ToList();

        if (activeLogs.Count == 0) { return; }

        // Serialize with HandleApplyFilter' XML-reload path (see _logCloseCoordinatorLock
        // doc-comment). Without this lock a concurrent reload-on-XML-filter could overwrite
        // our pre-registered TCSes (or vice versa), orphaning awaiters until they hit the
        // 30s timeout.
        await _logCloseCoordinatorLock.WaitAsync(cancellationToken);

        try
        {
            // Snapshot selection state BEFORE dispatching any CloseLog action — ReduceCloseLog
            // synchronously wipes SelectedEvents/SelectedEvent from state, so reading them after
            // the dispatch loses everything.
            var reloadNames = activeLogs.Select(l => l.Name).ToHashSet(StringComparer.Ordinal);

            var selectionByLog = _eventLogState.Value.SelectedEvents
                .Where(e => e.RecordId.HasValue && reloadNames.Contains(e.OwningLog))
                .GroupBy(e => e.OwningLog)
                .ToDictionary(g => g.Key, g => (IReadOnlySet<long>)g.Select(e => e.RecordId!.Value).ToHashSet());

            var selectedRecordId = _eventLogState.Value.SelectedEvent?.RecordId;
            var selectedLogName = _eventLogState.Value.SelectedEvent?.OwningLog;

            if (selectedRecordId.HasValue
                && !string.IsNullOrEmpty(selectedLogName)
                && reloadNames.Contains(selectedLogName)
                && !selectionByLog.ContainsKey(selectedLogName))
            {
                selectionByLog[selectedLogName] = new HashSet<long>();
            }

            // Pre-register the close-completion TCSes BEFORE dispatching, so HandleCloseLog
            // is guaranteed to find an entry to signal (the dispatcher might queue HandleCloseLog
            // before this method's next statement runs).
            var waiters = new List<(EventLogId Id, string Name, LogPathType Type, Task Task)>(activeLogs.Count);

            foreach (var log in activeLogs)
            {
                var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _logCloseCompletions[log.Id] = tcs;
                waiters.Add((log.Id, log.Name, log.Type, tcs.Task));
            }

            foreach (var (id, name, _, _) in waiters)
            {
                _dispatcher.Dispatch(new CloseLogAction(id, name));
            }

            // Await completion per-log so we can populate the snapshot incrementally. If a later
            // log times out or cancels, the caller still gets the prefix that closed cleanly and
            // can reopen them in the finally block.
            foreach (var (id, name, type, task) in waiters)
            {
                try
                {
                    await task.WaitAsync(LogCloseTimeout, cancellationToken);

                    snapshot.Add(new LogReopenInfo(name, type));
                }
                catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
                {
                    // The TCS will never be signaled now (HandleCloseLog already removed itself
                    // from the dict if it ran, or won't find us if it didn't). Drop the stranded
                    // entry so the dictionary doesn't leak.
                    _logCloseCompletions.TryRemove(id, out _);
                    _logger.Trace($"{nameof(PrepareForDatabaseRemovalAsync)}: close for log '{name}' did not complete: {ex.GetType().Name}");

                    throw;
                }
            }

            // NOW write pending selection restores. HandleCloseLog wiped any prior entries
            // for these log names during its TryRemove(_pendingSelectionRestore) call, so we
            // know we're not racing with stale state.
            foreach (var (name, ids) in selectionByLog)
            {
                long? selectedIdForLog = string.Equals(name, selectedLogName, StringComparison.Ordinal) ? selectedRecordId : null;
                _pendingSelectionRestore[name] = new PendingSelectionRestore(ids, selectedIdForLog);
            }
        }
        finally
        {
            _logCloseCoordinatorLock.Release();
        }
    }

    public void ReopenAfterDatabaseRemoval(IReadOnlyList<LogReopenInfo> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        foreach (var entry in snapshot)
        {
            _dispatcher.Dispatch(new OpenLogAction(entry.Name, entry.Type));
        }
    }

    /// <summary>Adds new events to the currently opened log</summary>
    private static EventLogData AddEventsToOneLog(EventLogData logData, List<ResolvedEvent> eventsToAdd)
    {
        if (eventsToAdd.Count == 0) { return logData; }

        eventsToAdd.AddRange(logData.Events);

        return logData with { Events = eventsToAdd.AsReadOnly() };
    }

    private static ImmutableDictionary<string, EventLogData> DistributeEventsToManyLogs(
        ImmutableDictionary<string, EventLogData> logsToUpdate,
        IEnumerable<ResolvedEvent> eventsToDistribute)
    {
        // Group events by owning log once to avoid repeated enumeration
        var eventsByLog = new Dictionary<string, List<ResolvedEvent>>();

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
    ///     Awaits the producer task, suppressing all exceptions. The sole purpose is to ensure the producer has fully
    ///     stopped before the reader is disposed. Any meaningful errors are handled by the caller.
    /// </summary>
    private static async Task StopProducerAsync(Task producerTask)
    {
        try { await producerTask; }
        catch { /* Intentionally swallowed — caller handles error reporting. */ }
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
                            _logger?.Information($"Using locale metadata from: {localeDir} ({mtaFiles.Length} file(s))");
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or SecurityException or ArgumentException or NotSupportedException)
            {
                _logger?.Warning($"Failed to probe locale metadata for {action.LogName}: {ex.Message}");
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

                // Skip the immediate first tick (dueTime = 0) so the first partial
                // is dispatched after ~3 seconds of loading.
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

                                    _logger?.Warning($"{@event.PathName}: Bad Event: {@event.Error}");

                                    continue;
                                }

                                localBatch.Add(eventResolver.ResolveEvent(@event));
                                localResolved++;
                            }
                            catch (Exception ex)
                            {
                                _logger?.Warning($"Failed to resolve RecordId: {@event.RecordId}, {ex.Message}");
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

            _pendingSelectionRestore.TryRemove(logData.Name, out _);

            _logger?.Trace($"Open '{action.LogName}': canceled after {stopwatch.ElapsedMilliseconds}ms ({Volatile.Read(ref resolved)} resolved, {Volatile.Read(ref failed)} failed).");

            if (_eventLogState.Value.ActiveLogs.TryGetValue(logData.Name, out var currentLog)
                && currentLog.Id == logData.Id)
            {
                dispatcher.Dispatch(new CloseLogAction(logData.Id, logData.Name));
            }

            dispatcher.Dispatch(new ClearStatusAction(activityId));

            return;
        }
        catch (Exception ex)
        {
            _logger?.Error($"Failed to load log {action.LogName} after {stopwatch.ElapsedMilliseconds}ms: {ex.Message}");

            await StopProducerAsync(producerTask);

            _pendingSelectionRestore.TryRemove(logData.Name, out _);

            // Only close the log if it still exists with the same ID.
            if (_eventLogState.Value.ActiveLogs.TryGetValue(logData.Name, out var currentLog)
                && currentLog.Id == logData.Id)
            {
                dispatcher.Dispatch(new CloseLogAction(logData.Id, logData.Name));
            }

            dispatcher.Dispatch(new ClearStatusAction(activityId));
            dispatcher.Dispatch(new SetResolverStatusAction($"Error: Failed to load {action.LogName}"));

            return;
        }

        events.Sort((a, b) => Comparer<long?>.Default.Compare(b.RecordId, a.RecordId));

        token.ThrowIfCancellationRequested();

        if (!_eventLogState.Value.ActiveLogs.TryGetValue(logData.Name, out var activeLog)
            || activeLog.Id != logData.Id)
        {
            _logger?.Trace($"Open '{action.LogName}': log was closed or replaced after producer completed; discarding {events.Count} resolved events after {stopwatch.ElapsedMilliseconds}ms.");

            _pendingSelectionRestore.TryRemove(logData.Name, out _);

            return;
        }

        if (renderXml)
        {
            _logsLoadedWithXml[logData.Id] = 0;
        }

        dispatcher.Dispatch(new LoadEventsAction(logData, events.AsReadOnly()));

        dispatcher.Dispatch(new SetEventsLoadingAction(activityId, 0, 0));

        if (action.LogPathType == LogPathType.Channel)
        {
            _logWatcherService.AddLog(action.LogName, lastEvent, renderXml);
        }

        dispatcher.Dispatch(new SetResolverStatusAction(string.Empty));

        _logger?.Information($"Loaded '{action.LogName}': {events.Count} events ({failed} failed) in {stopwatch.ElapsedMilliseconds}ms.");
    }

    private void ProcessNewEventBuffer(EventLogState state, IDispatcher dispatcher)
    {
        var activeLogs = DistributeEventsToManyLogs(state.ActiveLogs, state.NewEventBuffer);

        dispatcher.Dispatch(new AddEventSuccessAction(activeLogs));

        // Group the buffered events by owning log id, filter each group, and dispatch
        // a single batched append so the combined-view reducer only fires once.
        var batched = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>();
        var grouped = new Dictionary<EventLogId, List<ResolvedEvent>>();

        foreach (var bufferedEvent in state.NewEventBuffer)
        {
            if (!activeLogs.TryGetValue(bufferedEvent.OwningLog, out var owningLog)) { continue; }

            if (!grouped.TryGetValue(owningLog.Id, out var list))
            {
                list = [];
                grouped[owningLog.Id] = list;
            }

            list.Add(bufferedEvent);
        }

        foreach (var (logId, events) in grouped)
        {
            var filtered = _filterService.GetFilteredEvents(events, state.AppliedFilter);

            if (filtered.Count > 0)
            {
                batched[logId] = filtered;
            }
        }

        if (batched.Count > 0)
        {
            dispatcher.Dispatch(new AppendTableEventsBatchAction(batched));
        }

        dispatcher.Dispatch(new EventBufferedAction([], false));
    }
}

internal sealed record PendingSelectionRestore(IReadOnlySet<long> SelectedIds, long? SelectedId);
