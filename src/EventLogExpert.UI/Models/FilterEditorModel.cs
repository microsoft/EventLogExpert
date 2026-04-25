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

    /// <summary>The main Basic-filter clause being edited.</summary>
    public BasicFilterCriteriaDraft Main { get; set; } = new();

    /// <summary>
    ///     Flat list of additional sub-clauses joined to the main clause via
    ///     <see cref="BasicSubClauseDraft.JoinWithAny" /> (OR when true, AND when false).
    /// </summary>
    public List<BasicSubClauseDraft> SubClauses { get; set; } = [];

    /// <summary>
    ///     Creates a draft editor from a saved filter. When the saved filter retains its Basic
    ///     <see cref="FilterModel.BasicSource" />, the editor is hydrated from it so Basic re-edit reopens with the original
    ///     main + sub-clause structure. Otherwise (Advanced / Cached, or legacy persisted Basic filters that pre-date
    ///     BasicSource) the editor opens with empty Basic fields and the user sees just the comparison text.
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

    /// <summary>Materializes the immutable Basic source for parse / compile.</summary>
    public BasicFilterSource ToBasicSource() =>
        new(Main.ToCriteria(), [.. SubClauses.Select(subClause => subClause.ToSubClause())]);

    private static BasicSubClauseDraft SubClauseDraftFromSubClause(BasicSubClause subClause) =>
        new()
        {
            Criteria = BasicFilterCriteriaDraft.FromCriteria(subClause.Criteria),
            JoinWithAny = subClause.JoinWithAny
        };
}
