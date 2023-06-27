// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.FilterCache;
using EventLogExpert.UI.Store.FilterPane;
using Fluxor;
using Microsoft.AspNetCore.Components;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components.Filters;

public partial class FilterCacheRow
{
    private CacheType _cacheType = CacheType.Favorites;
    private FilterCacheModel _filter = null!;
    private bool _isEditing;

    [Parameter] public FilterCacheModel Value { get; set; } = null!;

    [Inject] private IDispatcher Dispatcher { get; set; } = null!;

    [Inject] private IState<FilterCacheState> FilterCacheState { get; set; } = null!;

    private void EditFilter()
    {
        _isEditing = true;
        _filter = Value;
    }

    private void RemoveFilter()
    {
        // TODO: This is bugged and will not delete the cache entry unless the Value is in the filter list
        _isEditing = false;

        Dispatcher.Dispatch(new FilterPaneAction.RemoveCachedFilter(Value));
    }

    private void SaveFilter()
    {
        _isEditing = false;

        if (ReferenceEquals(Value, _filter)) { return; }

        Dispatcher.Dispatch(new FilterPaneAction.RemoveCachedFilter(_filter));
        Dispatcher.Dispatch(new FilterPaneAction.AddCachedFilter(Value));
    }

    private void ToggleFilter() => Dispatcher.Dispatch(new FilterPaneAction.ToggleCachedFilter(Value));
}
