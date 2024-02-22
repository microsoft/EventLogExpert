// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace EventLogExpert.UI.Models;

public sealed record FilterModel
{
    [JsonIgnore]
    public FilterId Id { get; } = FilterId.Create();

    public HighlightColor Color { get; set; } = HighlightColor.None;

    public FilterComparison Comparison { get; set; } = new();

    [JsonIgnore]
    public FilterData Data { get; set; } = new();

    [JsonIgnore]
    public List<FilterModel> SubFilters { get; set; } = [];

    [JsonIgnore]
    public bool ShouldCompareAny { get; set; }

    [JsonIgnore]
    public bool IsEditing { get; set; }

    [JsonIgnore]
    public bool IsEnabled { get; set; }
}
