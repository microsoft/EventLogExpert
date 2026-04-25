// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.UI.Models;

public sealed class FilterEditorModel
{
    public HighlightColor Color { get; set; } = HighlightColor.None;

    public string ComparisonText { get; set; } = string.Empty;

    public FilterType FilterType { get; set; } = FilterType.Advanced;

    public FilterId Id { get; init; } = FilterId.Create();

    public bool IsEnabled { get; set; }

    public bool IsExcluded { get; set; }

    /// <summary>The main Basic-filter clause being edited.</summary>
    public BasicFilterCriteriaDraft Main { get; set; } = new();

    /// <summary>
    ///     Flat list of additional sub-clauses joined to the main clause via
    ///     <see cref="BasicSubClauseDraft.JoinWithAny" /> (OR when true, AND when false). Production never produces nested
    ///     grandchildren; legacy deeper trees are intentionally flattened away on import.
    /// </summary>
    public List<BasicSubClauseDraft> SubClauses { get; set; } = [];

    /// <summary>Creates a draft editor from a saved filter.</summary>
    /// <remarks>
    ///     Lossy bridge: only the top level of <paramref name="filter" />.SubFilters is preserved (mapped to
    ///     <see cref="SubClauses" />). Grandchildren and per-sub-filter Color/IsEnabled/IsExcluded are discarded — production
    ///     never produces them.
    /// </remarks>
    public static FilterEditorModel FromFilterModel(FilterModel filter) =>
        new()
        {
            Id = filter.Id,
            Color = filter.Color,
            ComparisonText = filter.Comparison.Value,
            FilterType = filter.FilterType,
            IsEnabled = filter.IsEnabled,
            IsExcluded = filter.IsExcluded,
            Main = DraftFromFilterData(filter.Data),
            SubClauses = [.. filter.SubFilters.Select(SubClauseDraftFromFilterModel)]
        };

    /// <summary>Materializes the immutable Basic source for parse / compile.</summary>
    public BasicFilterSource ToBasicSource() =>
        new(Main.ToCriteria(), [.. SubClauses.Select(subClause => subClause.ToSubClause())]);

    /// <summary>
    ///     Materializes a saved <see cref="FilterModel" /> in the legacy shape so downstream consumers (reducers,
    ///     persistence, re-edit) can continue to round-trip the draft. Will be removed in 13d when callers migrate to
    ///     <see cref="ToBasicSource" /> + <c>FilterModel</c>'s new shape.
    /// </summary>
    public FilterModel ToFilterModel() =>
        new()
        {
            Id = Id,
            Color = Color,
            FilterType = FilterType,
            IsEnabled = IsEnabled,
            IsExcluded = IsExcluded,
            Data = FilterDataFromDraft(Main),
            SubFilters = [.. SubClauses.Select(SubFilterFromSubClauseDraft)],
            Comparison = string.IsNullOrEmpty(ComparisonText)
                ? new FilterComparison()
                : new FilterComparison { Value = ComparisonText }
        };

    private static BasicFilterCriteriaDraft DraftFromFilterData(FilterData data) =>
        new()
        {
            Category = data.Category,
            Evaluator = data.Evaluator,
            Value = data.Value,
            Values = [.. data.Values]
        };

    private static FilterData FilterDataFromDraft(BasicFilterCriteriaDraft draft)
    {
        // FilterData.Category setter clears Value/Values, so populate Category before Value/Values.
        var data = new FilterData
        {
            Category = draft.Category,
            Evaluator = draft.Evaluator,
            Value = draft.Value,
            Values = [.. draft.Values]
        };

        return data;
    }

    private static BasicSubClauseDraft SubClauseDraftFromFilterModel(FilterModel subFilter) =>
        new()
        {
            Id = subFilter.Id,
            Criteria = DraftFromFilterData(subFilter.Data),
            JoinWithAny = subFilter.ShouldCompareAny
        };

    private static FilterModel SubFilterFromSubClauseDraft(BasicSubClauseDraft subClause) =>
        new()
        {
            Id = subClause.Id,
            Data = FilterDataFromDraft(subClause.Criteria),
            ShouldCompareAny = subClause.JoinWithAny,
            SubFilters = ImmutableList<FilterModel>.Empty
        };
}
