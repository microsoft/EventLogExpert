// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.UI.Store.FilterCache;

internal sealed class Reducers
{
    [ReducerMethod]
    public static FilterCacheState ReduceAddFavoriteFilterCompleted(FilterCacheState state,
        AddFavoriteFilterCompletedAction action) => state with { FavoriteFilters = action.Filters };

    [ReducerMethod]
    public static FilterCacheState ReduceAddRecentFilterCompleted(FilterCacheState state,
        AddRecentFilterCompletedAction action) => state with { RecentFilters = action.Filters };

    [ReducerMethod]
    public static FilterCacheState ReduceLoadFiltersCompleted(FilterCacheState state,
        LoadFiltersCompletedAction action) => state with
        {
            FavoriteFilters = action.FavoriteFilters,
            RecentFilters = action.RecentFilters
        };

    [ReducerMethod]
    public static FilterCacheState ReduceRemoveFavoriteFilterCompleted(FilterCacheState state,
        RemoveFavoriteFilterCompletedAction action) => state with
        {
            FavoriteFilters = action.FavoriteFilters,
            RecentFilters = action.RecentFilters
        };
}
