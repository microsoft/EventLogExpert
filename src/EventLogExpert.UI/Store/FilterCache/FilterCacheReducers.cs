// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.UI.Store.FilterCache;

public sealed class FilterCacheReducers
{
    [ReducerMethod]
    public static FilterCacheState ReduceAddFavoriteFilterCompleted(FilterCacheState state,
        FilterCacheAction.AddFavoriteFilterCompleted action) => state with { FavoriteFilters = action.Filters };

    [ReducerMethod]
    public static FilterCacheState ReduceAddRecentFilterCompleted(FilterCacheState state,
        FilterCacheAction.AddRecentFilterCompleted action) => state with { RecentFilters = action.Filters };

    [ReducerMethod]
    public static FilterCacheState ReduceLoadFiltersCompleted(FilterCacheState state,
        FilterCacheAction.LoadFiltersCompleted action) => state with
    {
        FavoriteFilters = action.FavoriteFilters,
        RecentFilters = action.RecentFilters
    };

    [ReducerMethod]
    public static FilterCacheState ReduceRemoveFavoriteFilterCompleted(FilterCacheState state,
        FilterCacheAction.RemoveFavoriteFilterCompleted action) => state with
    {
        FavoriteFilters = action.FavoriteFilters,
        RecentFilters = action.RecentFilters
    };
}
