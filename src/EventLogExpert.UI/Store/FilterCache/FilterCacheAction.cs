// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.FilterCache;

public sealed record FilterCacheAction
{
    public sealed record AddFavoriteFilter(string Filter);

    public sealed record AddFavoriteFilterCompleted(ImmutableList<string> Filters);

    public sealed record AddRecentFilter(string Filter);

    public sealed record AddRecentFilterCompleted(ImmutableQueue<string> Filters);

    public sealed record ImportFavorites(List<string> Filters);

    public sealed record LoadFilters;

    public sealed record LoadFiltersCompleted(
        ImmutableList<string> FavoriteFilters,
        ImmutableQueue<string> RecentFilters);

    public sealed record OpenMenu;

    public sealed record RemoveFavoriteFilter(string Filter);

    public sealed record RemoveFavoriteFilterCompleted(
        ImmutableList<string> FavoriteFilters,
        ImmutableQueue<string> RecentFilters);
}
