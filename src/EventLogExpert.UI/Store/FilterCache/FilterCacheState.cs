// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.FilterCache;

[FeatureState]
public sealed record FilterCacheState
{
    public ImmutableList<FilterModel> FavoriteFilters { get; init; } = [];

    public ImmutableQueue<FilterModel> RecentFilters { get; init; } = [];
}
