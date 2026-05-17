// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.FilterCache;

internal sealed class Effects(IFilterCachePreferencesProvider preferencesProvider, IState<FilterCacheState> state)
{
    private const int MaxRecentFilterCount = 20;

    [EffectMethod]
    public Task HandleAddFavoriteFilter(AddFavoriteFilterAction action, IDispatcher dispatcher)
    {
        if (state.Value.FavoriteFilters.Contains(action.Filter)) { return Task.CompletedTask; }

        var newFavorites = state.Value.FavoriteFilters.Add(action.Filter);

        // Recent and Favorite are mutually exclusive views of cached filter strings; promoting a
        // filter to Favorite removes it from Recent so it isn't shown twice in the picker.
        var newRecents = ImmutableQueue.CreateRange(
            state.Value.RecentFilters.Where(filter =>
                !string.Equals(filter, action.Filter, StringComparison.OrdinalIgnoreCase)));

        preferencesProvider.FavoriteFiltersPreference = newFavorites;
        preferencesProvider.RecentFiltersPreference = newRecents.ToList();

        dispatcher.Dispatch(new AddFavoriteFilterSuccessAction(newFavorites));

        if (newRecents.Count() != state.Value.RecentFilters.Count())
        {
            dispatcher.Dispatch(new AddRecentFilterSuccessAction(newRecents));
        }

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleAddRecentFilter(AddRecentFilterAction action, IDispatcher dispatcher)
    {
        if (string.IsNullOrWhiteSpace(action.Filter) ||
            state.Value.FavoriteFilters.Any(filter =>
                string.Equals(filter, action.Filter, StringComparison.OrdinalIgnoreCase)) ||
            state.Value.RecentFilters.Any(filter =>
                string.Equals(filter, action.Filter, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.CompletedTask;
        }

        ImmutableQueue<string> newFilters = state.Value.RecentFilters.Count() >= MaxRecentFilterCount
            ? state.Value.RecentFilters.Dequeue().Enqueue(action.Filter)
            : state.Value.RecentFilters.Enqueue(action.Filter);

        preferencesProvider.RecentFiltersPreference = newFilters.ToList();

        dispatcher.Dispatch(new AddRecentFilterSuccessAction(newFilters));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleImportFavorites(ImportFavoritesAction action, IDispatcher dispatcher)
    {
        HashSet<string> currentFilters = new(state.Value.FavoriteFilters, StringComparer.OrdinalIgnoreCase);
        List<string> newFilters = [.. state.Value.FavoriteFilters];

        foreach (var filter in action.Filters)
        {
            if (currentFilters.Add(filter))
            {
                newFilters.Add(filter);
            }
        }

        preferencesProvider.FavoriteFiltersPreference = newFilters;

        dispatcher.Dispatch(new AddFavoriteFilterSuccessAction([.. newFilters]));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleLoadFilters(LoadFiltersAction action, IDispatcher dispatcher)
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
            new LoadFiltersSuccessAction([.. favorites], ImmutableQueue.CreateRange(recent)));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleRemoveFavoriteFilter(RemoveFavoriteFilterAction action, IDispatcher dispatcher)
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

        dispatcher.Dispatch(new RemoveFavoriteFilterSuccessAction(favorites, recent));

        return Task.CompletedTask;
    }
}
