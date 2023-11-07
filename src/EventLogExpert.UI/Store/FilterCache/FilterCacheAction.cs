// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.FilterCache;

public record FilterCacheAction
{
    public record AddRecentFilter(AdvancedFilterModel Filter);

    public record AddRecentFilterCompleted(ImmutableQueue<AdvancedFilterModel> Filters);

    public record AddFavoriteFilter(AdvancedFilterModel Filter);

    public record AddFavoriteFilterCompleted(ImmutableList<AdvancedFilterModel> Filters);

    public record RemoveFavoriteFilter(AdvancedFilterModel Filter);

    public record RemoveFavoriteFilterCompleted(ImmutableList<AdvancedFilterModel> FavoriteFilters,
        ImmutableQueue<AdvancedFilterModel> RecentFilters);

    public record LoadFilters;

    public record LoadFiltersCompleted(ImmutableList<AdvancedFilterModel> FavoriteFilters,
        ImmutableQueue<AdvancedFilterModel> RecentFilters);

    public record ImportFavorites(List<AdvancedFilterModel> Filters);

    public record OpenMenu;
}
