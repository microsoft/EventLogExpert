// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Filter;

public sealed record FilterGroupNode
{
    public Dictionary<string, FilterGroupNode> ChildNodes { get; init; } = [];

    public List<SavedFilterGroup> FilterGroups { get; init; } = [];
}
