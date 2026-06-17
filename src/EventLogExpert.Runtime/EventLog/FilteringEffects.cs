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
    private readonly IState<RawEventStoreState> _rawEventStore = rawEventStore;

    [EffectMethod]
    public Task HandleAddEvent(AddEventAction action, IDispatcher dispatcher)
    {
        if (!_eventLogState.Value.ActiveLogs.ContainsKey(action.NewEvent.OwningLog)) { return Task.CompletedTask; }

        if (_eventLogState.Value.ContinuouslyUpdate)
        {
            var activeLogs = EventLogEffectsUtility.DistributeEventsToManyLogs(
                _eventLogState.Value.ActiveLogs,
                [action.NewEvent]);

            if (!activeLogs.TryGetValue(action.NewEvent.OwningLog, out var owningLog))
            {
                return Task.CompletedTask;
            }

            // Ingest the raw event unconditionally (even when the filter hides it); the display append below
            // stays filter-gated.
            dispatcher.Dispatch(new IngestRawEventsAction(
                new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>> { [owningLog.Id] = [action.NewEvent] },
                RawIngestMode.Prepend));

            dispatcher.Dispatch(new AddEventSuccessAction(activeLogs));

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

        var logsNeedingReload = newRequiresXml && !_eventLogState.Value.ActiveLogs.IsEmpty
            ? _eventLogState.Value.ActiveLogs.Values
                .Where(d => !_concurrencyState.IsLoadedWithXml(d.Id))
                .Select(d => (d.Id, d.Name, d.Type))
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

    private async Task ApplyFilterAndPublishAsync(Filter filter, long filterToken, IDispatcher dispatcher)
    {
        var snapshot = SnapshotOpenLogEvents();

        dispatcher.Dispatch(new SetFilterProgressAction(true));

        try
        {
            var filteredActiveLogs = await Task.Run(() => _filterService.FilterActiveLogs(snapshot, filter));

            if (_concurrencyState.GetCurrentFilterToken() != filterToken) { return; }

            var snapshotById = snapshot.ToDictionary(pair => pair.Id, pair => pair.Events);
            var currentRaw = _rawEventStore.Value.ByLog;
            var fresh = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>(filteredActiveLogs.Count);
            var staleIds = new List<EventLogId>();

            foreach (var (logId, filteredEvents) in filteredActiveLogs)
            {
                if (!snapshotById.TryGetValue(logId, out var snapshotEvents)) { continue; }

                if (currentRaw.TryGetValue(logId, out var currentEvents) &&
                    ReferenceEquals(snapshotEvents, currentEvents))
                {
                    fresh[logId] = filteredEvents;
                }
                else
                {
                    staleIds.Add(logId);
                }
            }

            if (staleIds.Count > 0)
            {
                var pass2Snapshot = SnapshotEventsForLogs(staleIds);
                var refiltered = await Task.Run(() => _filterService.FilterActiveLogs(pass2Snapshot, filter));

                if (_concurrencyState.GetCurrentFilterToken() != filterToken) { return; }

                var pass2InputById = pass2Snapshot.ToDictionary(pair => pair.Id, pair => pair.Events);
                var nowRaw = _rawEventStore.Value.ByLog;

                foreach (var (logId, filteredEvents) in refiltered)
                {
                    if (!pass2InputById.TryGetValue(logId, out var pass2Events)) { continue; }

                    if (nowRaw.TryGetValue(logId, out var nowEvents) &&
                        ReferenceEquals(pass2Events, nowEvents))
                    {
                        fresh[logId] = filteredEvents;
                    }
                }
            }

            dispatcher.Dispatch(new UpdateDisplayedEventsAction(fresh));
        }
        finally
        {
            if (_concurrencyState.GetCurrentFilterToken() == filterToken)
            {
                dispatcher.Dispatch(new SetFilterProgressAction(false));
            }
        }
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
