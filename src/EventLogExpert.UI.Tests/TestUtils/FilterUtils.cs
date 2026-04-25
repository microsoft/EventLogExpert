// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using System.Collections.Immutable;
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
        bool shouldCompareAny = false,
        FilterData? data = null,
        IEnumerable<FilterModel>? subFilters = null) =>
        new()
        {
            Color = color,
            FilterType = filterType,
            Data = data ?? new FilterData(),
            ShouldCompareAny = shouldCompareAny,
            IsEnabled = isEnabled,
            IsExcluded = isExcluded,
            Comparison = new FilterComparison { Value = comparisonValue },
            SubFilters = subFilters?.ToImmutableList() ?? []
        };

    internal static FilterEditorModel CreateTestFilterEditor(
        FilterId? id = null,
        string comparisonText = FilterIdEquals100,
        FilterType filterType = FilterType.Advanced,
        HighlightColor color = HighlightColor.None,
        bool isEnabled = false,
        bool isExcluded = false,
        BasicFilterCriteriaDraft? main = null,
        IEnumerable<BasicSubClauseDraft>? subClauses = null) =>
        new()
        {
            Id = id ?? FilterId.Create(),
            Color = color,
            ComparisonText = comparisonText,
            FilterType = filterType,
            Main = main ?? new BasicFilterCriteriaDraft(),
            SubClauses = subClauses?.ToList() ?? [],
            IsEnabled = isEnabled,
            IsExcluded = isExcluded
        };
}
