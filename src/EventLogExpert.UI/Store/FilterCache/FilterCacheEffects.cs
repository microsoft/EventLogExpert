// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.FilterCache;

public sealed class FilterCacheEffects(IPreferencesProvider preferencesProvider, IState<FilterCacheState> state)
{
    private const int MaxRecentFilterCount = 20;

    [EffectMethod]
    public Task HandleAddFavoriteFilter(FilterCacheAction.AddFavoriteFilter action, IDispatcher dispatcher)
    {
        if (state.Value.FavoriteFilters.Contains(action.Filter)) { return Task.CompletedTask; }

        var newFilters = state.Value.FavoriteFilters.Add(action.Filter);

        preferencesProvider.FavoriteFiltersPreference = newFilters.Select(filter => filter.ComparisonString).ToList();

        dispatcher.Dispatch(new FilterCacheAction.AddFavoriteFilterCompleted(newFilters));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleAddRecentFilter(FilterCacheAction.AddRecentFilter action, IDispatcher dispatcher)
    {
        if (state.Value.RecentFilters.Any(filter =>
            string.Equals(filter.ComparisonString, action.Filter.ComparisonString, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.CompletedTask;
        }

        ImmutableQueue<AdvancedFilterModel> newFilters = state.Value.RecentFilters.Count() >= MaxRecentFilterCount
            ? state.Value.RecentFilters.Dequeue().Enqueue(action.Filter)
            : state.Value.RecentFilters.Enqueue(action.Filter);

        preferencesProvider.RecentFiltersPreference = newFilters.Select(filter => filter.ComparisonString).ToList();

        dispatcher.Dispatch(new FilterCacheAction.AddRecentFilterCompleted(newFilters));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleImportFavorites(FilterCacheAction.ImportFavorites action, IDispatcher dispatcher)
    {
        List<AdvancedFilterModel> newFilters = state.Value.FavoriteFilters.ToList();

        foreach (AdvancedFilterModel filter in
            action.Filters.Where(filter => !newFilters.Any(x => filter.ComparisonString.Equals(x.ComparisonString))))
        {
            newFilters.Add(filter);
        }

        preferencesProvider.FavoriteFiltersPreference = newFilters.Select(filter => filter.ComparisonString).ToList();

        dispatcher.Dispatch(new FilterCacheAction.AddFavoriteFilterCompleted([.. newFilters]));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleLoadFilters(FilterCacheAction.LoadFilters action, IDispatcher dispatcher)
    {
        var favoritesPreference = preferencesProvider.FavoriteFiltersPreference;
        var recentPreference = preferencesProvider.RecentFiltersPreference;

        List<AdvancedFilterModel> favorites = [];
        List<AdvancedFilterModel> recent = [];

        foreach (var filter in favoritesPreference)
        {
            favorites.Add(new AdvancedFilterModel { ComparisonString = filter });
        }

        foreach (var filter in recentPreference)
        {
            recent.Add(new AdvancedFilterModel { ComparisonString = filter });
        }

        dispatcher.Dispatch(
            new FilterCacheAction.LoadFiltersCompleted([.. favorites], ImmutableQueue.CreateRange(recent)));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleRemoveFavoriteFilter(FilterCacheAction.RemoveFavoriteFilter action, IDispatcher dispatcher)
    {
        if (!state.Value.FavoriteFilters.Contains(action.Filter)) { return Task.CompletedTask; }

        ImmutableList<AdvancedFilterModel> favorites;
        ImmutableQueue<AdvancedFilterModel> recent;

        if (state.Value.RecentFilters.Any(filter =>
            string.Equals(filter.ComparisonString,
                action.Filter.ComparisonString,
                StringComparison.OrdinalIgnoreCase)))
        {
            favorites = state.Value.FavoriteFilters.Remove(action.Filter);
            recent = state.Value.RecentFilters;
        }
        else if (state.Value.RecentFilters.Count() >= MaxRecentFilterCount)
        {
            favorites = state.Value.FavoriteFilters.Remove(action.Filter);
            recent = state.Value.RecentFilters.Dequeue().Enqueue(action.Filter);
        }
        else
        {
            favorites = state.Value.FavoriteFilters.Remove(action.Filter);
            recent = state.Value.RecentFilters.Enqueue(action.Filter);
        }

        preferencesProvider.FavoriteFiltersPreference = favorites.Select(filter => filter.ComparisonString).ToList();
        preferencesProvider.RecentFiltersPreference = recent.Select(filter => filter.ComparisonString).ToList();

        dispatcher.Dispatch(new FilterCacheAction.RemoveFavoriteFilterCompleted(favorites, recent));

        return Task.CompletedTask;
    }
}
