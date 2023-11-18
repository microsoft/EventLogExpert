// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.FilterCache;

[FeatureState]
public sealed record FilterCacheState
{
    public ImmutableList<AdvancedFilterModel> FavoriteFilters { get; init; } = [];

    public ImmutableQueue<AdvancedFilterModel> RecentFilters { get; init; } = [];
}
