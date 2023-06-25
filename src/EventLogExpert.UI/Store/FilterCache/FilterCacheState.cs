// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.FilterCache;

[FeatureState]
public record FilterCacheState
{
    public ImmutableList<FilterCacheModel> FavoriteFilters { get; init; } = ImmutableList<FilterCacheModel>.Empty;

    public ImmutableQueue<FilterCacheModel> RecentFilters { get; init; } = ImmutableQueue<FilterCacheModel>.Empty;
}
