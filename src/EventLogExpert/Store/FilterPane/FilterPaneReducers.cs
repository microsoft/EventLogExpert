// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using Fluxor;

namespace EventLogExpert.Store.FilterPane;

public class FilterPaneReducers
{
    [ReducerMethod]
    public static FilterPaneState ReduceAddFilter(FilterPaneState state, FilterPaneAction.AddFilter action)
    {
        if (!state.CurrentFilters.Any())
        {
            return state with { CurrentFilters = new List<FilterModel> { new() }.AsReadOnly() };
        }

        if (action.FilterModel is null)
        {
            return state with
            {
                CurrentFilters = state.CurrentFilters.Concat(new[] { new FilterModel() })
                    .ToList().AsReadOnly()
            };
        }

        return state with
        {
            CurrentFilters = state.CurrentFilters.Concat(new[] { action.FilterModel })
                .ToList().AsReadOnly()
        };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceAddSubFilter(FilterPaneState state, FilterPaneAction.AddSubFilter action)
    {
        var updatedList = state.CurrentFilters.ToList();
        var parentFilter = updatedList.FirstOrDefault(parent => parent.Id == action.ParentId);

        if (parentFilter is null) { return state; } // If not parent filter, something went wrong and bail

        parentFilter.SubFilters.Add(new SubFilterModel());

        return state with { CurrentFilters = updatedList.AsReadOnly() };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceRemoveFilter(FilterPaneState state, FilterPaneAction.RemoveFilter action)
    {
        var updatedList = state.CurrentFilters.ToList();
        var filter = updatedList.FirstOrDefault(filter => filter.Id == action.Id);

        if (filter is null)
        {
            return state;
        }

        updatedList.Remove(filter);

        return state with { CurrentFilters = updatedList.AsReadOnly() };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceRemoveSubFilter(FilterPaneState state,
        FilterPaneAction.RemoveSubFilter action)
    {
        var updatedList = state.CurrentFilters.ToList();
        var parentFilter = updatedList.FirstOrDefault(parent => parent.Id == action.ParentId);

        if (parentFilter is null) { return state; }

        parentFilter.SubFilters.RemoveAll(filter => filter.Id == action.SubFilterId);

        return state with { CurrentFilters = updatedList.AsReadOnly() };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceSetAdvancedFilter(FilterPaneState state,
        FilterPaneAction.SetAdvancedFilter action) => state with { AdvancedFilter = action.Expression };

    [ReducerMethod]
    public static FilterPaneState ReduceSetFilter(FilterPaneState state, FilterPaneAction.SetFilter action)
    {
        return state with
        {
            CurrentFilters = state.CurrentFilters
                .Where(filter => filter.Id != action.FilterModel.Id)
                .Concat(new[] { action.FilterModel })
                .ToList().AsReadOnly()
        };
    }

    [ReducerMethod]
    public static FilterPaneState
        ReduceSetFilterDateRange(FilterPaneState state, FilterPaneAction.SetFilterDateRange action) =>
        state with { FilteredDateRange = action.FilterDateModel };

    [ReducerMethod(typeof(FilterPaneAction.ToggleAdvancedFilter))]
    public static FilterPaneState ReduceToggleAdvancedFilter(FilterPaneState state) =>
        state with { IsAdvancedFilterEnabled = !state.IsAdvancedFilterEnabled };

    [ReducerMethod]
    public static FilterPaneState ReduceToggleEditFilter(FilterPaneState state,
        FilterPaneAction.ToggleEditFilter action)
    {
        List<FilterModel> filters = new();

        foreach (var filterModel in state.CurrentFilters)
        {
            if (filterModel.Id == action.Id)
            {
                filterModel.IsEditing = !filterModel.IsEditing;
            }

            filters.Add(filterModel);
        }

        return state with { CurrentFilters = filters.AsReadOnly() };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceToggleEnableFilter(FilterPaneState state,
        FilterPaneAction.ToggleEnableFilter action)
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

        return state with { CurrentFilters = filters.AsReadOnly() };
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
