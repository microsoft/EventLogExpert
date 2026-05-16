// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.FilterGroup;

[FeatureState]
public sealed record FilterGroupState
{
    public ImmutableList<SavedFilterGroup> Groups { get; init; } = [];

    public IReadOnlyDictionary<string, FilterGroupNode> DisplayGroups { get; init; } =
        ImmutableDictionary<string, FilterGroupNode>.Empty;
}
