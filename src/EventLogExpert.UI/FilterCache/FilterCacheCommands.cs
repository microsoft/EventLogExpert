// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.UI.FilterCache;

internal sealed class FilterCacheCommands(IDispatcher dispatcher) : IFilterCacheCommands
{
    private readonly IDispatcher _dispatcher = dispatcher;

    public void AddFavoriteFilter(string filter) => _dispatcher.Dispatch(new AddFavoriteFilterAction(filter));

    public void ImportFavorites(IEnumerable<string> filters) =>
        _dispatcher.Dispatch(new ImportFavoritesAction([.. filters]));

    public void LoadFilters() => _dispatcher.Dispatch(new LoadFiltersAction());

    public void RemoveFavoriteFilter(string filter) => _dispatcher.Dispatch(new RemoveFavoriteFilterAction(filter));
}
