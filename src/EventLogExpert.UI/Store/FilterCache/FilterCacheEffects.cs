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

        preferencesProvider.FavoriteFiltersPreference = newFilters;

        dispatcher.Dispatch(new FilterCacheAction.AddFavoriteFilterCompleted(newFilters));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleAddRecentFilter(FilterCacheAction.AddRecentFilter action, IDispatcher dispatcher)
    {
        if (state.Value.RecentFilters.Any(filter =>
            string.Equals(filter, action.Filter, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.CompletedTask;
        }

        ImmutableQueue<string> newFilters = state.Value.RecentFilters.Count() >= MaxRecentFilterCount
            ? state.Value.RecentFilters.Dequeue().Enqueue(action.Filter)
            : state.Value.RecentFilters.Enqueue(action.Filter);

        preferencesProvider.RecentFiltersPreference = newFilters.ToList();

        dispatcher.Dispatch(new FilterCacheAction.AddRecentFilterCompleted(newFilters));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleImportFavorites(FilterCacheAction.ImportFavorites action, IDispatcher dispatcher)
    {
        List<string> newFilters = [.. state.Value.FavoriteFilters];

        foreach (var filter in
            action.Filters.Where(filter =>
                !newFilters.Any(x => string.Equals(filter, x, StringComparison.OrdinalIgnoreCase))))
        {
            newFilters.Add(filter);
        }

        preferencesProvider.FavoriteFiltersPreference = newFilters;

        dispatcher.Dispatch(new FilterCacheAction.AddFavoriteFilterCompleted([.. newFilters]));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleLoadFilters(FilterCacheAction.LoadFilters action, IDispatcher dispatcher)
    {
        var favoritesPreference = preferencesProvider.FavoriteFiltersPreference;
        var recentPreference = preferencesProvider.RecentFiltersPreference;

        List<string> favorites = [];
        List<string> recent = [];

        foreach (var filter in favoritesPreference)
        {
            favorites.Add(filter);
        }

        foreach (var filter in recentPreference)
        {
            recent.Add(filter);
        }

        dispatcher.Dispatch(
            new FilterCacheAction.LoadFiltersCompleted([.. favorites], ImmutableQueue.CreateRange(recent)));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleRemoveFavoriteFilter(FilterCacheAction.RemoveFavoriteFilter action, IDispatcher dispatcher)
    {
        if (!state.Value.FavoriteFilters.Contains(action.Filter)) { return Task.CompletedTask; }

        ImmutableList<string> favorites;
        ImmutableQueue<string> recent;

        if (state.Value.RecentFilters.Any(filter =>
            string.Equals(filter, action.Filter, StringComparison.OrdinalIgnoreCase)))
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

        preferencesProvider.FavoriteFiltersPreference = favorites;
        preferencesProvider.RecentFiltersPreference = recent.ToList();

        dispatcher.Dispatch(new FilterCacheAction.RemoveFavoriteFilterCompleted(favorites, recent));

        return Task.CompletedTask;
    }
}
