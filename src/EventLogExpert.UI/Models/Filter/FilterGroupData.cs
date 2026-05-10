// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Models;

public sealed record FilterGroupData
{
    public Dictionary<string, FilterGroupData> ChildGroup { get; init; } = [];

    public List<FilterGroupModel> FilterGroups { get; init; } = [];
}
