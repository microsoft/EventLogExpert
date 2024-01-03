// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.FilterCache;

[FeatureState]
public sealed record FilterCacheState
{
    public ImmutableList<string> FavoriteFilters { get; init; } = [];

    public ImmutableQueue<string> RecentFilters { get; init; } = [];
}
