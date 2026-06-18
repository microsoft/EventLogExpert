// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Compilation;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.FilterProgress;
using EventLogExpert.Runtime.LogTable;
using Fluxor;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Runtime.EventLog;

internal sealed class FilteringEffects(
    IState<EventLogState> eventLogState,
    IState<RawEventStoreState> rawEventStore,
    IState<LogTableState> logTableState,
    IFilterService filterService,
    ITraceLogger logger,
    LogCloseCoordinator closeCoordinator,
    EventLogConcurrencyState concurrencyState)
{
    private readonly LogCloseCoordinator _closeCoordinator = closeCoordinator;
    private readonly EventLogConcurrencyState _concurrencyState = concurrencyState;
    private readonly IState<EventLogState> _eventLogState = eventLogState;
    private readonly IFilterService _filterService = filterService;
    private readonly ITraceLogger _logger = logger;
    private readonly IState<LogTableState> _logTableState = logTableState;
    private readonly IState<RawEventStoreState> _rawEventStore = rawEventStore;

    [EffectMethod]
    public Task HandleAddEvent(AddEventAction action, IDispatcher dispatcher)
    {
        if (!_eventLogState.Value.OpenLogs.TryGetValue(action.NewEvent.OwningLog, out var owningLog))
        {
            return Task.CompletedTask;
        }

        if (_eventLogState.Value.ContinuouslyUpdate)
        {
            // Ingest the raw event unconditionally (even when the filter hides it); the display append below
            // stays filter-gated.
            dispatcher.Dispatch(new IngestRawEventsAction(
                new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>> { [owningLog.Id] = [action.NewEvent] },
                RawIngestMode.Prepend));

            var filteredNew = _filterService.GetFilteredEvents(
                [action.NewEvent],
                _eventLogState.Value.AppliedFilter);

            if (filteredNew.Count > 0)
            {
                dispatcher.Dispatch(new AppendTableEventsAction(owningLog.Id, filteredNew));
            }

            return Task.CompletedTask;
        }

        var updatedBuffer = new List<ResolvedEvent>(_eventLogState.Value.NewEventBuffer.Count + 1)
        {
            action.NewEvent
        };

        updatedBuffer.AddRange(_eventLogState.Value.NewEventBuffer);

        var full = updatedBuffer.Count >= EventLogState.MaxNewEvents;

        dispatcher.Dispatch(new EventBufferedAction(updatedBuffer.AsReadOnly(), full));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public async Task HandleApplyFilter(ApplyFilterAction action, IDispatcher dispatcher)
    {
        long reloadTokenAtStart = _concurrencyState.GetCurrentReloadToken();

        bool newRequiresXml = action.Filter.RequiresXml;

        var logsNeedingReload = newRequiresXml && !_eventLogState.Value.OpenLogs.IsEmpty
            ? _eventLogState.Value.OpenLogs
                .Where(kvp => !_concurrencyState.IsLoadedWithXml(kvp.Value.Id))
                .Select(kvp => (kvp.Value.Id, Name: kvp.Key, kvp.Value.Type))
                .ToList()
            : [];

        long filterToken = _concurrencyState.InvalidateInFlightFilters();

        if (logsNeedingReload.Count > 0)
        {
            dispatcher.Dispatch(new SetFilterProgressAction(false));

            await ReloadLogsWithXmlAsync(logsNeedingReload, reloadTokenAtStart, dispatcher);

            return;
        }

        await ApplyFilterAndPublishAsync(action.Filter, filterToken, dispatcher);
    }

    [EffectMethod]
    public Task HandleSetContinuouslyUpdate(SetContinuouslyUpdateAction action, IDispatcher dispatcher)
    {
        if (action.ContinuouslyUpdate)
        {
            LogReloadEffects.ProcessNewEventBuffer(_eventLogState.Value, dispatcher, _filterService);
        }

        return Task.CompletedTask;
    }

    [EffectMethod]
    public async Task HandleSetGroupBy(SetGroupByAction action, IDispatcher dispatcher) =>
        await RepublishForSortAsync(dispatcher);

    [EffectMethod]
    public async Task HandleSetOrderBy(SetOrderByAction action, IDispatcher dispatcher) =>
        await RepublishForSortAsync(dispatcher);

    [EffectMethod(typeof(ToggleGroupSortingAction))]
    public async Task HandleToggleGroupSorting(IDispatcher dispatcher)
    {
        if (_logTableState.Value.RequestedGroupBy is null) { return; }

        await RepublishForSortAsync(dispatcher);
    }

    [EffectMethod(typeof(ToggleSortingAction))]
    public async Task HandleToggleSorting(IDispatcher dispatcher) =>
        await RepublishForSortAsync(dispatcher);

    [EffectMethod(typeof(UpdateTableAction))]
    public async Task HandleUpdateTable(IDispatcher dispatcher)
    {
        // An XML-reload reopen settles under the live sort and drops the in-flight rebuild; if a sort is still pending, republish so it applies.
        if (!_logTableState.Value.HasPendingSortChange) { return; }

        await RepublishForSortAsync(dispatcher);
    }

    private async Task ApplyFilterAndPublishAsync(Filter filter, long filterToken, IDispatcher dispatcher)
    {
        var snapshot = SnapshotOpenLogEvents();
        var capturedContext = _logTableState.Value.SortContext;
        var generation = _logTableState.Value.DisplayListGeneration;

        dispatcher.Dispatch(new SetFilterProgressAction(true));

        try
        {
            var sortedActiveLogs = await Task.Run(() => FilterAndSort(snapshot, filter, capturedContext));

            if (_concurrencyState.GetCurrentFilterToken() != filterToken) { return; }

            var snapshotById = snapshot.ToDictionary(pair => pair.Id, pair => pair.Events);
            var currentRaw = _rawEventStore.Value.ByLog;
            var fresh = new Dictionary<EventLogId, SegmentedSortedList>(sortedActiveLogs.Count);
            var staleIds = new List<EventLogId>();

            foreach (var (logId, sortedList) in sortedActiveLogs)
            {
                if (!snapshotById.TryGetValue(logId, out var snapshotEvents)) { continue; }

                if (currentRaw.TryGetValue(logId, out var currentEvents) &&
                    ReferenceEquals(snapshotEvents, currentEvents))
                {
                    fresh[logId] = sortedList;
                }
                else
                {
                    staleIds.Add(logId);
                }
            }

            if (staleIds.Count > 0)
            {
                var pass2Snapshot = SnapshotEventsForLogs(staleIds);
                var refilteredSorted = await Task.Run(() => FilterAndSort(pass2Snapshot, filter, capturedContext));

                if (_concurrencyState.GetCurrentFilterToken() != filterToken) { return; }

                var pass2InputById = pass2Snapshot.ToDictionary(pair => pair.Id, pair => pair.Events);
                var nowRaw = _rawEventStore.Value.ByLog;

                foreach (var (logId, sortedList) in refilteredSorted)
                {
                    if (!pass2InputById.TryGetValue(logId, out var pass2Events)) { continue; }

                    if (nowRaw.TryGetValue(logId, out var nowEvents) &&
                        ReferenceEquals(pass2Events, nowEvents))
                    {
                        fresh[logId] = sortedList;
                    }
                }
            }

            dispatcher.Dispatch(new DisplayReadyAction { Lists = fresh, Generation = generation });
        }
        finally
        {
            if (_concurrencyState.GetCurrentFilterToken() == filterToken)
            {
                dispatcher.Dispatch(new SetFilterProgressAction(false));
            }
        }
    }

    private Dictionary<EventLogId, SegmentedSortedList> FilterAndSort(
        IReadOnlyList<(EventLogId Id, IReadOnlyList<ResolvedEvent> Events)> snapshot,
        Filter filter,
        SortContext context)
    {
        var filtered = _filterService.FilterActiveLogs(snapshot, filter);
        var sorted = new Dictionary<EventLogId, SegmentedSortedList>(filtered.Count);

        foreach (var (logId, events) in filtered)
        {
            sorted[logId] = SegmentedSortedList.CreateSorted(events, context);
        }

        return sorted;
    }

    private async Task ReloadLogsWithXmlAsync(
        List<(EventLogId Id, string Name, LogPathType Type)> logsNeedingReload,
        long reloadToken,
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

        if (selectedRecordId.HasValue &&
            !string.IsNullOrEmpty(selectedLogName) &&
            reloadNames.Contains(selectedLogName) &&
            !selectionByLog.ContainsKey(selectedLogName))
        {
            selectionByLog[selectedLogName] = new HashSet<long>();
        }

        await _closeCoordinator.AcquireCoordinatorLockAsync();

        try
        {
            var closeWaiters = new List<(EventLogId Id, string Name, Task Task)>(logsNeedingReload.Count);

            foreach (var (id, name, _) in logsNeedingReload)
            {
                var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _closeCoordinator.RegisterCloseCompletion(id, tcs);
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
                    await task.WaitAsync(LogCloseCoordinator.LogCloseTimeout);
                }
                catch (TimeoutException)
                {
                    _closeCoordinator.RemoveStrandedCompletion(id);
                    timedOutLogs.Add(name);

                    _logger.Trace(
                        $"{nameof(HandleApplyFilter)}: close for log '{name}' did not complete within {LogCloseCoordinator.LogCloseTimeout}; selection will not be restored to avoid race with the delayed close wiping the entry.");
                }
            }

            foreach (var (name, ids) in selectionByLog)
            {
                if (timedOutLogs.Contains(name)) { continue; }

                long? selectedIdForLog = string.Equals(name, selectedLogName, StringComparison.Ordinal) ?
                    selectedRecordId : null;

                _closeCoordinator.WritePendingRestore(name, new PendingSelectionRestore(ids, selectedIdForLog));
            }

            if (_concurrencyState.GetCurrentReloadToken() != reloadToken)
            {
                foreach (var (_, name, _) in logsNeedingReload)
                {
                    _closeCoordinator.ClearPendingRestore(name);
                }

                _logger.Trace(
                    $"{nameof(HandleApplyFilter)}: reload superseded by CloseAll; skipping reopen of {logsNeedingReload.Count} log(s) and clearing pending selection restore.");

                return;
            }

            var reopenedSoFar = new List<(EventLogId Id, string Name)>(logsNeedingReload.Count);

            foreach (var (id, name, type) in logsNeedingReload)
            {
                if (_concurrencyState.GetCurrentReloadToken() != reloadToken)
                {
                    foreach (var (reopenedId, reopenedName) in reopenedSoFar)
                    {
                        dispatcher.Dispatch(new CloseLogAction(reopenedId, reopenedName));
                    }

                    foreach (var (_, restoreName, _) in logsNeedingReload)
                    {
                        _closeCoordinator.ClearPendingRestore(restoreName);
                    }

                    _logger.Trace(
                        $"{nameof(HandleApplyFilter)}: reload superseded by CloseAll mid-reopen; dispatched CloseLog for {reopenedSoFar.Count} just-reopened log(s) and cleared pending selection restore.");

                    return;
                }

                dispatcher.Dispatch(new OpenLogAction(name, type));
                reopenedSoFar.Add((id, name));
            }
        }
        finally
        {
            _closeCoordinator.ReleaseCoordinatorLock();
        }
    }

    private Task RepublishForSortAsync(IDispatcher dispatcher) =>
        ApplyFilterAndPublishAsync(
            _eventLogState.Value.AppliedFilter,
            _concurrencyState.InvalidateInFlightFilters(),
            dispatcher);

    private List<(EventLogId Id, IReadOnlyList<ResolvedEvent> Events)> SnapshotEventsForLogs(
        IReadOnlyList<EventLogId> logIds)
    {
        var raw = _rawEventStore.Value.ByLog;
        var snapshot = new List<(EventLogId Id, IReadOnlyList<ResolvedEvent> Events)>();

        foreach (var logId in logIds)
        {
            if (raw.TryGetValue(logId, out var events))
            {
                snapshot.Add((logId, events));
            }
        }

        return snapshot;
    }

    private List<(EventLogId Id, IReadOnlyList<ResolvedEvent> Events)> SnapshotOpenLogEvents()
    {
        var raw = _rawEventStore.Value.ByLog;
        var snapshot = new List<(EventLogId Id, IReadOnlyList<ResolvedEvent> Events)>();

        foreach (var info in _eventLogState.Value.OpenLogs.Values)
        {
            if (raw.TryGetValue(info.Id, out var events))
            {
                snapshot.Add((info.Id, events));
            }
        }

        return snapshot;
    }
}
