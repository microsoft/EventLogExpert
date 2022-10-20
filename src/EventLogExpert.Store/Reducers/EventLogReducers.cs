using EventLogExpert.Library.Models;
using EventLogExpert.Store.Actions;
using EventLogExpert.Store.State;
using Fluxor;

namespace EventLogExpert.Store.Reducers;

public class EventLogReducers
{
    [ReducerMethod]
    public static EventLogState ReduceClearEvents(EventLogState state, EventLogAction.ClearEvents action) =>
        new(state.ActiveLog, new List<DisplayEventModel>(), new List<DisplayEventModel>());

    [ReducerMethod]
    public static EventLogState ReduceFilterEvents(EventLogState state, EventLogAction.FilterEvents action) =>
        new(state.ActiveLog,
            state.Events,
            action.Filter.Count < 1 ? state.Events : state.Events.Where(ev => action.Filter.All(f => f(ev))).ToList());

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
