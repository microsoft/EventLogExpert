// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Helpers;
using EventLogExpert.Library.Models;
using EventLogExpert.Store.EventLog;
using Fluxor;

namespace EventLogExpert.Store.FilterPane;

public class FilterPaneReducers
{
    private readonly IDispatcher _dispatcher;

    public FilterPaneReducers(IDispatcher dispatcher) => _dispatcher = dispatcher;

    [ReducerMethod(typeof(FilterPaneAction.AddFilter))]
    public static FilterPaneState ReduceAddFilterAction(FilterPaneState state)
    {
        var updatedList = state.CurrentFilters.ToList();

        if (updatedList.Count <= 0)
        {
            return state with { CurrentFilters = new List<FilterModel> { new(Guid.NewGuid()) } };
        }

        updatedList.Add(new FilterModel(Guid.NewGuid()));

        return state with { CurrentFilters = updatedList };
    }

    [ReducerMethod(typeof(FilterPaneAction.AddAvailableFilters))]
    public static AvailableFilterState ReduceAddRecentFilter(AvailableFilterState state) => state;

    [ReducerMethod]
    public static FilterPaneState ReduceAddSubFilterAction(FilterPaneState state, FilterPaneAction.AddSubFilter action)
    {
        var updatedList = state.CurrentFilters.ToList();
        var parentFilter = updatedList.FirstOrDefault(parent => parent.Id == action.ParentId);

        if (parentFilter is null) { return state; } // If not parent filter, something went wrong and bail

        parentFilter.SubFilters.Add(new SubFilterModel(parentFilter.FilterType));

        return state with { CurrentFilters = updatedList };
    }

    [ReducerMethod]
    public static AvailableFilterState ReduceLoadEventsAction(AvailableFilterState state,
        EventLogAction.LoadEvents action) => state with
    {
        EventIdsAll = action.AllEventIds,
        EventProviderNamesAll = action.AllProviderNames,
        TaskNamesAll = action.AllTaskNames
    };

    [ReducerMethod]
    public FilterPaneState ReduceRemoveFilterAction(FilterPaneState state, FilterPaneAction.RemoveFilter action)
    {
        var updatedList = state.CurrentFilters.ToList();
        var filter = updatedList.FirstOrDefault(filter => filter.Id == action.FilterModel.Id);

        if (filter is null)
        {
            return state;
        }

        updatedList.Remove(filter);

        return state with { CurrentFilters = updatedList };
    }

    [ReducerMethod]
    public FilterPaneState ReduceRemoveSubFilterAction(FilterPaneState state, FilterPaneAction.RemoveSubFilter action)
    {
        var updatedList = state.CurrentFilters.ToList();
        var parentFilter = updatedList.FirstOrDefault(parent => parent.Id == action.ParentId);

        if (parentFilter is null) { return state; }

        parentFilter.SubFilters.Remove(action.SubFilterModel);

        return state with { CurrentFilters = updatedList };
    }
}
