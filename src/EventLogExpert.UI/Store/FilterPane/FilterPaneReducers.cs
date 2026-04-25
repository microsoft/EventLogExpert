// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Fluxor;

namespace EventLogExpert.UI.Store.FilterPane;

public sealed class FilterPaneReducers
{
    [ReducerMethod]
    public static FilterPaneState ReduceAddFilter(FilterPaneState state, FilterPaneAction.AddFilter action) =>
        state with { Filters = state.Filters.Add(action.FilterModel) };

    [ReducerMethod]
    public static FilterPaneState ReduceApplyFilterGroup(
        FilterPaneState state,
        FilterPaneAction.ApplyFilterGroup action)
    {
        if (!action.FilterGroup.Filters.Any()) { return state; }

        // Dedupe key includes IsExcluded so include/exclude pairs of the same expression both land.
        HashSet<(string Value, bool IsExcluded)> existingKeys =
            [.. state.Filters.Select(filter => (filter.ComparisonText, filter.IsExcluded))];

        List<FilterModel> additions = [];

        foreach (var filter in action.FilterGroup.Filters)
        {
            if (!existingKeys.Add((filter.ComparisonText, filter.IsExcluded))) { continue; }

            // Preserve the group filter as-is, but only enable when Compiled is non-null. A saved group
            // filter loaded with an invalid expression has Compiled == null and must stay disabled, otherwise
            // it appears active in the UI but is silently skipped by filtering/highlighting.
            additions.Add(filter with { Id = FilterId.Create(), IsEnabled = filter.Compiled is not null });
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
    public static FilterPaneState ReduceSetFilter(FilterPaneState state, FilterPaneAction.SetFilter action)
    {
        // Upsert: replace-by-Id (preserving position) or append.
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

    [ReducerMethod]
    public static FilterPaneState ReduceSetIsLoading(FilterPaneState state, FilterPaneAction.SetIsLoading action) =>
        state.IsLoading == action.IsLoading ? state : state with { IsLoading = action.IsLoading };

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
