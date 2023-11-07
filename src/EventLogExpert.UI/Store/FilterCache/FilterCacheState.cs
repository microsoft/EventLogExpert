// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.FilterCache;

[FeatureState]
public record FilterCacheState
{
    public ImmutableList<AdvancedFilterModel> FavoriteFilters { get; init; } = ImmutableList<AdvancedFilterModel>.Empty;

    public ImmutableQueue<AdvancedFilterModel> RecentFilters { get; init; } = ImmutableQueue<AdvancedFilterModel>.Empty;
}
