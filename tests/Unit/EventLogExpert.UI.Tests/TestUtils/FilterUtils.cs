// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Filter;
using static EventLogExpert.UI.Tests.TestUtils.Constants.Constants;

namespace EventLogExpert.UI.Tests.TestUtils;

internal static class FilterUtils
{
    internal static SavedFilter CreateTestFilter(
        string comparisonValue = FilterIdEquals100,
        FilterType filterType = FilterType.Advanced,
        HighlightColor color = HighlightColor.None,
        bool isEnabled = false,
        bool isExcluded = false,
        BasicFilter? basicFilter = null,
        FilterId? id = null) =>
        SavedFilter.TryCreate(comparisonValue, filterType, basicFilter, color, isExcluded, isEnabled, id) ??
        throw new InvalidOperationException($"Test filter expression failed to compile: '{comparisonValue}'");

    internal static FilterDraft CreateTestFilterDraft(
        FilterId? id = null,
        string comparisonText = FilterIdEquals100,
        FilterType filterType = FilterType.Advanced,
        HighlightColor color = HighlightColor.None,
        bool isEnabled = false,
        bool isExcluded = false,
        FilterDataDraft? comparison = null,
        IEnumerable<SubFilterDraft>? subFilters = null) =>
        new()
        {
            Id = id ?? FilterId.Create(),
            Color = color,
            ComparisonText = comparisonText,
            FilterType = filterType,
            Comparison = comparison ?? new FilterDataDraft(),
            SubFilters = subFilters?.ToList() ?? [],
            IsEnabled = isEnabled,
            IsExcluded = isExcluded
        };
}
