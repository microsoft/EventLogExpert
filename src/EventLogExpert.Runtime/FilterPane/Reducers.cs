// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterPane;

internal sealed class Reducers
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

        // Dedup tuple matches FilterLibrary store invariant: case-insensitive ComparisonText
        // + Mode + IsExcluded. See FilterLibrarySqliteStore.idx_library_autotracked_dedup
        // and ReduceMergeFilters for parallel implementations.
        HashSet<(string LoweredText, FilterMode Mode, bool IsExcluded)> existingKeys =
            [.. state.Filters.Select(filter => (
                filter.ComparisonText.ToLowerInvariant(),
                filter.Mode,
                filter.IsExcluded))];

        List<SavedFilter> additions = [];

        foreach (var filter in action.FilterGroup.Filters)
        {
            if (!existingKeys.Add((filter.ComparisonText.ToLowerInvariant(), filter.Mode, filter.IsExcluded))) { continue; }

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
    public static FilterPaneState ReduceMergeFilters(FilterPaneState state, MergeFiltersAction action)
    {
        if (action.Filters.IsEmpty) { return state; }

        // Dedup tuple matches the FilterLibrary store's invariant: case-insensitive ComparisonText
        // + Mode + IsExcluded. See FilterLibrarySqliteStore.idx_library_autotracked_dedup.
        HashSet<(string LoweredText, FilterMode Mode, bool IsExcluded)> existingKeys =
            [.. state.Filters.Select(filter => (
                filter.ComparisonText.ToLowerInvariant(),
                filter.Mode,
                filter.IsExcluded))];

        List<SavedFilter> additions = [];

        foreach (var filter in action.Filters)
        {
            if (!existingKeys.Add((filter.ComparisonText.ToLowerInvariant(), filter.Mode, filter.IsExcluded))) { continue; }

            additions.Add(filter with { Id = FilterId.Create(), IsEnabled = filter.Compiled is not null });
        }

        return additions.Count == 0 ? state : state with { Filters = state.Filters.AddRange(additions) };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceRemoveFilter(FilterPaneState state, RemoveFilterAction action)
    {
        var filter = state.Filters.FirstOrDefault(filter => filter.Id == action.Id);

        if (filter is null) { return state; }

        return state with { Filters = state.Filters.Remove(filter) };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceReplaceFilters(FilterPaneState state, ReplaceFiltersAction action)
    {
        var replaced = action.Filters
            .Select(filter => filter with { Id = FilterId.Create(), IsEnabled = filter.Compiled is not null })
            .ToImmutableList();

        return state with { Filters = replaced };
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

    [ReducerMethod]
    public static FilterPaneState ReduceSetFilterExcluded(
        FilterPaneState state,
        SetFilterExcludedAction action) =>
        UpdateFilterById(state, action.Id, filter => filter with { IsExcluded = action.IsExcluded });

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
