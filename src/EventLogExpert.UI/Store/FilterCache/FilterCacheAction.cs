// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.FilterCache;

public record FilterCacheAction
{
    public record AddRecentFilter(FilterCacheModel Filter);

    public record AddRecentFilterCompleted(ImmutableQueue<FilterCacheModel> Filters);

    public record AddFavoriteFilter(FilterCacheModel Filter);

    public record AddFavoriteFilterCompleted(ImmutableList<FilterCacheModel> Filters);

    public record RemoveFavoriteFilter(FilterCacheModel Filter);

    public record RemoveFavoriteFilterCompleted(ImmutableList<FilterCacheModel> FavoriteFilters,
        ImmutableQueue<FilterCacheModel> RecentFilters);

    public record LoadFilters;

    public record LoadFiltersCompleted(ImmutableList<FilterCacheModel> FavoriteFilters,
        ImmutableQueue<FilterCacheModel> RecentFilters);

    public record OpenMenu;
}
