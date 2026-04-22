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
            return state with { Filters = state.Filters.Add(new FilterModel { IsEditing = true }) };
        }

        return state with { Filters = state.Filters.Add(action.FilterModel) };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceAddSubFilter(FilterPaneState state, FilterPaneAction.AddSubFilter action)
    {
        var parent = state.Filters.FirstOrDefault(filter => filter.Id == action.ParentId);

        if (parent is null) { return state; }

        var index = state.Filters.IndexOf(parent);

        return state with
        {
            Filters = state.Filters.SetItem(
                index,
                parent with { SubFilters = parent.SubFilters.Add(new FilterModel()) })
        };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceApplyFilterGroup(
        FilterPaneState state,
        FilterPaneAction.ApplyFilterGroup action)
    {
        if (!action.FilterGroup.Filters.Any()) { return state; }

        // Dedupe on (Comparison.Value, IsExcluded) so an "Id == 100" include and an "Id == 100" exclude
        // are treated as semantically different filters and both can land in the pane.
        HashSet<(string Value, bool IsExcluded)> existingKeys =
            [.. state.Filters.Select(filter => (filter.Comparison.Value, filter.IsExcluded))];

        List<FilterModel> additions = [];

        foreach (var filter in action.FilterGroup.Filters)
        {
            if (!existingKeys.Add((filter.Comparison.Value, filter.IsExcluded))) { continue; }

            additions.Add(new FilterModel
            {
                Color = filter.Color,
                Comparison = filter.Comparison with { },
                IsEnabled = true,
                IsExcluded = filter.IsExcluded
            });
        }

        return additions.Count == 0 ? state : state with { Filters = state.Filters.AddRange(additions) };
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
        var parent = state.Filters.FirstOrDefault(filter => filter.Id == action.ParentId);

        if (parent is null) { return state; }

        var updatedSubFilters = parent.SubFilters.RemoveAll(filter => filter.Id == action.SubFilterId);

        // ImmutableList<T>.RemoveAll returns the same instance when nothing matched.
        if (ReferenceEquals(updatedSubFilters, parent.SubFilters)) { return state; }

        var index = state.Filters.IndexOf(parent);

        return state with
        {
            Filters = state.Filters.SetItem(index, parent with { SubFilters = updatedSubFilters })
        };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceSetFilter(FilterPaneState state, FilterPaneAction.SetFilter action)
    {
        // Upsert: replace by Id if present (preserving position), append if not. ContextMenu and
        // the FilterRow save path both rely on the append branch.
        var existing = state.Filters.FirstOrDefault(filter => filter.Id == action.FilterModel.Id);

        if (existing is null)
        {
            return state with { Filters = state.Filters.Add(action.FilterModel) };
        }

        var index = state.Filters.IndexOf(existing);

        return state with { Filters = state.Filters.SetItem(index, action.FilterModel) };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceSetFilterDateRangeSuccess(
        FilterPaneState state,
        FilterPaneAction.SetFilterDateRangeSuccess action) =>
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
        FilterPaneAction.ToggleFilterEditing action) =>
        UpdateFilterById(state, action.Id, filter => filter with { IsEditing = !filter.IsEditing });

    [ReducerMethod]
    public static FilterPaneState ReduceToggleFilterEnabled(
        FilterPaneState state,
        FilterPaneAction.ToggleFilterEnabled action) =>
        UpdateFilterById(state, action.Id, filter => filter with { IsEnabled = !filter.IsEnabled });

    [ReducerMethod]
    public static FilterPaneState ReduceToggleFilterExcluded(
        FilterPaneState state,
        FilterPaneAction.ToggleFilterExcluded action) =>
        UpdateFilterById(state, action.Id, filter => filter with { IsExcluded = !filter.IsExcluded });

    [ReducerMethod(typeof(FilterPaneAction.ToggleIsEnabled))]
    public static FilterPaneState ReduceToggleIsEnabled(FilterPaneState state) =>
        state with { IsEnabled = !state.IsEnabled };

    [ReducerMethod(typeof(FilterPaneAction.ToggleIsLoading))]
    public static FilterPaneState ReduceToggleIsLoading(FilterPaneState state) =>
        state with { IsLoading = !state.IsLoading };

    private static FilterPaneState UpdateFilterById(
        FilterPaneState state,
        FilterId id,
        Func<FilterModel, FilterModel> transform)
    {
        var existing = state.Filters.FirstOrDefault(filter => filter.Id == id);

        if (existing is null) { return state; }

        var index = state.Filters.IndexOf(existing);

        return state with { Filters = state.Filters.SetItem(index, transform(existing)) };
    }
}
