// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterGroup;

public sealed record FilterGroupNode
{
    public ImmutableDictionary<string, FilterGroupNode> ChildNodes { get; init; } =
        ImmutableDictionary<string, FilterGroupNode>.Empty;

    public ImmutableList<SavedFilterGroup> Groups { get; init; } = ImmutableList<SavedFilterGroup>.Empty;
}
