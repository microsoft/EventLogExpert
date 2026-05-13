// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering;

namespace EventLogExpert.UI.Filter;

public sealed class FilterDraft
{
    public HighlightColor Color { get; set; } = HighlightColor.None;

    public FilterConditionDraft Comparison { get; set; } = new();

    public string ComparisonText { get; set; } = string.Empty;

    public FilterId Id { get; init; } = FilterId.Create();

    /// <summary>
    ///     Marks this draft as a Basic-row edit (structured Property/Operator/Value editor). Defaults to <c>false</c>
    ///     (Advanced — free-form expression text). The flag drives only the editor surface; the saved filter is
    ///     identified post-L1 by the presence of <see cref="SavedFilter.BasicFilter" />.
    /// </summary>
    public bool IsBasic { get; set; }

    public bool IsEnabled { get; set; }

    public bool IsExcluded { get; set; }

    public List<SubFilterDraft> SubFilters { get; set; } = [];

    /// <summary>
    ///     Hydrates Basic structure from <see cref="SavedFilter.BasicFilter" /> when present so Basic re-edit reopens
    ///     with the original comparison + sub-filters; otherwise opens empty Basic fields with just
    ///     <see cref="SavedFilter.ComparisonText" /> populated.
    /// </summary>
    public static FilterDraft FromSavedFilter(SavedFilter filter)
    {
        var basicFilter = filter.BasicFilter;

        return new FilterDraft
        {
            Id = filter.Id,
            Color = filter.Color,
            ComparisonText = filter.ComparisonText,
            IsBasic = basicFilter is not null,
            IsEnabled = filter.IsEnabled,
            IsExcluded = filter.IsExcluded,
            Comparison = basicFilter is null
                ? new FilterConditionDraft()
                : FilterConditionDraft.FromCondition(basicFilter.Comparison),
            SubFilters = basicFilter is null
                ? []
                : [.. basicFilter.SubFilters.Select(SubFilterDraftFromSubFilter)]
        };
    }

    public BasicFilter ToBasicFilter() =>
        new(Comparison.ToCondition(), [.. SubFilters.Select(subFilter => subFilter.ToSubFilter())]);

    private static SubFilterDraft SubFilterDraftFromSubFilter(SubFilter subFilter) =>
        new()
        {
            Condition = FilterConditionDraft.FromCondition(subFilter.Data),
            JoinWithAny = subFilter.JoinWithAny
        };
}
