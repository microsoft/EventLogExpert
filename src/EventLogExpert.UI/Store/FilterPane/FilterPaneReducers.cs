// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.FilterPane;

public sealed class FilterPaneReducers
{
    [ReducerMethod]
    public static FilterPaneState ReduceAddCachedFilter(FilterPaneState state, FilterPaneAction.AddCachedFilter action)
    {
        if (state.CachedFilters.Contains(action.AdvancedFilterModel)) { return state; }

        return state with
        {
            CachedFilters = state.CachedFilters.Add(action.AdvancedFilterModel)
        };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceAddFilter(FilterPaneState state, FilterPaneAction.AddFilter action)
    {
        if (!state.CurrentFilters.Any())
        {
            return state with { CurrentFilters = new List<FilterModel> { new() }.ToImmutableList() };
        }

        if (action.FilterModel is null)
        {
            return state with
            {
                CurrentFilters = state.CurrentFilters.Concat(new[] { new FilterModel() })
                    .ToImmutableList()
            };
        }

        return state with
        {
            CurrentFilters = state.CurrentFilters.Concat(new[] { action.FilterModel })
                .ToImmutableList()
        };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceAddSubFilter(FilterPaneState state, FilterPaneAction.AddSubFilter action)
    {
        var updatedList = state.CurrentFilters.ToList();
        var parentFilter = updatedList.FirstOrDefault(parent => parent.Id == action.ParentId);

        if (parentFilter is null) { return state; } // If no parent filter, something went wrong and bail

        parentFilter.SubFilters.Add(new SubFilterModel());

        return state with { CurrentFilters = updatedList.ToImmutableList() };
    }

    [ReducerMethod(typeof(FilterPaneAction.ClearAllFilters))]
    public static FilterPaneState ReduceClearFilters(FilterPaneState state) => new();

    [ReducerMethod]
    public static FilterPaneState ReduceRemoveCachedFilter(FilterPaneState state, FilterPaneAction.RemoveCachedFilter action)
    {
        if (!state.CachedFilters.Contains(action.AdvancedFilterModel)) { return state; }

        return state with
        {
            CachedFilters = state.CachedFilters.Remove(action.AdvancedFilterModel)
        };
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

        return state with { CurrentFilters = updatedList.ToImmutableList() };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceRemoveSubFilter(FilterPaneState state,
        FilterPaneAction.RemoveSubFilter action)
    {
        var updatedList = state.CurrentFilters.ToList();
        var parentFilter = updatedList.FirstOrDefault(parent => parent.Id == action.ParentId);

        if (parentFilter is null) { return state; }

        parentFilter.SubFilters.RemoveAll(filter => filter.Id == action.SubFilterId);

        return state with { CurrentFilters = updatedList.ToImmutableList() };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceSetAdvancedFilter(FilterPaneState state,
        FilterPaneAction.SetAdvancedFilter action) => state with
    {
        AdvancedFilter = action.AdvancedFilterModel
    };

    [ReducerMethod]
    public static FilterPaneState ReduceSetFilter(FilterPaneState state, FilterPaneAction.SetFilter action) =>
        state with
        {
            CurrentFilters = state.CurrentFilters
                .Where(filter => filter.Id != action.FilterModel.Id)
                .Concat(new[] { action.FilterModel })
                .ToImmutableList()
        };

    [ReducerMethod]
    public static FilterPaneState
        ReduceSetFilterDateRange(FilterPaneState state, FilterPaneAction.SetFilterDateRange action) =>
        state with { FilteredDateRange = action.FilterDateModel };

    [ReducerMethod(typeof(FilterPaneAction.ToggleAdvancedFilter))]
    public static FilterPaneState ReduceToggleAdvancedFilter(FilterPaneState state)
    {
        if (state.AdvancedFilter is null) { return state; }

        return state with
        {
            AdvancedFilter = state.AdvancedFilter with { IsEnabled = !state.AdvancedFilter.IsEnabled }
        };

    }

    [ReducerMethod]
    public static FilterPaneState ReduceToggleCachedFilter(FilterPaneState state, FilterPaneAction.ToggleCachedFilter action)
    {
        List<AdvancedFilterModel> filters = [];

        foreach (var filterModel in state.CachedFilters)
        {
            if (filterModel == action.AdvancedFilterModel)
            {
                filterModel.IsEnabled = !filterModel.IsEnabled;
            }

            filters.Add(filterModel);
        }

        return state with { CachedFilters = filters.ToImmutableList() };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceToggleEditFilter(FilterPaneState state,
        FilterPaneAction.ToggleEditFilter action)
    {
        List<FilterModel> filters = [];

        foreach (var filterModel in state.CurrentFilters)
        {
            if (filterModel.Id == action.Id)
            {
                filterModel.IsEditing = !filterModel.IsEditing;
            }

            filters.Add(filterModel);
        }

        return state with { CurrentFilters = filters.ToImmutableList() };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceToggleEnableFilter(FilterPaneState state,
        FilterPaneAction.ToggleEnableFilter action)
    {
        List<FilterModel> filters = [];

        foreach (var filterModel in state.CurrentFilters)
        {
            if (filterModel.Id == action.Id)
            {
                filterModel.IsEnabled = !filterModel.IsEnabled;
            }

            filters.Add(filterModel);
        }

        return state with { CurrentFilters = filters.ToImmutableList() };
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
