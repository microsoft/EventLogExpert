// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using Fluxor;
using System.Collections.Immutable;
using CloseAllLogsAction = EventLogExpert.Runtime.EventLog.CloseAllLogsAction;

namespace EventLogExpert.Runtime.LogTable;

internal sealed class FilteredLogPresenceReducers
{
    [ReducerMethod]
    public static FilteredLogPresenceState ReduceAddTable(FilteredLogPresenceState state, AddTableAction action) =>
        state with { ByLog = state.ByLog.SetItem(action.LogData.Id, FilteredLogPresence.Pending) };

    [ReducerMethod(typeof(CloseAllLogsAction))]
    public static FilteredLogPresenceState ReduceCloseAll(FilteredLogPresenceState state) =>
        state.ByLog.IsEmpty ?
            state :
            state with { ByLog = ImmutableDictionary<EventLogId, FilteredLogPresence>.Empty };

    [ReducerMethod]
    public static FilteredLogPresenceState ReduceCloseLog(FilteredLogPresenceState state, CloseLogAction action)
    {
        var remaining = state.ByLog.Remove(action.LogId);

        return ReferenceEquals(remaining, state.ByLog) ? state : state with { ByLog = remaining };
    }

    [ReducerMethod]
    public static FilteredLogPresenceState ReduceInvalidated(
        FilteredLogPresenceState state,
        FilteredPresenceInvalidatedAction action)
    {
        if (action.FilterVersion <= state.FilterVersion) { return state; }

        var builder = state.ByLog.ToBuilder();

        foreach (var logId in action.LogIds)
        {
            if (builder.ContainsKey(logId)) { builder[logId] = FilteredLogPresence.Pending; }
        }

        return state with { ByLog = builder.ToImmutable(), FilterVersion = action.FilterVersion };
    }

    [ReducerMethod]
    public static FilteredLogPresenceState ReduceUpdated(
        FilteredLogPresenceState state,
        FilteredPresenceUpdatedAction action)
    {
        if (action.FilterVersion != state.FilterVersion) { return state; }

        var builder = state.ByLog.ToBuilder();
        bool changed = false;

        foreach (var (logId, presence) in action.Verdicts)
        {
            if (!builder.TryGetValue(logId, out var current) || current == presence) { continue; }

            builder[logId] = presence;
            changed = true;
        }

        return changed ? state with { ByLog = builder.ToImmutable() } : state;
    }
}
