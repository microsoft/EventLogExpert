// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Fluxor;

namespace EventLogExpert.UI.Store.FilterPane;

public sealed class FilterPaneReducers
{
    [ReducerMethod]
    public static FilterPaneState ReduceAddAdvancedFilter(FilterPaneState state,
        FilterPaneAction.AddAdvancedFilter action)
    {
        if (state.AdvancedFilters.IsEmpty)
        {
            return state with { AdvancedFilters = [new FilterModel { IsEditing = true }] };
        }

        if (action.FilterModel is null)
        {
            return state with { AdvancedFilters = [.. state.BasicFilters, new FilterModel { IsEditing = true }] };
        }

        return state with { AdvancedFilters = [.. state.BasicFilters, action.FilterModel] };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceAddBasicFilter(FilterPaneState state, FilterPaneAction.AddBasicFilter action)
    {
        if (state.BasicFilters.IsEmpty)
        {
            return state with { BasicFilters = [new FilterModel { IsEditing = true }] };
        }

        if (action.FilterModel is null)
        {
            return state with { BasicFilters = [.. state.BasicFilters, new FilterModel { IsEditing = true }] };
        }

        return state with { BasicFilters = [.. state.BasicFilters, action.FilterModel] };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceAddCachedFilter(FilterPaneState state, FilterPaneAction.AddCachedFilter action)
    {
        if (state.CachedFilters.Contains(action.FilterModel)) { return state; }

        action.FilterModel.IsEnabled = true;

        return state with { CachedFilters = state.CachedFilters.Add(action.FilterModel) };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceAddSubFilter(FilterPaneState state, FilterPaneAction.AddSubFilter action)
    {
        var updatedList = state.BasicFilters.ToList();
        var parentFilter = updatedList.FirstOrDefault(parent => parent.Id == action.ParentId);

        if (parentFilter is null) { return state; } // If not parent filter, something went wrong and bail

        parentFilter.SubFilters.Add(new FilterModel());

        return state with { BasicFilters = [.. updatedList] };
    }

    [ReducerMethod(typeof(FilterPaneAction.ClearAllFilters))]
    public static FilterPaneState ReduceClearFilters(FilterPaneState state) => new();

    [ReducerMethod]
    public static FilterPaneState ReduceRemoveAdvancedFilter(
        FilterPaneState state,
        FilterPaneAction.RemoveAdvancedFilter action)
    {
        var filter = state.AdvancedFilters.FirstOrDefault(filter => filter.Id == action.Id);

        if (filter is null) { return state; }

        return state with { AdvancedFilters = state.AdvancedFilters.Remove(filter) };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceRemoveBasicFilter(
        FilterPaneState state,
        FilterPaneAction.RemoveBasicFilter action)
    {
        var filter = state.BasicFilters.FirstOrDefault(filter => filter.Id == action.Id);

        if (filter is null) { return state; }

        return state with { BasicFilters = state.BasicFilters.Remove(filter) };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceRemoveCachedFilter(
        FilterPaneState state,
        FilterPaneAction.RemoveCachedFilter action)
    {
        var filter = state.CachedFilters.FirstOrDefault(filter => filter.Id == action.Id);

        if (filter is null) { return state; }

        return state with { CachedFilters = state.CachedFilters.Remove(filter) };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceRemoveSubFilter(
        FilterPaneState state,
        FilterPaneAction.RemoveSubFilter action)
    {
        var parentFilter = state.BasicFilters.FirstOrDefault(parent => parent.Id == action.ParentId);

        if (parentFilter is null) { return state; }

        parentFilter.SubFilters.RemoveAll(filter => filter.Id == action.SubFilterId);

        return state with { BasicFilters = state.BasicFilters.Remove(parentFilter).Add(parentFilter) };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceSetAdvancedFilter(
        FilterPaneState state,
        FilterPaneAction.SetAdvancedFilter action) => state with
    {
        AdvancedFilters =
        [
            .. state.AdvancedFilters
                .Where(filter => filter.Id != action.FilterModel.Id)
                .Concat([action.FilterModel])
        ]
    };

    [ReducerMethod]
    public static FilterPaneState ReduceSetBasicFilter(FilterPaneState state, FilterPaneAction.SetBasicFilter action) =>
        state with
        {
            BasicFilters =
            [
                .. state.BasicFilters
                    .Where(filter => filter.Id != action.FilterModel.Id)
                    .Concat([action.FilterModel])
            ]
        };

    [ReducerMethod]
    public static FilterPaneState
        ReduceSetFilterDateRange(FilterPaneState state, FilterPaneAction.SetFilterDateRange action) =>
        state with { FilteredDateRange = action.FilterDateModel };

    [ReducerMethod]
    public static FilterPaneState ReduceToggleAdvancedFilterEditing(
        FilterPaneState state,
        FilterPaneAction.ToggleAdvancedFilterEditing action)
    {
        List<FilterModel> filters = [];

        foreach (var filterModel in state.AdvancedFilters)
        {
            if (filterModel.Id == action.Id)
            {
                filterModel.IsEditing = !filterModel.IsEditing;
            }

            filters.Add(filterModel);
        }

        return state with { AdvancedFilters = [.. filters] };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceToggleAdvancedFilterEnabled(
        FilterPaneState state,
        FilterPaneAction.ToggleAdvancedFilterEnabled action)
    {
        List<FilterModel> filters = [];

        foreach (var filterModel in state.AdvancedFilters)
        {
            if (filterModel.Id == action.Id)
            {
                filterModel.IsEnabled = !filterModel.IsEnabled;
            }

            filters.Add(filterModel);
        }

        return state with { AdvancedFilters = [.. filters] };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceToggleBasicFilterEditing(
        FilterPaneState state,
        FilterPaneAction.ToggleBasicFilterEditing action)
    {
        List<FilterModel> filters = [];

        foreach (var filterModel in state.BasicFilters)
        {
            if (filterModel.Id == action.Id)
            {
                filterModel.IsEditing = !filterModel.IsEditing;
            }

            filters.Add(filterModel);
        }

        return state with { BasicFilters = [.. filters] };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceToggleBasicFilterEnabled(
        FilterPaneState state,
        FilterPaneAction.ToggleBasicFilterEnabled action)
    {
        List<FilterModel> filters = [];

        foreach (var filterModel in state.BasicFilters)
        {
            if (filterModel.Id == action.Id)
            {
                filterModel.IsEnabled = !filterModel.IsEnabled;
            }

            filters.Add(filterModel);
        }

        return state with { BasicFilters = [.. filters] };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceToggleCachedFilter(
        FilterPaneState state,
        FilterPaneAction.ToggleCachedFilter action)
    {
        List<FilterModel> filters = [];

        foreach (var filterModel in state.CachedFilters)
        {
            if (filterModel.Id == action.Id)
            {
                filterModel.IsEnabled = !filterModel.IsEnabled;
            }

            filters.Add(filterModel);
        }

        return state with { CachedFilters = [.. filters] };
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

    [ReducerMethod(typeof(FilterPaneAction.ToggleIsEnabled))]
    public static FilterPaneState ReduceToggleIsEnabled(FilterPaneState state) =>
        state with { IsEnabled = !state.IsEnabled };

    [ReducerMethod(typeof(FilterPaneAction.ToggleIsLoading))]
    public static FilterPaneState ReduceToggleIsLoading(FilterPaneState state) =>
        state with { IsLoading = !state.IsLoading };

    [ReducerMethod]
    public FilterPaneState ReduceApplyFilterGroup(FilterPaneState state, FilterPaneAction.ApplyFilterGroup action)
    {
        if (!action.FilterGroup.Filters.Any()) { return state; }

        List<FilterModel> updatedList = [];

        foreach (var filter in action.FilterGroup.Filters)
        {
            updatedList.Add(filter);
        }

        return state with { AdvancedFilters = state.AdvancedFilters.AddRange(updatedList) };
    }
}
