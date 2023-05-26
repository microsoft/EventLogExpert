// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using Fluxor;

namespace EventLogExpert.Store.FilterPane;

public class FilterPaneReducers
{
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

    [ReducerMethod]
    public static FilterPaneState ReduceAddSubFilterAction(FilterPaneState state, FilterPaneAction.AddSubFilter action)
    {
        var updatedList = state.CurrentFilters.ToList();
        var parentFilter = updatedList.FirstOrDefault(parent => parent.Id == action.ParentId);

        if (parentFilter is null) { return state; } // If not parent filter, something went wrong and bail

        parentFilter.SubFilters.Add(new SubFilterModel(parentFilter.FilterType));

        return state with { CurrentFilters = updatedList };
    }

    [ReducerMethod(typeof(FilterPaneAction.ApplyFilters))]
    public static FilterPaneState ReduceApplyFilters(FilterPaneState state)
    {
        List<FilterModel> filters = new();

        foreach (var filterModel in state.CurrentFilters)
        {
            if (filterModel.Comparison.Any() && filterModel.IsEnabled)
            {
                filters.Add(filterModel);
            }
        }

        return state with { AppliedFilters = filters };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceRemoveFilterAction(FilterPaneState state, FilterPaneAction.RemoveFilter action)
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
    public static FilterPaneState ReduceRemoveSubFilterAction(FilterPaneState state,
        FilterPaneAction.RemoveSubFilter action)
    {
        var updatedList = state.CurrentFilters.ToList();
        var parentFilter = updatedList.FirstOrDefault(parent => parent.Id == action.ParentId);

        if (parentFilter is null) { return state; }

        parentFilter.SubFilters.Remove(action.SubFilterModel);

        return state with { CurrentFilters = updatedList };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceSetAdvancedFilter(FilterPaneState state,
        FilterPaneAction.SetAdvancedFilter action) => state with { AdvancedFilter = action.Expression };

    [ReducerMethod]
    public static FilterPaneState
        ReduceSetFilterDateRange(FilterPaneState state, FilterPaneAction.SetFilterDateRange action) =>
        state with { FilteredDateRange = action.FilterDateModel };

    [ReducerMethod(typeof(FilterPaneAction.ToggleAdvancedFilter))]
    public static FilterPaneState ReduceToggleAdvancedFilter(FilterPaneState state) =>
        state with { IsAdvancedFilterEnabled = !state.IsAdvancedFilterEnabled };

    [ReducerMethod]
    public static FilterPaneState ReduceToggleFilterAction(FilterPaneState state,
        FilterPaneAction.ToggleFilter action)
    {
        List<FilterModel> filters = new();

        foreach (var filterModel in state.CurrentFilters)
        {
            if (filterModel.Id == action.Id)
            {
                filterModel.IsEnabled = !filterModel.IsEnabled;
            }

            filters.Add(filterModel);
        }

        return state with { CurrentFilters = filters };
    }

    [ReducerMethod(typeof(FilterPaneAction.ToggleFilterDate))]
    public static FilterPaneState ReduceToggleFilterDate(FilterPaneState state)
    {
        if (state.FilteredDateRange is null) { return state; }

        return state with
        {
            FilteredDateRange = state.FilteredDateRange with { IsEnabled = !state.FilteredDateRange.IsEnabled }
        };
    }
}
