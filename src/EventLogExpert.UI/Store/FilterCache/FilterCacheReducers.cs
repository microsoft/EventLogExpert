// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.FilterCache;

public class FilterCacheReducers
{
    [ReducerMethod]
    public static FilterCacheState ReduceAddRecentFilter(FilterCacheState state,
        FilterCacheAction.AddRecentFilter action)
    {
        if (state.Filters.Any(filter =>
            string.Equals(filter.ComparisonString, action.Filter.ComparisonString, StringComparison.OrdinalIgnoreCase)))
        {
            return state;
        }

        return state with
        {
            Filters = state.Filters.Add(action.Filter).OrderByDescending(x => x.IsFavorite).ToImmutableList()
        };
    }

    [ReducerMethod]
    public static FilterCacheState ReduceRemoveRecentFilter(FilterCacheState state,
        FilterCacheAction.RemoveRecentFilter action)
    {
        if (!state.Filters.Any(filter =>
            string.Equals(filter.ComparisonString, action.Filter.ComparisonString, StringComparison.OrdinalIgnoreCase)))
        {
            return state;
        }

        return state with
        {
            Filters = state.Filters.Remove(action.Filter).OrderByDescending(x => x.IsFavorite).ToImmutableList()
        };
    }

    [ReducerMethod]
    public static FilterCacheState ReduceToggleFavoriteFilter(FilterCacheState state,
        FilterCacheAction.ToggleFavoriteFilter action)
    {
        if (!state.Filters.Contains(action.Filter)) { return state; }

        return state with
        {
            Filters = state.Filters
                .Remove(action.Filter)
                .Add(action.Filter with { IsFavorite = !action.Filter.IsFavorite })
                .OrderByDescending(x => x.IsFavorite).ToImmutableList()
        };
    }
}
