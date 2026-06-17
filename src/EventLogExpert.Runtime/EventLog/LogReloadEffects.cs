// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Compilation;
using EventLogExpert.Runtime.LogTable;
using Fluxor;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Runtime.EventLog;

internal sealed class LogReloadEffects(
    IState<EventLogState> eventLogState,
    IFilterService filterService,
    LogCloseCoordinator closeCoordinator,
    PartialLoadCoordinator coordinator)
{
    private readonly LogCloseCoordinator _closeCoordinator = closeCoordinator;
    private readonly PartialLoadCoordinator _coordinator = coordinator;
    private readonly IState<EventLogState> _eventLogState = eventLogState;
    private readonly IFilterService _filterService = filterService;

    [EffectMethod]
    public Task HandleLoadEvents(LoadEventsAction action, IDispatcher dispatcher)
    {
        var filteredEvents = _filterService.GetFilteredEvents(action.Events, _eventLogState.Value.AppliedFilter);

        _coordinator.MarkFinalized(action.LogData.Id);

        dispatcher.Dispatch(new UpdateTableAction(action.LogData.Id, filteredEvents));

        if (!_closeCoordinator.TryConsumePendingRestore(action.LogData.Name, out var pending) ||
            pending is null ||
            (pending.SelectedIds.Count <= 0 && !pending.SelectedId.HasValue))
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

        var focused = selectedRestored ?? (restored.Count > 0 ? restored[^1] : null);
        dispatcher.Dispatch(new SetSelectedEventsAction(restored, focused));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleLoadEventsPartial(LoadEventsPartialAction action, IDispatcher dispatcher)
    {
        var filteredEvents = _filterService.GetFilteredEvents(action.Events, _eventLogState.Value.AppliedFilter);

        _coordinator.Enqueue(action.LogData.Id, filteredEvents);

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(LoadNewEventsAction))]
    public Task HandleLoadNewEvents(IDispatcher dispatcher)
    {
        ProcessNewEventBuffer(_eventLogState.Value, dispatcher);

        return Task.CompletedTask;
    }

    internal static void ProcessNewEventBuffer(
        EventLogState state,
        IDispatcher dispatcher,
        IFilterService filterService)
    {
        var activeLogs = EventLogEffectsUtility.DistributeEventsToManyLogs(state.ActiveLogs, state.NewEventBuffer);

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

        var rawByLog = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>(grouped.Count);

        foreach (var (logId, events) in grouped) { rawByLog[logId] = events.AsReadOnly(); }

        if (rawByLog.Count > 0)
        {
            dispatcher.Dispatch(new IngestRawEventsAction(rawByLog, RawIngestMode.Prepend));
        }

        dispatcher.Dispatch(new AddEventSuccessAction(activeLogs));

        foreach (var (logId, events) in grouped)
        {
            var filtered = filterService.GetFilteredEvents(events, state.AppliedFilter);

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

    private void ProcessNewEventBuffer(EventLogState state, IDispatcher dispatcher) =>
        ProcessNewEventBuffer(state, dispatcher, _filterService);
}
