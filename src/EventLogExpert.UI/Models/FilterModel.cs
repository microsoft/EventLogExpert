// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace EventLogExpert.UI.Models;

public sealed record FilterModel
{
    [JsonIgnore]
    public FilterId Id { get; init; } = FilterId.Create();

    public HighlightColor Color { get; init; } = HighlightColor.None;

    public FilterComparison Comparison { get; init; } = new();

    [JsonIgnore]
    public FilterType FilterType { get; init; } = FilterType.Advanced;

    [JsonIgnore]
    public FilterData Data { get; init; } = new();

    [JsonIgnore]
    public ImmutableList<FilterModel> SubFilters { get; init; } = [];

    [JsonIgnore]
    public bool ShouldCompareAny { get; init; }

    [JsonIgnore]
    public bool IsEnabled { get; init; }

    public bool IsExcluded { get; init; }
}
