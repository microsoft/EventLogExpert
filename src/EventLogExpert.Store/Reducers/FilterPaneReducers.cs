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
        new(
            state.RecentFilters.Prepend(action.FilterText).Take(10).ToImmutableList(),
            state.EventIdsAll,
            state.EventIdsSelected,
            state.EventProviderNamesAll,
            state.EventProviderNamesSelected,
            state.TaskNamesAll,
            state.TaskNamesSelected);

    [ReducerMethod]
    public static FilterPaneState ReduceLoadEventsAction(FilterPaneState state, EventLogAction.LoadEvents action) =>
    new(
        state.RecentFilters,
        action.AllEventIds,
        state.EventIdsSelected,
        action.AllProviderNames,
        state.EventProviderNamesSelected,
        action.AllTaskNames,
        state.TaskNamesSelected);
}
