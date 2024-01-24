// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace EventLogExpert.UI.Models;

public sealed record FilterModel
{
    [JsonIgnore]
    public Guid Id { get; } = Guid.NewGuid();

    public FilterColor Color { get; set; } = FilterColor.None;

    public FilterComparison Comparison { get; set; } = new();

    [JsonIgnore]
    public FilterData Data { get; set; } = new();

    public List<FilterModel> SubFilters { get; set; } = [];

    public bool ShouldCompareAny { get; set; }

    [JsonIgnore]
    public bool IsEditing { get; set; }

    [JsonIgnore]
    public bool IsEnabled { get; set; }
}
