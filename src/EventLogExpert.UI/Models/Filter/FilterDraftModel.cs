// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Models;

public sealed class FilterDraftModel
{
    public HighlightColor Color { get; set; } = HighlightColor.None;

    public FilterDataDraft Comparison { get; set; } = new();

    public string ComparisonText { get; set; } = string.Empty;

    public FilterType FilterType { get; set; } = FilterType.Advanced;

    public FilterId Id { get; init; } = FilterId.Create();

    public bool IsEnabled { get; set; }

    public bool IsExcluded { get; set; }

    public List<SubFilterDraft> SubFilters { get; set; } = [];

    /// <summary>
    ///     Hydrates Basic structure from <see cref="FilterModel.BasicFilter" /> when present so Basic re-edit reopens
    ///     with the original comparison + sub-filters; otherwise opens empty Basic fields with just
    ///     <see cref="FilterModel.ComparisonText" /> populated.
    /// </summary>
    public static FilterDraftModel FromFilterModel(FilterModel filter)
    {
        var basicFilter = filter.FilterType == FilterType.Basic ? filter.BasicFilter : null;

        return new FilterDraftModel
        {
            Id = filter.Id,
            Color = filter.Color,
            ComparisonText = filter.ComparisonText,
            FilterType = filter.FilterType,
            IsEnabled = filter.IsEnabled,
            IsExcluded = filter.IsExcluded,
            Comparison = basicFilter is null
                ? new FilterDataDraft()
                : FilterDataDraft.FromData(basicFilter.Comparison),
            SubFilters = basicFilter is null
                ? []
                : [.. basicFilter.SubFilters.Select(SubFilterDraftFromSubFilter)]
        };
    }

    public BasicFilter ToBasicFilter() =>
        new(Comparison.ToData(), [.. SubFilters.Select(subFilter => subFilter.ToSubFilter())]);

    private static SubFilterDraft SubFilterDraftFromSubFilter(SubFilter subFilter) =>
        new()
        {
            Data = FilterDataDraft.FromData(subFilter.Data),
            JoinWithAny = subFilter.JoinWithAny
        };
}
