// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.FilterCache;

public sealed record FilterCacheAction
{
    public sealed record AddRecentFilter(AdvancedFilterModel Filter);

    public sealed record AddRecentFilterCompleted(ImmutableQueue<AdvancedFilterModel> Filters);

    public sealed record AddFavoriteFilter(AdvancedFilterModel Filter);

    public sealed record AddFavoriteFilterCompleted(ImmutableList<AdvancedFilterModel> Filters);

    public sealed record RemoveFavoriteFilter(AdvancedFilterModel Filter);

    public sealed record RemoveFavoriteFilterCompleted(ImmutableList<AdvancedFilterModel> FavoriteFilters,
        ImmutableQueue<AdvancedFilterModel> RecentFilters);

    public sealed record LoadFilters;

    public sealed record LoadFiltersCompleted(ImmutableList<AdvancedFilterModel> FavoriteFilters,
        ImmutableQueue<AdvancedFilterModel> RecentFilters);

    public sealed record ImportFavorites(List<AdvancedFilterModel> Filters);

    public sealed record OpenMenu;
}
