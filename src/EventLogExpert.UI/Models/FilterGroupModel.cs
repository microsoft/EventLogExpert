// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace EventLogExpert.UI.Models;

public sealed record FilterGroupModel
{
    [JsonIgnore]
    public Guid Id { get; } = Guid.NewGuid();

    public string Name { get; set; } = "New Filter Group";

    public string DisplayName => Name.Split('\\').Last();

    public IEnumerable<FilterModel> Filters { get; set; } = [];

    [JsonIgnore]
    public bool IsEditing { get; set; }
}
