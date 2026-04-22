// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.EventLog;

public sealed class EventLogReducers
{
    [ReducerMethod]
    public static EventLogState ReduceAddEventBuffered(EventLogState state, EventLogAction.AddEventBuffered action) =>
        state with { NewEventBuffer = action.UpdatedBuffer, NewEventBufferIsFull = action.IsFull };

    [ReducerMethod]
    public static EventLogState ReduceAddEventSuccess(EventLogState state, EventLogAction.AddEventSuccess action) =>
        state with { ActiveLogs = action.ActiveLogs };

    [ReducerMethod(typeof(EventLogAction.CloseAll))]
    public static EventLogState ReduceCloseAll(EventLogState state) =>
        state with
        {
            ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty,
            SelectedEvent = null,
            SelectedEvents = [],
            NewEventBuffer = [],
            NewEventBufferIsFull = false
        };

    [ReducerMethod]
    public static EventLogState ReduceCloseLog(EventLogState state, EventLogAction.CloseLog action)
    {
        var newEventBuffer = state.NewEventBuffer
            .Where(e => e.OwningLog != action.LogName)
            .ToList();

        // Drop selections belonging to the closed log so stale references don't linger
        // in SelectedEvents. Without this, a reload (which closes and re-opens the log
        // to load XML) would leave stale entries that prevent the highlight refresh
        // when the new instances are restored via SelectEvents.
        var newSelectedEvents = state.SelectedEvents
            .RemoveAll(e => string.Equals(e.OwningLog, action.LogName, StringComparison.Ordinal));

        // Clear the focused event when it belongs to the closed log; otherwise focus would
        // point at a stale instance after reload-driven close + reopen.
        var newSelectedEvent =
            state.SelectedEvent is not null &&
            string.Equals(state.SelectedEvent.OwningLog, action.LogName, StringComparison.Ordinal)
                ? null
                : state.SelectedEvent;

        return state with
        {
            ActiveLogs = state.ActiveLogs.Remove(action.LogName),
            NewEventBuffer = newEventBuffer,
            NewEventBufferIsFull = newEventBuffer.Count >= EventLogState.MaxNewEvents,
            SelectedEvent = newSelectedEvent,
            SelectedEvents = newSelectedEvents
        };
    }

    [ReducerMethod]
    public static EventLogState ReduceLoadEvents(EventLogState state, EventLogAction.LoadEvents action)
    {
        if (!state.ActiveLogs.TryGetValue(action.LogData.Name, out var existing) ||
            existing.Id != action.LogData.Id)
        {
            return state;
        }

        return UpdateActiveLog(state, action.LogData, action.Events);
    }

    [ReducerMethod]
    public static EventLogState ReduceLoadEventsPartial(EventLogState state, EventLogAction.LoadEventsPartial action)
    {
        if (!state.ActiveLogs.TryGetValue(action.LogData.Name, out var existingLog) ||
            existingLog.Id != action.LogData.Id)
        {
            return state;
        }

        var merged = new List<DisplayEventModel>(existingLog.Events.Count + action.Events.Count);
        merged.AddRange(existingLog.Events);
        merged.AddRange(action.Events);

        return state with
        {
            ActiveLogs = state.ActiveLogs
                .Remove(action.LogData.Name)
                .Add(action.LogData.Name, existingLog with { Events = merged.AsReadOnly() })
        };
    }

    [ReducerMethod]
    public static EventLogState ReduceOpenLog(EventLogState state, EventLogAction.OpenLog action) =>
        state with
        {
            ActiveLogs = state.ActiveLogs.Add(action.LogName, GetEmptyLogData(action.LogName, action.PathType))
        };

    [ReducerMethod]
    public static EventLogState ReduceSelectEvent(EventLogState state, EventLogAction.SelectEvent action)
    {
        // Reference equality keeps selection consistent with EventTable's
        // ReferenceEqualityComparer-based selection set. Value equality on
        // DisplayEventModel (a record) would treat distinct-but-value-equal
        // instances as the same — e.g., after a log reload, SelectedEvents
        // may still hold stale references that are value-equal to new ones.
        bool alreadySelected = ContainsReference(state.SelectedEvents, action.SelectedEvent);

        // SelectedEvent always tracks the affected row (Explorer-style focus),
        // independent of whether the row ends up selected.
        if (!alreadySelected)
        {
            return state with
            {
                SelectedEvents = action.IsMultiSelect ?
                    state.SelectedEvents.Add(action.SelectedEvent) : [action.SelectedEvent],
                SelectedEvent = action.SelectedEvent
            };
        }

        if (action is { IsMultiSelect: true, ShouldStaySelected: false })
        {
            return state with
            {
                SelectedEvents = RemoveByReference(state.SelectedEvents, action.SelectedEvent),
                SelectedEvent = action.SelectedEvent
            };
        }

        if (action.ShouldStaySelected)
        {
            return ReferenceEquals(state.SelectedEvent, action.SelectedEvent)
                ? state
                : state with { SelectedEvent = action.SelectedEvent };
        }

        return state with { SelectedEvents = [action.SelectedEvent], SelectedEvent = action.SelectedEvent };
    }

