// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.UI.Models;

public sealed class FilterEditorModel
{
    public HighlightColor Color { get; set; } = HighlightColor.None;

    public string ComparisonText { get; set; } = string.Empty;

    public FilterData Data { get; set; } = new();

    public FilterType FilterType { get; set; } = FilterType.Advanced;

    public FilterId Id { get; init; } = FilterId.Create();

    public bool IsEnabled { get; set; }

    public bool IsExcluded { get; set; }

    public bool ShouldCompareAny { get; set; }

    public List<FilterEditorModel> SubFilters { get; set; } = [];

    /// <summary>Creates a deep-copied draft editor from a saved filter.</summary>
    public static FilterEditorModel FromFilterModel(FilterModel filter) =>
        new()
        {
            Id = filter.Id,
            Color = filter.Color,
            ComparisonText = filter.Comparison.Value,
            FilterType = filter.FilterType,
            Data = CloneFilterData(filter.Data),
            SubFilters = filter.SubFilters.Select(FromFilterModel).ToList(),
            ShouldCompareAny = filter.ShouldCompareAny,
            IsEnabled = filter.IsEnabled,
            IsExcluded = filter.IsExcluded
        };

    /// <summary>
    ///     Materializes an immutable <see cref="FilterModel" /> from the draft. Throws if <see cref="ComparisonText" />
    ///     is not parseable.
    /// </summary>
    public FilterModel ToFilterModel() =>
        new()
        {
            Id = Id,
            Color = Color,
            FilterType = FilterType,
            Data = CloneFilterData(Data),
            SubFilters = SubFilters.Select(child => child.ToFilterModel()).ToImmutableList(),
            ShouldCompareAny = ShouldCompareAny,
            IsEnabled = IsEnabled,
            IsExcluded = IsExcluded,
            Comparison = string.IsNullOrEmpty(ComparisonText)
                ? new FilterComparison()
                : new FilterComparison { Value = ComparisonText }
        };

    private static FilterData CloneFilterData(FilterData source)
    {
        // Category setter clears Value/Values; assign it first.
        var clone = new FilterData
        {
            Category = source.Category,
            Evaluator = source.Evaluator,
            Value = source.Value,
            Values = [.. source.Values]
        };

        return clone;
    }
}
