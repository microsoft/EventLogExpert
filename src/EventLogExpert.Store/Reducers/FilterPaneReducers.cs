using EventLogExpert.Store.Actions;
using EventLogExpert.Store.State;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Store.Reducers;

public class FilterPaneReducers
{
    [ReducerMethod]
    public static FilterPaneState
        ReduceAddRecentFilter(FilterPaneState state, FilterPaneAction.AddRecentFilter action) =>
        new(state.RecentFilters.Prepend(action.FilterText).Take(10).ToImmutableList());
}
