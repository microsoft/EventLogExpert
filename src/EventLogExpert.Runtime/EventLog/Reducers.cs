// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Evaluation;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.EventLog;

internal sealed class Reducers
{
    private static readonly ImmutableHashSet<string> s_emptyNames =
        ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase);

    private static readonly ImmutableDictionary<string, ImmutableHashSet<string>> s_emptyNamesByLog =
        ImmutableDictionary<string, ImmutableHashSet<string>>.Empty;

    [ReducerMethod]
    public static EventLogState ReduceApplyFilter(EventLogState state, ApplyFilterAction action)
    {
        if (!action.Filter.HasFilteringChangedFrom(state.AppliedFilter))
        {
            return state;
        }

        return state with { AppliedFilter = action.Filter };
    }

    [ReducerMethod(typeof(CloseAllLogsAction))]
    public static EventLogState ReduceCloseAll(EventLogState state) =>
        state with
        {
            OpenLogs = [],
            NamesByLog = s_emptyNamesByLog,
            LoadedLogNames = RecomputeLoadedLogNames(s_emptyNamesByLog, state.LoadedLogNames),
            SelectedEvent = null,
            SelectedEvents = [],
            NewEventBuffer = [],
            NewEventBufferIsFull = false
        };

    [ReducerMethod]
    public static EventLogState ReduceCloseLog(EventLogState state, CloseLogAction action)
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

        var newNamesByLog = state.NamesByLog.Remove(action.LogName);

        return state with
        {
            OpenLogs = state.OpenLogs.Remove(action.LogName),
            NamesByLog = newNamesByLog,
            LoadedLogNames = RecomputeLoadedLogNames(newNamesByLog, state.LoadedLogNames),
            NewEventBuffer = newEventBuffer,
            NewEventBufferIsFull = newEventBuffer.Count >= EventLogState.MaxNewEvents,
            SelectedEvent = newSelectedEvent,
            SelectedEvents = newSelectedEvents
        };
    }

    [ReducerMethod]
    public static EventLogState ReduceEventBuffered(EventLogState state, EventBufferedAction action) =>
        state with { NewEventBuffer = action.UpdatedBuffer, NewEventBufferIsFull = action.IsFull };

    [ReducerMethod]
    public static EventLogState ReduceLoadEvents(EventLogState state, LoadEventsAction action)
    {
        if (!state.OpenLogs.TryGetValue(action.LogData.Name, out var existing) ||
            existing.Id != action.LogData.Id ||
            action.LogData.Type != LogPathType.File)
        {
            return state;
        }

        var newSet = DistinctLogNames(action.Events);

        if (state.NamesByLog.TryGetValue(action.LogData.Name, out var existingSet) &&
            newSet.SetEquals(existingSet))
        {
            return state;
        }

        var newNamesByLog = state.NamesByLog.SetItem(action.LogData.Name, newSet);

        return state with
        {
            NamesByLog = newNamesByLog,
            LoadedLogNames = RecomputeLoadedLogNames(newNamesByLog, state.LoadedLogNames)
        };
    }

    [ReducerMethod]
    public static EventLogState ReduceLoadEventsPartial(EventLogState state, LoadEventsPartialAction action)
    {
        if (!state.OpenLogs.TryGetValue(action.LogData.Name, out var existingLog) ||
            existingLog.Id != action.LogData.Id ||
            action.LogData.Type != LogPathType.File)
        {
            return state;
        }

        var existingSet = state.NamesByLog.TryGetValue(action.LogData.Name, out var current)
            ? current
            : s_emptyNames;

        var newSet = existingSet.Union(DistinctLogNames(action.Events));

        if (ReferenceEquals(newSet, existingSet)) { return state; }

        var newNamesByLog = state.NamesByLog.SetItem(action.LogData.Name, newSet);

        return state with
        {
            NamesByLog = newNamesByLog,
            LoadedLogNames = RecomputeLoadedLogNames(newNamesByLog, state.LoadedLogNames)
        };
    }

    [ReducerMethod]
    public static EventLogState ReduceOpenLog(EventLogState state, OpenLogAction action)
    {
        // Idempotent: re-opening an already-active log is a no-op so callers (menu, drag/drop, command line,
        // SettingsModal.ReloadOpenLogs, effects) don't need to coordinate to avoid ImmutableDictionary.Add throwing.
        if (state.OpenLogs.ContainsKey(action.LogName)) { return state; }

        var openLogId = EventLogId.Create();

        var perLogNames = action.LogPathType == LogPathType.Channel
            ? s_emptyNames.Add(action.LogName)
            : s_emptyNames;

        var newNamesByLog = state.NamesByLog.SetItem(action.LogName, perLogNames);

        return state with
        {
            OpenLogs = state.OpenLogs.SetItem(action.LogName, new OpenLogInfo(openLogId, action.LogPathType)),
            NamesByLog = newNamesByLog,
            LoadedLogNames = RecomputeLoadedLogNames(newNamesByLog, state.LoadedLogNames)
        };
    }

    [ReducerMethod]
    public static EventLogState ReduceSelectEvent(EventLogState state, SelectEventAction action)
    {
        // Reference equality keeps selection consistent with EventTable's
        // ReferenceEqualityComparer-based selection set. Value equality on
        // ResolvedEvent (a record) would treat distinct-but-value-equal
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
    public static EventLogState ReduceSelectEvents(EventLogState state, SelectEventsAction action)
    {
        // Reference-identity dedupe only: prevents adding the same instance
        // twice, but intentionally allows distinct value-equal instances
        // (for example, stale vs. fresh events after a reload) to coexist
        // so a stale reference in SelectedEvents isn't silently collapsed
        // with a freshly loaded copy.
        var existing = new HashSet<ResolvedEvent>(state.SelectedEvents, ReferenceEqualityComparer.Instance);
        List<ResolvedEvent> eventsToAdd = [];

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
        ResolvedEvent newSelectedEvent = eventsToAdd[^1];

        if (state.SelectedEvent is null)
        {
            return state with
            {
                SelectedEvents = newSelection,
                SelectedEvent = newSelectedEvent
            };
        }

        ResolvedEvent? valueEqualMatch = null;

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
    public static EventLogState ReduceSetContinuouslyUpdate(
        EventLogState state,
        SetContinuouslyUpdateAction action) =>
        state with { ContinuouslyUpdate = action.ContinuouslyUpdate };

    [ReducerMethod]
    public static EventLogState ReduceSetSelectedEvents(EventLogState state, SetSelectedEventsAction action)
    {
        // Order-preserving distinct by reference identity. The caller (typically
        // EventTable) is responsible for ordering events according to the current
        // sort column; the reducer honors whatever order it receives.
        var seen = new HashSet<ResolvedEvent>(ReferenceEqualityComparer.Instance);
        var builder = ImmutableList.CreateBuilder<ResolvedEvent>();

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

    private static bool ContainsReference(ImmutableList<ResolvedEvent> list, ResolvedEvent target)
    {
        foreach (var item in list)
        {
            if (ReferenceEquals(item, target)) { return true; }
        }

        return false;
    }

    private static ImmutableHashSet<string> DistinctLogNames(IReadOnlyList<ResolvedEvent> events)
    {
        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var resolvedEvent in events)
        {
            if (!string.IsNullOrEmpty(resolvedEvent.LogName)) { builder.Add(resolvedEvent.LogName); }
        }

        return builder.ToImmutable();
    }

    private static ImmutableHashSet<string> RecomputeLoadedLogNames(
        ImmutableDictionary<string, ImmutableHashSet<string>> namesByLog,
        ImmutableHashSet<string> priorLoadedLogNames)
    {
        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var perLogNames in namesByLog.Values) { builder.UnionWith(perLogNames); }

        var union = builder.ToImmutable();

        return union.SetEquals(priorLoadedLogNames) ? priorLoadedLogNames : union;
    }

    private static ImmutableList<ResolvedEvent> RemoveByReference(
        ImmutableList<ResolvedEvent> list,
        ResolvedEvent target)
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
        ImmutableList<ResolvedEvent> left,
        ImmutableList<ResolvedEvent> right)
    {
        if (ReferenceEquals(left, right)) { return true; }

        if (left.Count != right.Count) { return false; }

        for (int i = 0; i < left.Count; i++)
        {
            if (!ReferenceEquals(left[i], right[i])) { return false; }
        }

        return true;
    }
}
