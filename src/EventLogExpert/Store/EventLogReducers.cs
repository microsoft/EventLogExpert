using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EventLogExpert.EventUtils;
using Fluxor;

namespace EventLogExpert.Store
{
    public class EventLogReducers
    {
        [ReducerMethod]
        public static EventLogState ReduceOpenLog(EventLogState state, EventLogAction.OpenLog action) =>
            new(action.logSpecifier, state.Events, state.Filter, state.EventsToDisplay);

        [ReducerMethod]
        public static EventLogState ReduceClearEvents(EventLogState state, EventLogAction.ClearEvents action) =>
            new(state.ActiveLog, new List<DisplayEvent>(), state.Filter, new List<DisplayEvent>());

        [ReducerMethod]
        public static EventLogState ReduceLoadEvents(EventLogState state, EventLogAction.LoadEvents action) =>
            new(state.ActiveLog, action.events, state.Filter,
                state.Filter.Count < 1 ? action.events : action.events.Where(ev => state.Filter.All(filter => filter(ev))).ToList());

        [ReducerMethod]
        public static EventLogState ReduceFilterEvents(EventLogState state, EventLogAction.FilterEvents action) =>
            new(state.ActiveLog, state.Events, action.filter,
                action.filter.Count < 1 ? state.Events : state.Events.Where(ev => action.filter.All(f => f(ev))).ToList());
    }
}
