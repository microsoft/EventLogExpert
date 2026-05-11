// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace EventLogExpert.UI.Filter;

public sealed record SavedFilterGroup
{
    [JsonIgnore]
    public FilterGroupId Id { get; } = FilterGroupId.Create();

    public string Name { get; init; } = "New Filter Section\\New Filter Group";

    [JsonIgnore]
    public string DisplayName => Name.Split('\\').Last();

    public IReadOnlyList<SavedFilter> Filters { get; init; } = [];

    [JsonIgnore]
    public bool IsEditing { get; init; }
}
