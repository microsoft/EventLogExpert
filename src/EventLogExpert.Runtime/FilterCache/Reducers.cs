// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.Runtime.FilterCache;

internal sealed class Reducers
{
    [ReducerMethod]
    public static FilterCacheState ReduceAddFavoriteFilterSuccess(FilterCacheState state,
        AddFavoriteFilterSuccessAction action) => state with { FavoriteFilters = action.Filters };

    [ReducerMethod]
    public static FilterCacheState ReduceAddRecentFilterSuccess(FilterCacheState state,
        AddRecentFilterSuccessAction action) => state with { RecentFilters = action.Filters };

    [ReducerMethod]
    public static FilterCacheState ReduceLoadFiltersSuccess(FilterCacheState state,
        LoadFiltersSuccessAction action) => state with
        {
            FavoriteFilters = action.FavoriteFilters,
            RecentFilters = action.RecentFilters
        };

    [ReducerMethod]
    public static FilterCacheState ReduceRemoveFavoriteFilterSuccess(FilterCacheState state,
        RemoveFavoriteFilterSuccessAction action) => state with
        {
            FavoriteFilters = action.FavoriteFilters,
            RecentFilters = action.RecentFilters
        };
}
