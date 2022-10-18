using EventLogExpert.Store.Actions;
using EventLogExpert.Store.State;
using Fluxor;

namespace EventLogExpert.Store.Reducers;

public class StatusBarReducers
{
    [ReducerMethod]
    public static StatusBarState ReduceSetEventsLoaded(StatusBarState state, StatusBarAction.SetEventsLoaded action) =>
        new(action.EventCount);
}
