// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.FilterCache;

public class FilterCacheEffects
{
    private const int MaxRecentFilterCount = 20;

    private readonly IPreferencesProvider _preferencesProvider;
    private readonly IState<FilterCacheState> _state;

    public FilterCacheEffects(IPreferencesProvider preferencesProvider, IState<FilterCacheState> state)
    {
        _preferencesProvider = preferencesProvider;
        _state = state;
    }

    [EffectMethod]
    public async Task HandleAddFavoriteFilter(FilterCacheAction.AddFavoriteFilter action, IDispatcher dispatcher)
    {
        if (_state.Value.FavoriteFilters.Contains(action.Filter)) { return; }

        var newFilters = _state.Value.FavoriteFilters.Add(action.Filter);

        _preferencesProvider.FavoriteFiltersPreference = newFilters.Select(filter => filter.ComparisonString).ToList();

        dispatcher.Dispatch(new FilterCacheAction.AddFavoriteFilterCompleted(newFilters));
    }

    [EffectMethod]
    public async Task HandleAddRecentFilter(FilterCacheAction.AddRecentFilter action, IDispatcher dispatcher)
    {
        if (_state.Value.RecentFilters.Any(filter =>
            string.Equals(filter.ComparisonString, action.Filter.ComparisonString, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        ImmutableQueue<AdvancedFilterModel> newFilters = _state.Value.RecentFilters.Count() >= MaxRecentFilterCount
            ? _state.Value.RecentFilters.Dequeue().Enqueue(action.Filter)
            : _state.Value.RecentFilters.Enqueue(action.Filter);

        _preferencesProvider.RecentFiltersPreference = newFilters.Select(filter => filter.ComparisonString).ToList();

        dispatcher.Dispatch(new FilterCacheAction.AddRecentFilterCompleted(newFilters));
    }

    [EffectMethod]
    public async Task HandleImportFavorites(FilterCacheAction.ImportFavorites action, IDispatcher dispatcher)
    {
        List<AdvancedFilterModel> newFilters = _state.Value.FavoriteFilters.ToList();

        foreach (AdvancedFilterModel filter in
            action.Filters.Where(filter => !newFilters.Any(x => filter.ComparisonString.Equals(x.ComparisonString))))
        {
            newFilters.Add(filter);
        }

        _preferencesProvider.FavoriteFiltersPreference = newFilters.Select(filter => filter.ComparisonString).ToList();

        dispatcher.Dispatch(new FilterCacheAction.AddFavoriteFilterCompleted(newFilters.ToImmutableList()));
    }

    [EffectMethod]
    public async Task HandleLoadFilters(FilterCacheAction.LoadFilters action, IDispatcher dispatcher)
    {
        var favoritesPreference = _preferencesProvider.FavoriteFiltersPreference;
        var recentPreference = _preferencesProvider.RecentFiltersPreference;

        List<AdvancedFilterModel> favorites = new();
        List<AdvancedFilterModel> recent = new();

        foreach (var filter in favoritesPreference)
        {
            favorites.Add(new AdvancedFilterModel { ComparisonString = filter });
        }

        foreach (var filter in recentPreference)
        {
            recent.Add(new AdvancedFilterModel { ComparisonString = filter });
        }

        dispatcher.Dispatch(
            new FilterCacheAction.LoadFiltersCompleted(
                favorites.ToImmutableList(),
                ImmutableQueue.CreateRange(recent)));
    }

    [EffectMethod]
    public async Task HandleRemoveFavoriteFilter(FilterCacheAction.RemoveFavoriteFilter action, IDispatcher dispatcher)
    {
        if (!_state.Value.FavoriteFilters.Contains(action.Filter)) { return; }

        ImmutableList<AdvancedFilterModel> favorites;
        ImmutableQueue<AdvancedFilterModel> recent;

        if (_state.Value.RecentFilters.Any(filter =>
            string.Equals(filter.ComparisonString,
                action.Filter.ComparisonString,
                StringComparison.OrdinalIgnoreCase)))
        {
            favorites = _state.Value.FavoriteFilters.Remove(action.Filter);
            recent = _state.Value.RecentFilters;
        }
        else if (_state.Value.RecentFilters.Count() >= MaxRecentFilterCount)
        {
            favorites = _state.Value.FavoriteFilters.Remove(action.Filter);
            recent = _state.Value.RecentFilters.Dequeue().Enqueue(action.Filter);
        }
        else
        {
            favorites = _state.Value.FavoriteFilters.Remove(action.Filter);
            recent = _state.Value.RecentFilters.Enqueue(action.Filter);
        }

        _preferencesProvider.FavoriteFiltersPreference = favorites.Select(filter => filter.ComparisonString).ToList();
        _preferencesProvider.RecentFiltersPreference = recent.Select(filter => filter.ComparisonString).ToList();

        dispatcher.Dispatch(new FilterCacheAction.RemoveFavoriteFilterCompleted(favorites, recent));
    }
}
