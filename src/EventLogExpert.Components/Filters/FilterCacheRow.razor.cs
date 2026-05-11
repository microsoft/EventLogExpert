// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Components.Filters.Base;
using EventLogExpert.UI.Filter;
using EventLogExpert.UI.FilterCache;
using EventLogExpert.UI.FilterPane;
using Fluxor;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Components.Filters;

public sealed partial class FilterCacheRow : EditableFilterRowBase
{
    private CacheType _cacheType = CacheType.Favorites;

    [Inject] private IState<FilterCacheState> FilterCacheState { get; init; } = null!;

    private List<string> Items =>
        _cacheType switch
        {
            CacheType.Favorites => [.. FilterCacheState.Value.FavoriteFilters],
            CacheType.Recent => [.. FilterCacheState.Value.RecentFilters],
            _ => [],
        };

    protected override void DispatchRemoveFilter()
    {
        if (Value is not { } savedFilter) { return; }

        Dispatcher.Dispatch(new RemoveFilterAction(savedFilter.Id));
    }

    protected override void DispatchSetFilter(SavedFilter filter) =>
        Dispatcher.Dispatch(new SetFilterAction(filter));

    protected override void DispatchToggleEnabled(FilterId id) =>
        Dispatcher.Dispatch(new ToggleFilterEnabledAction(id));

    protected override void DispatchToggleExclusion(FilterId id) =>
        Dispatcher.Dispatch(new ToggleFilterExcludedAction(id));
}
