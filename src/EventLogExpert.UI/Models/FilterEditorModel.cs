// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Models;

public sealed class FilterEditorModel
{
    public HighlightColor Color { get; set; } = HighlightColor.None;

    public string ComparisonText { get; set; } = string.Empty;

    public FilterType FilterType { get; set; } = FilterType.Advanced;

    public FilterId Id { get; init; } = FilterId.Create();

    public bool IsEnabled { get; set; }

    public bool IsExcluded { get; set; }

    public BasicFilterCriteriaDraft Main { get; set; } = new();

    /// <summary>
    ///     Sub-clauses are joined to <see cref="Main" /> via <see cref="BasicSubClauseDraft.JoinWithAny" />
    ///     (OR when true, AND when false).
    /// </summary>
    public List<BasicSubClauseDraft> SubClauses { get; set; } = [];

    /// <summary>
    ///     Hydrates Basic structure from <see cref="FilterModel.BasicSource" /> when present so Basic re-edit
    ///     reopens with the original main + sub-clauses; otherwise opens empty Basic fields with just
    ///     <see cref="FilterModel.ComparisonText" /> populated.
    /// </summary>
    public static FilterEditorModel FromFilterModel(FilterModel filter)
    {
        var basicSource = filter.FilterType == FilterType.Basic ? filter.BasicSource : null;

        return new FilterEditorModel
        {
            Id = filter.Id,
            Color = filter.Color,
            ComparisonText = filter.ComparisonText,
            FilterType = filter.FilterType,
            IsEnabled = filter.IsEnabled,
            IsExcluded = filter.IsExcluded,
            Main = basicSource is null
                ? new BasicFilterCriteriaDraft()
                : BasicFilterCriteriaDraft.FromCriteria(basicSource.Main),
            SubClauses = basicSource is null
                ? []
                : [.. basicSource.SubClauses.Select(SubClauseDraftFromSubClause)]
        };
    }

    public BasicFilterSource ToBasicSource() =>
        new(Main.ToCriteria(), [.. SubClauses.Select(subClause => subClause.ToSubClause())]);

    private static BasicSubClauseDraft SubClauseDraftFromSubClause(BasicSubClause subClause) =>
        new()
        {
            Criteria = BasicFilterCriteriaDraft.FromCriteria(subClause.Criteria),
            JoinWithAny = subClause.JoinWithAny
        };
}
