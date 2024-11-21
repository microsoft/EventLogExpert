// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Fluxor;

namespace EventLogExpert.UI.Store.FilterPane;

public sealed class FilterPaneReducers
{
    [ReducerMethod]
    public static FilterPaneState ReduceAddFilter(FilterPaneState state, FilterPaneAction.AddFilter action)
    {
        if (action.FilterModel is null)
        {
            return state with { Filters = [.. state.Filters, new FilterModel { IsEditing = true }] };
        }

        return state with { Filters = [.. state.Filters, action.FilterModel] };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceAddSubFilter(FilterPaneState state, FilterPaneAction.AddSubFilter action)
    {
        var updatedList = state.Filters.ToList();
        var parentFilter = updatedList.FirstOrDefault(parent => parent.Id == action.ParentId);

        if (parentFilter is null) { return state; } // If not parent filter, something went wrong and bail

        parentFilter.SubFilters.Add(new FilterModel());

        return state with { Filters = [.. updatedList] };
    }

    [ReducerMethod(typeof(FilterPaneAction.ToggleIsXmlEnabled))]
    public static FilterPaneState ReduceToggleIsXmlEnabled(FilterPaneState state) =>
        state with { IsXmlEnabled = !state.IsXmlEnabled };

    [ReducerMethod]
    public static FilterPaneState ReduceApplyFilterGroup(
        FilterPaneState state,
        FilterPaneAction.ApplyFilterGroup action)
    {
        if (!action.FilterGroup.Filters.Any()) { return state; }

        List<FilterModel> updatedList = [];

        foreach (var filter in action.FilterGroup.Filters)
        {
            if (state.Filters.FirstOrDefault(f =>
                string.Equals(f.Comparison.Value, filter.Comparison.Value)) is not null) { continue; }

            updatedList.Add(new FilterModel
            {
                Color = filter.Color,
                Comparison = filter.Comparison with { },
                IsEnabled = true
            });
        }

        return state with { Filters = state.Filters.AddRange(updatedList) };
    }

    [ReducerMethod(typeof(FilterPaneAction.ClearAllFilters))]
    public static FilterPaneState ReduceClearFilters(FilterPaneState state) => new() { IsEnabled = state.IsEnabled };

    [ReducerMethod]
    public static FilterPaneState ReduceRemoveFilter(FilterPaneState state, FilterPaneAction.RemoveFilter action)
    {
        var filter = state.Filters.FirstOrDefault(filter => filter.Id == action.Id);

        if (filter is null) { return state; }

        return state with { Filters = state.Filters.Remove(filter) };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceRemoveSubFilter(FilterPaneState state, FilterPaneAction.RemoveSubFilter action)
    {
        var parentFilter = state.Filters.FirstOrDefault(parent => parent.Id == action.ParentId);

        if (parentFilter is null) { return state; }

        parentFilter.SubFilters.RemoveAll(filter => filter.Id == action.SubFilterId);

        return state with { Filters = state.Filters.Remove(parentFilter).Add(parentFilter) };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceSetFilter(FilterPaneState state, FilterPaneAction.SetFilter action) =>
        state with
        {
            Filters =
            [
                .. state.Filters
                    .Where(filter => filter.Id != action.FilterModel.Id)
                    .Concat([action.FilterModel])
            ]
        };

    [ReducerMethod]
    public static FilterPaneState
        ReduceSetFilterDateRange(FilterPaneState state, FilterPaneAction.SetFilterDateRange action) =>
        state with { FilteredDateRange = action.FilterDateModel };

    [ReducerMethod(typeof(FilterPaneAction.ToggleFilterDate))]
    public static FilterPaneState ReduceToggleFilterDate(FilterPaneState state)
    {
        if (state.FilteredDateRange is null) { return state; }

        return state with
        {
            FilteredDateRange = state.FilteredDateRange with { IsEnabled = !state.FilteredDateRange.IsEnabled }
        };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceToggleFilterEditing(
        FilterPaneState state,
        FilterPaneAction.ToggleFilterEditing action)
    {
        List<FilterModel> filters = [];

        foreach (var filterModel in state.Filters)
        {
            if (filterModel.Id == action.Id)
            {
                filterModel.IsEditing = !filterModel.IsEditing;
            }

            filters.Add(filterModel);
        }

        return state with { Filters = [.. filters] };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceToggleFilterEnabled(
        FilterPaneState state,
        FilterPaneAction.ToggleFilterEnabled action)
    {
        List<FilterModel> filters = [];

        foreach (var filterModel in state.Filters)
        {
            if (filterModel.Id == action.Id)
            {
                filterModel.IsEnabled = !filterModel.IsEnabled;
            }

            filters.Add(filterModel);
        }

        return state with { Filters = [.. filters] };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceToggleFilterExcluded(
        FilterPaneState state,
        FilterPaneAction.ToggleFilterExcluded action)
    {
        List<FilterModel> filters = [];

        foreach (var filterModel in state.Filters)
        {
            if (filterModel.Id == action.Id)
            {
                filterModel.IsExcluded = !filterModel.IsExcluded;
            }

            filters.Add(filterModel);
        }

        return state with { Filters = [.. filters] };
    }

    [ReducerMethod(typeof(FilterPaneAction.ToggleIsEnabled))]
    public static FilterPaneState ReduceToggleIsEnabled(FilterPaneState state) =>
        state with { IsEnabled = !state.IsEnabled };

    [ReducerMethod(typeof(FilterPaneAction.ToggleIsLoading))]
    public static FilterPaneState ReduceToggleIsLoading(FilterPaneState state) =>
        state with { IsLoading = !state.IsLoading };
}
