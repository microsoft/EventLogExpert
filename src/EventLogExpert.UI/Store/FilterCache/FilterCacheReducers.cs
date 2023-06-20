// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.UI.Store.FilterCache;

public class FilterCacheReducers
{
    [ReducerMethod]
    public static FilterCacheState ReduceAddRecentFilter(FilterCacheState state,
        FilterCacheAction.AddRecentFilter action)
    {
        if (state.RecentFilters.Any(filter =>
            filter.Contains(action.ComparisonString, StringComparison.OrdinalIgnoreCase))) { return state; }

        return state with { RecentFilters = state.RecentFilters.Add(action.ComparisonString) };
    }

    [ReducerMethod]
    public static FilterCacheState ReduceRemoveRecentFilter(FilterCacheState state,
        FilterCacheAction.RemoveRecentFilter action)
    {
        if (!state.RecentFilters.Any(filter =>
            filter.Contains(action.ComparisonString, StringComparison.OrdinalIgnoreCase))) { return state; }

        return state with { RecentFilters = state.RecentFilters.Remove(action.ComparisonString) };
    }
}
