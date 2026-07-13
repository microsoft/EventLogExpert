// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Evaluation;

namespace EventLogExpert.Runtime.FilterPane;

/// <summary>
///     Builds the base (lens-free) <see cref="Filter" /> from the current <see cref="FilterPaneState" />. Shared by
///     the FilterPane apply effect and the FilterLens effect so both compose lenses onto an identical base. When filtering
///     is disabled only exclusion filters (plus the date range) apply, matching the historical
///     <c>UpdateEventTableFilters</c> branch.
/// </summary>
internal static class FilterPaneFilterBuilder
{
    public static Filter Build(FilterPaneState state) =>
        state.IsEnabled
            ? new Filter(state.FilteredDateRange, [.. state.Filters.Where(filter => filter.IsEnabled)])
            : new Filter(state.FilteredDateRange, [.. state.Filters.Where(filter => filter.IsExcluded)]);
}
