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
    IState<LogTableState> logTableState,
    IState<RawEventStoreState> rawEventStore,
    IFilterService filterService,
    LogCloseCoordinator closeCoordinator,
    PartialLoadCoordinator coordinator)
{
    private readonly LogCloseCoordinator _closeCoordinator = closeCoordinator;
    private readonly PartialLoadCoordinator _coordinator = coordinator;
    private readonly IState<EventLogState> _eventLogState = eventLogState;
    private readonly IFilterService _filterService = filterService;
    private readonly IState<LogTableState> _logTableState = logTableState;
    private readonly IState<RawEventStoreState> _rawEventStore = rawEventStore;

    [EffectMethod]
    public Task HandleLoadEvents(LoadEventsAction action, IDispatcher dispatcher)
    {
        var version = _logTableState.Value.DisplayListVersion;

        _coordinator.MarkFinalized(action.LogData.Id);

        // The raw-store reducer runs synchronously before this effect, so the store already holds the finalized build.
        if (!_rawEventStore.Value.ByLog.TryGetValue(action.LogData.Id, out var store))
        {
            return Task.CompletedTask;
        }

        var view = DisplayViewBuilder.Build(
            store, action.LogData.Id, _eventLogState.Value.AppliedFilter, _logTableState.Value.SortContext);

        dispatcher.Dispatch(new UpdateTableAction(action.LogData.Id) { View = view, Version = version });

        if (!_closeCoordinator.TryConsumePendingRestore(action.LogData.Name, out var pending) ||
            pending is null ||
            (pending.SelectedIds.Count <= 0 && !pending.SelectedId.HasValue))
        {
            return Task.CompletedTask;
        }

        RestoreSelection(action, store, pending, dispatcher);

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleLoadEventsPartial(LoadEventsPartialAction action, IDispatcher dispatcher)
    {
        // The raw-store reducer already appended this partial's events, so the coordinator only marks the log dirty and
        // rebuilds the view at flush time.
        var version = _logTableState.Value.DisplayListVersion;

        _coordinator.Enqueue(action.LogData.Id, version);

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(LoadNewEventsAction))]
    public Task HandleLoadNewEvents(IDispatcher dispatcher)
    {
        ProcessNewEventBuffer(_eventLogState.Value, dispatcher);

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleRebuildDisplayViews(RebuildDisplayViewsAction action, IDispatcher dispatcher)
    {
        // Continuation of the IngestRawEventsAction dispatched just before it, so these reads see the post-ingest store. Do
        // not inline back into the producer effect: a same-effect read would see the stale pre-ingest store (the fixed bug).
        var raw = _rawEventStore.Value.ByLog;
        var filter = _eventLogState.Value.AppliedFilter;
        var context = _logTableState.Value.SortContext;
        var viewsByLog = new Dictionary<EventLogId, EventColumnView>(action.NewEventsByLog.Count);

        foreach (var (logId, newEvents) in action.NewEventsByLog)
        {
            // Existence check before filter work: skip a log a concurrent close dropped without filtering it, and rebuild
            // only when a new event survives the filter so a fully hidden batch doesn't churn the view.
            if (raw.TryGetValue(logId, out var store) &&
                _filterService.GetFilteredEvents(newEvents, filter).Count > 0)
            {
                viewsByLog[logId] = DisplayViewBuilder.Build(store, logId, filter, context);
            }
        }

        if (viewsByLog.Count > 0)
        {
            dispatcher.Dispatch(new AppendTableEventsBatchAction { ViewsByLog = viewsByLog });
        }

        // Consume only after a successful rebuild (unreachable if it threw above), so a build failure preserves the count.
        // Consuming the captured snapshot - not a blanket clear - keeps a mid-flush event; skip the dispatch when nothing
        // was captured (an all-filtered rebuild still consumes its non-empty snapshot).
        if (action.BufferEntriesToConsume is { Count: > 0 } bufferEntriesToConsume)
        {
            dispatcher.Dispatch(new NewEventBufferConsumedAction(bufferEntriesToConsume));
        }

        return Task.CompletedTask;
    }

    internal static void ProcessNewEventBuffer(EventLogState state, IDispatcher dispatcher)
    {
        var grouped = new Dictionary<EventLogId, List<ResolvedEvent>>();

        foreach (var bufferedEvent in state.NewEventBuffer)
        {
            if (!state.OpenLogs.TryGetValue(bufferedEvent.OwningLog, out var owningLog)) { continue; }

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

        // Continuation runs after the ingest, so it reads the post-ingest store and consumes exactly this snapshot by
        // identity. Atomic reducer buffering (ReduceAddEvent) keeps an event buffered concurrently with the flush alive.
        dispatcher.Dispatch(new RebuildDisplayViewsAction(rawByLog, BufferEntriesToConsume: state.NewEventBuffer));
    }

    private static void RestoreSelection(
        LoadEventsAction action,
        EventColumnStore store,
        PendingSelectionRestore pending,
        IDispatcher dispatcher)
    {
        List<SelectionEntry> restored = [];
        SelectionEntry? focusEntry = null;

        for (int i = 0; i < action.Events.Count; i++)
        {
            var resolvedEvent = action.Events[i];

            if (resolvedEvent.RecordId is not { } recordId) { continue; }

            bool isSelected = pending.SelectedIds.Contains(recordId);
            bool isFocus = pending.SelectedId.HasValue && recordId == pending.SelectedId.Value;

            if (!isSelected && !isFocus) { continue; }

            var locator = new EventLocator(action.LogData.Id, store.Generation, i);
            ValueKey.TryCreate(resolvedEvent, out var key);
            var entry = new SelectionEntry(locator, locator, key);

            if (isSelected) { restored.Add(entry); }

            if (isFocus) { focusEntry = entry; }
        }

        if (restored.Count <= 0 && focusEntry is null) { return; }

        SelectionEntry? focused = focusEntry ?? (restored.Count > 0 ? restored[^1] : null);
        dispatcher.Dispatch(new SetSelectedEventsAction(restored, focused));
    }
}
