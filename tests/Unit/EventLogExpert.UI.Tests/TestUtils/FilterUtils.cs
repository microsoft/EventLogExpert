// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using static EventLogExpert.UI.Tests.TestUtils.Constants.Constants;

namespace EventLogExpert.UI.Tests.TestUtils;

internal static class FilterUtils
{
    internal static FilterModel CreateTestFilter(
        string comparisonValue = FilterIdEquals100,
        FilterType filterType = FilterType.Advanced,
        HighlightColor color = HighlightColor.None,
        bool isEnabled = false,
        bool isExcluded = false,
        BasicFilter? basicFilter = null,
        FilterId? id = null) =>
        FilterModel.TryCreate(comparisonValue, filterType, basicFilter, color, isExcluded, isEnabled, id) ??
        throw new InvalidOperationException($"Test filter expression failed to compile: '{comparisonValue}'");

    internal static FilterDraftModel CreateTestFilterDraft(
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
