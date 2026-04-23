// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace EventLogExpert.UI.Models;

public sealed record FilterModel
{
    [JsonIgnore]
    public FilterId Id { get; init; } = FilterId.Create();

    // NOTE: Color and ShouldCompareAny remain mutable until Step 3c migrates Razor components
    // (FilterRow / SubFilterRow / AdvancedFilterRow / FilterCacheRow / FilterGroupRow) to bind
    // FilterEditorModel instead of FilterModel directly. Razor @bind-Value requires a settable
    // property on the binding target, so flipping these to init now would break compilation of
    // the existing components.
    public HighlightColor Color { get; set; } = HighlightColor.None;

    public FilterComparison Comparison { get; init; } = new();

    [JsonIgnore]
    public FilterType FilterType { get; init; } = FilterType.Advanced;

    [JsonIgnore]
    public FilterData Data { get; init; } = new();

    [JsonIgnore]
    public ImmutableList<FilterModel> SubFilters { get; init; } = [];

    [JsonIgnore]
    public bool ShouldCompareAny { get; set; }

    [JsonIgnore]
    public bool IsEnabled { get; init; }

    public bool IsExcluded { get; init; }
}
