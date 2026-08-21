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
    public static EventLogState ReduceAddEvent(EventLogState state, AddEventAction action)
    {
        if (state.ContinuouslyUpdate || !state.OpenLogs.ContainsKey(action.NewEvent.OwningLog))
        {
            return state;
        }

        var buffer = new List<ResolvedEvent>(state.NewEventBuffer.Count + 1) { action.NewEvent };
        buffer.AddRange(state.NewEventBuffer);

        return state with
        {
            NewEventBuffer = buffer.AsReadOnly(),
            NewEventBufferIsFull = buffer.Count >= EventLogState.MaxNewEvents
        };
    }

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
            Focus = null,
            Selection = [],
            PendingRevealFocus = null,
            NewEventBuffer = [],
            NewEventBufferIsFull = false
        };

    [ReducerMethod]
    public static EventLogState ReduceCloseLog(EventLogState state, CloseLogAction action)
    {
        if (state.OpenLogs.TryGetValue(action.LogName, out var closingLog) && closingLog.Id != action.LogId)
        {
            return state;
        }

        var newEventBuffer = state.NewEventBuffer
            .Where(e => e.OwningLog != action.LogName)
            .ToList();

        var newSelection = state.Selection
            .RemoveAll(entry => entry.OriginHandle.LogId == action.LogId);

        var newFocus = state.Focus is { } focus && focus.OriginHandle.LogId == action.LogId ? null : state.Focus;

        var newPendingRevealFocus = state.PendingRevealFocus is { } reveal && reveal.Target.LogId == action.LogId ?
            null : state.PendingRevealFocus;

        var newNamesByLog = state.NamesByLog.Remove(action.LogName);

        return state with
        {
            OpenLogs = state.OpenLogs.Remove(action.LogName),
            NamesByLog = newNamesByLog,
            LoadedLogNames = RecomputeLoadedLogNames(newNamesByLog, state.LoadedLogNames),
            NewEventBuffer = newEventBuffer,
            NewEventBufferIsFull = newEventBuffer.Count >= EventLogState.MaxNewEvents,
            Focus = newFocus,
            Selection = newSelection,
            PendingRevealFocus = newPendingRevealFocus
        };
    }

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
    public static EventLogState ReduceNewEventBufferConsumed(EventLogState state, NewEventBufferConsumedAction action)
    {
        if (action.ConsumedEvents.Count == 0) { return state; }

        var consumed = new HashSet<object>(action.ConsumedEvents, ReferenceEqualityComparer.Instance);
        var remaining = state.NewEventBuffer.Where(bufferedEvent => !consumed.Contains(bufferedEvent)).ToList();

        return state with
        {
            NewEventBuffer = remaining,
            NewEventBufferIsFull = remaining.Count >= EventLogState.MaxNewEvents
        };
    }

    [ReducerMethod]
    public static EventLogState ReduceOpenLog(EventLogState state, OpenLogAction action)
    {
        if (state.OpenLogs.ContainsKey(action.LogName)) { return state; }

        var openLogId = action.PreassignedId ?? EventLogId.Create();

        var perLogNames = action.LogPathType == LogPathType.Channel ?
            s_emptyNames.Add(action.LogName) :
            s_emptyNames;

        var newNamesByLog = state.NamesByLog.SetItem(action.LogName, perLogNames);

        return state with
        {
            OpenLogs = state.OpenLogs.SetItem(action.LogName, new OpenLogInfo(openLogId, action.LogPathType)),
            NamesByLog = newNamesByLog,
            LoadedLogNames = RecomputeLoadedLogNames(newNamesByLog, state.LoadedLogNames)
        };
    }

    [ReducerMethod]
    public static EventLogState ReduceRequestRevealFocus(EventLogState state, RequestRevealFocusAction action)
    {
        var request = new RevealFocusRequest(action.Target, action.WaitForView);

        return state.PendingRevealFocus == request ? state : state with { PendingRevealFocus = request };
    }

    [ReducerMethod]
    public static EventLogState ReduceRevealFocusConsumed(EventLogState state, RevealFocusConsumedAction action)
    {
        if (state.PendingRevealFocus != action.Request) { return state; }

        return state with { PendingRevealFocus = null };
    }

    [ReducerMethod]
    public static EventLogState ReduceSelectEvent(EventLogState state, SelectEventAction action)
    {
        bool alreadySelected = ContainsByOriginHandle(state.Selection, action.Selection);

        if (!alreadySelected)
        {
            return state with
            {
                Selection = action.IsMultiSelect ?
                    state.Selection.Add(action.Selection) : [action.Selection],
                Focus = action.Selection
            };
        }

        if (action is { IsMultiSelect: true, ShouldStaySelected: false })
        {
            return state with
            {
                Selection = RemoveByOriginHandle(state.Selection, action.Selection),
                Focus = action.Selection
            };
        }

        if (action.ShouldStaySelected)
        {
            return FocusEqualsByOriginHandle(state.Focus, action.Selection) ?
                state :
                state with { Focus = action.Selection };
        }

        return state with { Selection = [action.Selection], Focus = action.Selection };
    }

    [ReducerMethod]
    public static EventLogState ReduceSelectEvents(EventLogState state, SelectEventsAction action)
    {
        var existing = new HashSet<EventLocator>();

        foreach (var entry in state.Selection) { existing.Add(entry.OriginHandle); }

        List<SelectionEntry> entriesToAdd = [];

        foreach (var entry in action.Selection)
        {
            if (existing.Add(entry.OriginHandle)) { entriesToAdd.Add(entry); }
        }

        if (entriesToAdd.Count == 0) { return state; }

        var newSelection = state.Selection.AddRange(entriesToAdd);

        SelectionEntry newFocus = entriesToAdd[^1];

        if (state.Focus is not { } priorFocus)
        {
            return state with { Selection = newSelection, Focus = newFocus };
        }

        foreach (var entry in newSelection)
        {
            if (entry.OriginHandle == priorFocus.OriginHandle)
            {
                newFocus = entry;

                break;
            }
        }

        return state with { Selection = newSelection, Focus = newFocus };
    }

    [ReducerMethod]
    public static EventLogState ReduceSetContinuouslyUpdate(
        EventLogState state,
        SetContinuouslyUpdateAction action) =>
        state with { ContinuouslyUpdate = action.ContinuouslyUpdate };

    [ReducerMethod]
    public static EventLogState ReduceSetSelectedEvents(EventLogState state, SetSelectedEventsAction action)
    {
        var seen = new HashSet<EventLocator>();
        var builder = ImmutableList.CreateBuilder<SelectionEntry>();

        foreach (var entry in action.Selection)
        {
            if (seen.Add(entry.OriginHandle))
            {
                builder.Add(entry);
            }
        }

        var newSelection = builder.ToImmutable();

        bool selectionUnchanged = SelectionsEqualByOriginHandle(state.Selection, newSelection);
        bool focusUnchanged = FocusEqualsByOriginHandle(state.Focus, action.Focus);

        if (selectionUnchanged && focusUnchanged) { return state; }

        if (selectionUnchanged)
        {
            return state with { Focus = action.Focus };
        }

        return state with
        {
            Focus = action.Focus,
            Selection = newSelection
        };
    }

    private static bool ContainsByOriginHandle(ImmutableList<SelectionEntry> list, SelectionEntry target)
    {
        foreach (var entry in list)
        {
            if (entry.OriginHandle == target.OriginHandle) { return true; }
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

    private static bool FocusEqualsByOriginHandle(SelectionEntry? left, SelectionEntry? right)
    {
        if (left is not { } leftEntry) { return right is null; }

        return right is { } rightEntry && leftEntry.OriginHandle == rightEntry.OriginHandle;
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

    private static ImmutableList<SelectionEntry> RemoveByOriginHandle(
        ImmutableList<SelectionEntry> list,
        SelectionEntry target)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].OriginHandle == target.OriginHandle)
            {
                return list.RemoveAt(i);
            }
        }

        return list;
    }

    private static bool SelectionsEqualByOriginHandle(
        ImmutableList<SelectionEntry> left,
        ImmutableList<SelectionEntry> right)
    {
        if (ReferenceEquals(left, right)) { return true; }

        if (left.Count != right.Count) { return false; }

        for (int i = 0; i < left.Count; i++)
        {
            if (left[i].OriginHandle != right[i].OriginHandle) { return false; }
        }

        return true;
    }
}
