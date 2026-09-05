// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.FilterLenses;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterPane;

internal sealed class Reducers
{
    [ReducerMethod]
    public static FilterPaneState ReduceAddFilter(FilterPaneState state, AddFilterAction action) =>
        state with { Filters = state.Filters.Add(action.SavedFilter) };

    [ReducerMethod(typeof(ClearAllFiltersAction))]
    public static FilterPaneState ReduceClearFilters(FilterPaneState state) => new() { IsEnabled = state.IsEnabled };

    [ReducerMethod]
    public static FilterPaneState ReduceCommitPromoted(FilterPaneState state, CommitPromotedLensAction action)
    {
        var filters = MergePromotedFilters(state.Filters, action.Filters);

        var date = action.Window is { IsEnabled: true } window ?
            EffectiveFilterBuilder.IntersectWindow(state.FilteredDateRange, window) :
            state.FilteredDateRange;

        return ReferenceEquals(filters, state.Filters) && date == state.FilteredDateRange ?
            state :
            state with { Filters = filters, FilteredDateRange = date };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceCommitPromotedLenses(FilterPaneState state, CommitPromotedLensesAction action)
    {
        var filters = state.Filters;
        var date = state.FilteredDateRange;

        // Fold every lens into the SAME running accumulators so batch == the sequential single-lens result: a filter
        // added for one lens deduplicates the next lens's identical filter, and each enabled window intersects
        // cumulatively (order-independent - dedup keys are exact and IntersectWindow is min/max of bounds).
        foreach (var commit in action.Commits)
        {
            filters = MergePromotedFilters(filters, commit.Filters);

            if (commit.Window is { IsEnabled: true } window)
            {
                date = EffectiveFilterBuilder.IntersectWindow(date, window);
            }
        }

        return ReferenceEquals(filters, state.Filters) && date == state.FilteredDateRange ?
            state :
            state with { Filters = filters, FilteredDateRange = date };
    }

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
    public static FilterPaneState ReduceRestoreFilterPaneState(FilterPaneState state, RestoreFilterPaneStateAction action) =>
        action.State;

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

    // Folds one lens's promoted filters into the RUNNING list (not a fresh read of state.Filters, so a batch dedups
    // across lenses): re-enable a disabled compiled-equivalent in place, leave a live one, else append a fresh copy.
    private static ImmutableList<SavedFilter> MergePromotedFilters(
        ImmutableList<SavedFilter> running,
        ImmutableList<SavedFilter> promotedFilters)
    {
        foreach (var promoted in promotedFilters)
        {
            // Match only a USABLE (compiled) equivalent, ordinal-exact so case-variant values stay distinct predicates
            // (filter string equality is ordinal, unlike the lowercased library MRU in ReduceMergeFilters). A dead
            // (uncompiled) ordinal match narrows nothing, so it is passed over and the working filter is added below.
            var existingIndex = running.FindIndex(filter =>
                string.Equals(filter.ComparisonText, promoted.ComparisonText, StringComparison.Ordinal) &&
                filter.Mode == promoted.Mode &&
                filter.IsExcluded == promoted.IsExcluded &&
                filter.Compiled is not null);

            if (existingIndex >= 0)
            {
                var existing = running[existingIndex];

                // Re-enable a disabled equivalent in place so the promoted narrowing stays active; an already-enabled
                // equivalent needs no change. Either way, no duplicate row.
                if (!existing.IsEnabled)
                {
                    running = running.SetItem(existingIndex, existing with { IsEnabled = true });
                }

                continue;
            }

            running = running.Add(promoted with { Id = FilterId.Create(), IsEnabled = promoted.Compiled is not null });
        }

        return running;
    }

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
