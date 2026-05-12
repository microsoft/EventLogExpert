// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Filter;

namespace EventLogExpert.UI.FilterGroup;

public sealed record FilterGroupNode
{
    public Dictionary<string, FilterGroupNode> ChildNodes { get; init; } = [];

    public List<SavedFilterGroup> Groups { get; init; } = [];
}
