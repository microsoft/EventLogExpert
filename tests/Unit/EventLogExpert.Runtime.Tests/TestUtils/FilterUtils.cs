// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Drafts;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Filtering.Runtime;
using static EventLogExpert.Runtime.Tests.TestUtils.Constants.Constants;

namespace EventLogExpert.Runtime.Tests.TestUtils;

internal static class FilterUtils
{
    internal static SavedFilter CreateTestFilter(
        string comparisonValue = FilterIdEquals100,
        HighlightColor color = HighlightColor.None,
        bool isEnabled = false,
        bool isExcluded = false,
        BasicFilter? basicFilter = null,
        FilterId? id = null,
        FilterMode mode = FilterMode.Advanced) =>
        SavedFilter.TryCreate(
            comparisonValue,
            basicFilter,
            color,
            isExcluded,
            isEnabled,
            id,
            basicFilter is not null ? FilterMode.Basic : mode) ??
        throw new InvalidOperationException($"Test filter expression failed to compile: '{comparisonValue}'");

    internal static FilterDraft CreateTestFilterDraft(
        FilterId? id = null,
        string comparisonText = FilterIdEquals100,
        FilterMode mode = FilterMode.Advanced,
        HighlightColor color = HighlightColor.None,
        bool isEnabled = false,
        bool isExcluded = false,
        FilterComparisonDraft? comparison = null,
        IEnumerable<SubFilterDraft>? subFilters = null) =>
        new()
        {
            Id = id ?? FilterId.Create(),
            Color = color,
            ComparisonText = comparisonText,
            Mode = mode,
            Comparison = comparison ?? new FilterComparisonDraft(),
            SubFilters = subFilters?.ToList() ?? [],
            IsEnabled = isEnabled,
            IsExcluded = isExcluded
        };
}
