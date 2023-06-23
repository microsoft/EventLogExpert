// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.UI.Store.FilterCache;

public class FilterCacheReducers
{
    private const int MaxRecentFilterCount = 20;

    [ReducerMethod]
    public static FilterCacheState ReduceAddRecentFilter(FilterCacheState state,
        FilterCacheAction.AddRecentFilter action)
    {
        if (state.RecentFilters.Any(filter =>
            string.Equals(filter.ComparisonString, action.Filter.ComparisonString, StringComparison.OrdinalIgnoreCase)))
        {
            return state;
        }

        if (state.RecentFilters.Count() >= MaxRecentFilterCount)
        {
            return state with { RecentFilters = state.RecentFilters.Dequeue().Enqueue(action.Filter) };
        }

        return state with { RecentFilters = state.RecentFilters.Enqueue(action.Filter) };
    }

    [ReducerMethod]
    public static FilterCacheState ReduceAddFavoriteFilter(FilterCacheState state,
        FilterCacheAction.AddFavoriteFilter action)
    {
        if (state.FavoriteFilters.Contains(action.Filter)) { return state; }

        return state with { FavoriteFilters = state.FavoriteFilters.Add(action.Filter) };
    }

    [ReducerMethod]
    public static FilterCacheState ReduceRemoveFavoriteFilter(FilterCacheState state,
        FilterCacheAction.RemoveFavoriteFilter action)
    {
        if (!state.FavoriteFilters.Contains(action.Filter)) { return state; }

        if (state.RecentFilters.Any(filter =>
            string.Equals(filter.ComparisonString,
                action.Filter.ComparisonString,
                StringComparison.OrdinalIgnoreCase)))
        {
            return state with { FavoriteFilters = state.FavoriteFilters.Remove(action.Filter) };
        }

        if (state.RecentFilters.Count() >= MaxRecentFilterCount)
        {
            return state with
            {
                FavoriteFilters = state.FavoriteFilters.Remove(action.Filter),
                RecentFilters = state.RecentFilters.Dequeue().Enqueue(action.Filter)
            };
        }

        return state with
        {
            FavoriteFilters = state.FavoriteFilters.Remove(action.Filter),
            RecentFilters = state.RecentFilters.Enqueue(action.Filter)
        };
    }
}
