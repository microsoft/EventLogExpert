// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using Fluxor;

namespace EventLogExpert.Store.EventLog;

public class EventLogReducers
{
    [ReducerMethod(typeof(EventLogAction.ClearEvents))]
    public static EventLogState ReduceClearEvents(EventLogState state) => 
        new(state.ActiveLog, new List<DisplayEventModel>(), new List<DisplayEventModel>());

    [ReducerMethod]
    public static EventLogState ReduceFilterEvents(EventLogState state, EventLogAction.FilterEvents action)
    {
        if (!state.Events.Any()) { return state; }

        if (!action.Filters.Any()) { return new(state.ActiveLog, state.Events, state.Events); }

        var events = state.Events.AsEnumerable();

        foreach (var filter in action.Filters)
        {
            events = events.Where(ev => filter.Comparison.Any(f => f(ev)));
        }

        var filteredEvents = events.DistinctBy(ev => ev.RecordId).OrderByDescending(ev => ev.RecordId).ToList();

        return new(state.ActiveLog, state.Events, filteredEvents);
    }

    [ReducerMethod(typeof(EventLogAction.ClearFilters))]
    public static EventLogState ReduceClearFilters(EventLogState state) =>
        new(state.ActiveLog, state.Events, state.Events);

    [ReducerMethod]
    public static EventLogState ReduceLoadEvents(EventLogState state, EventLogAction.LoadEvents action) =>
        new(state.ActiveLog, action.Events, action.Events);

    [ReducerMethod]
    public static EventLogState ReduceOpenLog(EventLogState state, EventLogAction.OpenLog action) =>
        new(action.LogSpecifier, state.Events, state.EventsToDisplay);
}
