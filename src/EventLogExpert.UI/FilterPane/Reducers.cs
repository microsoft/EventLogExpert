// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Filter;
using Fluxor;

namespace EventLogExpert.UI.FilterPane;

public sealed class Reducers
{
    [ReducerMethod]
    public static FilterPaneState ReduceAddFilter(FilterPaneState state, AddFilterAction action) =>
        state with { Filters = state.Filters.Add(action.SavedFilter) };

    [ReducerMethod]
    public static FilterPaneState ReduceApplyFilterGroup(
        FilterPaneState state,
        ApplyFilterGroupAction action)
    {
        if (!action.FilterGroup.Filters.Any()) { return state; }

        // Dedupe key includes IsExcluded so include/exclude pairs of the same expression both land.
        HashSet<(string Value, bool IsExcluded)> existingKeys =
            [.. state.Filters.Select(filter => (filter.ComparisonText, filter.IsExcluded))];

        List<SavedFilter> additions = [];

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

    [ReducerMethod(typeof(ClearAllFiltersAction))]
    public static FilterPaneState ReduceClearFilters(FilterPaneState state) => new() { IsEnabled = state.IsEnabled };

    [ReducerMethod]
    public static FilterPaneState ReduceRemoveFilter(FilterPaneState state, RemoveFilterAction action)
    {
        var filter = state.Filters.FirstOrDefault(filter => filter.Id == action.Id);

        if (filter is null) { return state; }

        return state with { Filters = state.Filters.Remove(filter) };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceSetFilter(FilterPaneState state, SetFilterAction action)
    {
        // Upsert: replace-by-Id (preserving position) or append.
        var existing = state.Filters.FirstOrDefault(filter => filter.Id == action.SavedFilter.Id);

        if (existing is null)
        {
            return state with { Filters = state.Filters.Add(action.SavedFilter) };
        }

        var index = state.Filters.IndexOf(existing);

        return state with { Filters = state.Filters.SetItem(index, action.SavedFilter) };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceSetFilterDateRangeSuccess(
        FilterPaneState state,
        SetFilterDateRangeSuccessAction action) =>
        state with { FilteredDateRange = action.DateFilter };

    [ReducerMethod(typeof(ToggleFilterDateAction))]
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
        ToggleFilterEnabledAction action) =>
        UpdateFilterById(state, action.Id, filter => filter with { IsEnabled = !filter.IsEnabled });

    [ReducerMethod]
    public static FilterPaneState ReduceToggleFilterExcluded(
        FilterPaneState state,
        ToggleFilterExcludedAction action) =>
        UpdateFilterById(state, action.Id, filter => filter with { IsExcluded = !filter.IsExcluded });

    [ReducerMethod(typeof(ToggleIsEnabledAction))]
    public static FilterPaneState ReduceToggleIsEnabled(FilterPaneState state) =>
        state with { IsEnabled = !state.IsEnabled };

    private static FilterPaneState UpdateFilterById(
        FilterPaneState state,
        FilterId id,
        Func<SavedFilter, SavedFilter> transform)
    {
        var existing = state.Filters.FirstOrDefault(filter => filter.Id == id);

        if (existing is null) { return state; }

        var index = state.Filters.IndexOf(existing);

        return state with { Filters = state.Filters.SetItem(index, transform(existing)) };
    }
}
