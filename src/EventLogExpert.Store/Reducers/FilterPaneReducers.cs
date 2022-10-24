// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

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
        new(state.RecentFilters.Prepend(action.FilterText).Take(10).ToImmutableList(),
            state.EventIdsAll,
            state.EventProviderNamesAll,
            state.TaskNamesAll);

    [ReducerMethod]
    public static FilterPaneState ReduceLoadEventsAction(FilterPaneState state, EventLogAction.LoadEvents action) =>
        new(state.RecentFilters, action.AllEventIds, action.AllProviderNames, action.AllTaskNames);
}
