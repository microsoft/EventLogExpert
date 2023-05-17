// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using Fluxor;

namespace EventLogExpert.Store.EventLog;

public class EventLogReducers
{
    [ReducerMethod(typeof(EventLogAction.ClearEvents))]
    public static EventLogState ReduceClearEvents(EventLogState state) => state with
    {
        Events = new List<DisplayEventModel>(), EventsToDisplay = new List<DisplayEventModel>()
    };

    [ReducerMethod(typeof(EventLogAction.ClearFilters))]
    public static EventLogState ReduceClearFilters(EventLogState state) =>
        state with { EventsToDisplay = state.Events };

    [ReducerMethod]
    public static EventLogState ReduceFilterEvents(EventLogState state, EventLogAction.FilterEvents action)
    {
        if (!state.Events.Any()) { return state; }

        if (!action.Filters.Any()) { return state with { EventsToDisplay = state.Events }; }

        var events = state.Events.AsEnumerable();

        foreach (var filter in action.Filters)
        {
            events = events.Where(ev => filter.Comparison.Any(f => f(ev)));
        }

        var filteredEvents = events.DistinctBy(ev => ev.RecordId).OrderByDescending(ev => ev.RecordId).ToList();

        return state with { EventsToDisplay = filteredEvents };
    }

    [ReducerMethod]
    public static EventLogState ReduceLoadEvents(EventLogState state, EventLogAction.LoadEvents action) =>
        state with { Events = action.Events, EventsToDisplay = action.Events };

    [ReducerMethod]
    public static EventLogState ReduceOpenLog(EventLogState state, EventLogAction.OpenLog action) =>
        new() { ActiveLog = action.LogSpecifier };

    public static EventLogState ReduceSelectEvent(EventLogState state, EventLogAction.SelectEvent action)
    {
        if (state.SelectedEvent == action.SelectedEvent) { return state; }

        return state with { SelectedEvent = action.SelectedEvent };
    }
}
