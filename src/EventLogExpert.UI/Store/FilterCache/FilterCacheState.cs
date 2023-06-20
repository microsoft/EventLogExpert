// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.FilterCache;

[FeatureState]
public record FilterCacheState
{
    public ImmutableList<string> FavoriteFilters { get; set; } = ImmutableList<string>.Empty;

    public ImmutableList<string> RecentFilters { get; set; } = ImmutableList<string>.Empty;
}