    [ReducerMethod]
    public static EventLogState ReduceSelectEvents(EventLogState state, EventLogAction.SelectEvents action)
    {
        // Reference-identity dedupe only: prevents adding the same instance
        // twice, but intentionally allows distinct value-equal instances
        // (for example, stale vs. fresh events after a reload) to coexist
        // so a stale reference in SelectedEvents isn't silently collapsed
        // with a freshly loaded copy.
        var existing = new HashSet<DisplayEventModel>(state.SelectedEvents, ReferenceEqualityComparer.Instance);
        List<DisplayEventModel> eventsToAdd = [];

        foreach (var selectedEvent in action.SelectedEvents)
        {
            if (existing.Add(selectedEvent))
            {
                eventsToAdd.Add(selectedEvent);
            }
        }

        if (eventsToAdd.Count == 0) { return state; }

        var newSelection = state.SelectedEvents.AddRange(eventsToAdd);

        // Preserve focus when it survives the merge, but always resolve
        // SelectedEvent to the instance that actually lives in newSelection
        // so subsequent reference-equality checks stay valid. Prefer the
        // same reference; fall back to a value-equal instance when only a
        // replacement reference is present. If neither is found, focus the
        // last incoming event so the restore path leaves something focused.
        DisplayEventModel newSelectedEvent = eventsToAdd[^1];

        if (state.SelectedEvent is null)
        {
            return state with
            {
                SelectedEvents = newSelection,
                SelectedEvent = newSelectedEvent
            };
        }

        DisplayEventModel? valueEqualMatch = null;

        foreach (var selectedEvent in newSelection)
        {
            if (ReferenceEquals(selectedEvent, state.SelectedEvent))
            {
                valueEqualMatch = selectedEvent;

                break;
            }

            if (valueEqualMatch is null && selectedEvent == state.SelectedEvent)
            {
                valueEqualMatch = selectedEvent;
            }
        }

        if (valueEqualMatch is not null)
        {
            newSelectedEvent = valueEqualMatch;
        }

        return state with
        {
            SelectedEvents = newSelection,
            SelectedEvent = newSelectedEvent
        };
    }

    [ReducerMethod]
    public static EventLogState ReduceSetSelectedEvents(EventLogState state, EventLogAction.SetSelectedEvents action)
    {
        // Order-preserving distinct by reference identity. The caller (typically
        // EventTable) is responsible for ordering events according to the current
        // sort column; the reducer honors whatever order it receives.
        var seen = new HashSet<DisplayEventModel>(ReferenceEqualityComparer.Instance);
        var builder = ImmutableList.CreateBuilder<DisplayEventModel>();

        foreach (var selectedEvent in action.SelectedEvents)
        {
            if (seen.Add(selectedEvent))
            {
                builder.Add(selectedEvent);
            }
        }

        var newSelection = builder.ToImmutable();

        // Avoid publishing a new state reference when nothing changed —
        // EventTable.ShouldRender uses ReferenceEquals on SelectedEvents.
        bool selectionUnchanged = SelectionsEqualByReference(state.SelectedEvents, newSelection);
        bool selectedUnchanged = ReferenceEquals(state.SelectedEvent, action.SelectedEvent);

        if (selectionUnchanged && selectedUnchanged) { return state; }

        if (selectionUnchanged)
        {
            return state with { SelectedEvent = action.SelectedEvent };
        }

        return state with
        {
            SelectedEvent = action.SelectedEvent,
            SelectedEvents = newSelection
        };
    }

    private static bool ContainsReference(ImmutableList<DisplayEventModel> list, DisplayEventModel target)
    {
        foreach (var item in list)
        {
            if (ReferenceEquals(item, target)) { return true; }
        }

        return false;
    }

    private static ImmutableList<DisplayEventModel> RemoveByReference(
        ImmutableList<DisplayEventModel> list,
        DisplayEventModel target)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], target))
            {
                return list.RemoveAt(i);
            }
        }

        return list;
    }

    private static bool SelectionsEqualByReference(
        ImmutableList<DisplayEventModel> left,
        ImmutableList<DisplayEventModel> right)
    {
        if (ReferenceEquals(left, right)) { return true; }

        if (left.Count != right.Count) { return false; }

        for (int i = 0; i < left.Count; i++)
        {
            if (!ReferenceEquals(left[i], right[i])) { return false; }
        }

        return true;
    }

    [ReducerMethod]
    public static EventLogState ReduceSetContinuouslyUpdate(
        EventLogState state,
        EventLogAction.SetContinuouslyUpdate action) =>
        state with { ContinuouslyUpdate = action.ContinuouslyUpdate };

    [ReducerMethod]
    public static EventLogState ReduceSetFilters(EventLogState state, EventLogAction.SetFilters action)
    {
        if (!FilterMethods.HasFilteringChanged(action.EventFilter, state.AppliedFilter))
        {
            return state;
        }

        return state with { AppliedFilter = action.EventFilter };
    }

    private static EventLogData GetEmptyLogData(string logName, PathType pathType) =>
        new(logName, pathType, new List<DisplayEventModel>().AsReadOnly());

    private static EventLogState UpdateActiveLog(
        EventLogState state,
        EventLogData logData,
        IReadOnlyList<DisplayEventModel> events) =>
        state with
        {
            ActiveLogs = state.ActiveLogs
                .Remove(logData.Name)
                .Add(logData.Name, logData with { Events = events })
        };
}
