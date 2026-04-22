// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

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

    /// <summary>
    /// Creates a draft editor populated from a saved filter. Deep-copies <see cref="Data"/> and
    /// recursively converts <see cref="SubFilters"/> so edits to the draft never reach state.
    /// </summary>
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
    /// Materializes an immutable <see cref="FilterModel"/> from the current draft state, compiling
    /// the comparison expression. Throws if <see cref="ComparisonText"/> is not parseable.
    /// </summary>
    public FilterModel ToFilterModel()
    {
        var model = new FilterModel
        {
            Id = Id,
            Color = Color,
            FilterType = FilterType,
            Data = CloneFilterData(Data),
            SubFilters = SubFilters.Select(child => child.ToFilterModel()).ToList(),
            ShouldCompareAny = ShouldCompareAny,
            IsEnabled = IsEnabled,
            IsExcluded = IsExcluded
        };

        if (!string.IsNullOrEmpty(ComparisonText))
        {
            model.Comparison = new FilterComparison { Value = ComparisonText };
        }

        return model;
    }

    private static FilterData CloneFilterData(FilterData source)
    {
        // Category setter clears Value/Values — assign Category first, then restore Value/Values.
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
