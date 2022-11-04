// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using EventLogExpert.Store.EventLog;
using Fluxor;

namespace EventLogExpert.Store.FilterPane;

public class FilterPaneReducers
{
    [ReducerMethod(typeof(FilterPaneAction.AddFilter))]
    public static FilterPaneState ReduceAddFilterAction(FilterPaneState state)
    {
        var id = state.CurrentFilters.Count();

        if (id <= 0)
        {
            return new FilterPaneState(new List<FilterModel> { new(id) });
        }

        var updatedList = state.CurrentFilters.ToList();
        updatedList.Add(new FilterModel(id));

        return new FilterPaneState(updatedList);
    }

    [ReducerMethod(typeof(FilterPaneAction.AddAvailableFilters))]
    public static AvailableFilterState
        ReduceAddRecentFilter(AvailableFilterState state) =>
        new(state.EventIdsAll, state.EventProviderNamesAll, state.TaskNamesAll);

    [ReducerMethod]
    public static AvailableFilterState ReduceLoadEventsAction(AvailableFilterState state,
        EventLogAction.LoadEvents action) => new(action.AllEventIds, action.AllProviderNames, action.AllTaskNames);

    [ReducerMethod]
    public static FilterPaneState ReduceRemoveFilterAction(FilterPaneState state, FilterPaneAction.RemoveFilter action)
    {
        var updatedList = state.CurrentFilters.ToList();
        var filter = updatedList.FirstOrDefault(filter => filter.Id == action.FilterModel.Id);

        if (filter is null)
        {
            return new FilterPaneState(state.CurrentFilters);
        }

        updatedList.Remove(filter);

        return new FilterPaneState(updatedList);
    }
}
