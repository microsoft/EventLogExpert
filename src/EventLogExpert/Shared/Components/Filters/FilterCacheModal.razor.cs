// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.FilterCache;
using EventLogExpert.UI.Store.FilterPane;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components.Filters;

public partial class FilterCacheModal
{
    [Inject] private IDispatcher Dispatcher { get; set; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;

    private void AddFavorite(FilterCacheModel filter) =>
        Dispatcher.Dispatch(new FilterCacheAction.AddFavoriteFilter(filter));

    private void AddFilter(FilterCacheModel filter)
    {
        Dispatcher.Dispatch(new FilterPaneAction.AddCachedFilter(filter));
        Close();
    }

    private async void Close() => await JSRuntime.InvokeVoidAsync("closeFilterCacheModal");

    private void RemoveFavorite(FilterCacheModel filter) =>
        Dispatcher.Dispatch(new FilterCacheAction.RemoveFavoriteFilter(filter));
}
