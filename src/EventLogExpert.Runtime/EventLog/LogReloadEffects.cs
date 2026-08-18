// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using Fluxor;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Runtime.EventLog;

internal sealed class LogReloadEffects(
    IState<EventLogState> eventLogState,
    IState<RawEventStoreState> rawEventStore,
    LogCloseCoordinator closeCoordinator)
{
    private readonly LogCloseCoordinator _closeCoordinator = closeCoordinator;
    private readonly IState<EventLogState> _eventLogState = eventLogState;
    private readonly IState<RawEventStoreState> _rawEventStore = rawEventStore;

    [EffectMethod]
    public Task HandleLoadEvents(LoadEventsAction action, IDispatcher dispatcher)
    {
        if (!_rawEventStore.Value.ByLog.TryGetValue(action.LogData.Id, out var store))
        {
            return Task.CompletedTask;
        }

        if (!_closeCoordinator.TryConsumePendingRestore(action.LogData.Name, out var pending) ||
            pending is null ||
            (pending.SelectedIds.Count <= 0 && !pending.SelectedId.HasValue))
        {
            return Task.CompletedTask;
        }

        RestoreSelection(action, store, pending, dispatcher);

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(LoadNewEventsAction))]
    public Task HandleLoadNewEvents(IDispatcher dispatcher)
    {
        ProcessNewEventBuffer(_eventLogState.Value, dispatcher);

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

        if (state.NewEventBuffer.Count > 0)
        {
            dispatcher.Dispatch(new NewEventBufferConsumedAction(state.NewEventBuffer));
        }
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

        dispatcher.Dispatch(new RequestRevealFocusAction(focused!.Value.OriginHandle, WaitForView: true));
    }
}
