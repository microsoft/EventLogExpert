// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.FilterCache;

public sealed record FilterCacheAction
{
    public sealed record AddFavoriteFilter(FilterModel Filter);

    public sealed record AddFavoriteFilterCompleted(ImmutableList<FilterModel> Filters);

    public sealed record AddRecentFilter(FilterModel Filter);

    public sealed record AddRecentFilterCompleted(ImmutableQueue<FilterModel> Filters);

    public sealed record ImportFavorites(List<FilterModel> Filters);

    public sealed record LoadFilters;

    public sealed record LoadFiltersCompleted(
        ImmutableList<FilterModel> FavoriteFilters,
        ImmutableQueue<FilterModel> RecentFilters);

    public sealed record OpenMenu;

    public sealed record RemoveFavoriteFilter(FilterModel Filter);

    public sealed record RemoveFavoriteFilterCompleted(
        ImmutableList<FilterModel> FavoriteFilters,
        ImmutableQueue<FilterModel> RecentFilters);
}
