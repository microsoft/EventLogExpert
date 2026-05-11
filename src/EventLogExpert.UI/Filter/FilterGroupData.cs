// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Filter;

public sealed record FilterGroupData
{
    public Dictionary<string, FilterGroupData> ChildGroup { get; init; } = [];

    public List<SavedFilterGroup> FilterGroups { get; init; } = [];
}
