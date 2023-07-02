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

    protected override void OnInitialized()
    {
        SubscribeToAction<FilterCacheAction.OpenMenu>(action => Open().AndForget());

        base.OnInitialized();
    }

    private void AddFavorite(FilterCacheModel filter) =>
        Dispatcher.Dispatch(new FilterCacheAction.AddFavoriteFilter(filter));

    private void AddFilter(FilterCacheModel filter)
    {
        Dispatcher.Dispatch(new FilterPaneAction.AddCachedFilter(filter));
        Close().AndForget();
    }

    private async Task Close() => await JSRuntime.InvokeVoidAsync("closeFilterCacheModal");

    private async Task Open() => await JSRuntime.InvokeVoidAsync("openFilterCacheModal");

    private void RemoveFavorite(FilterCacheModel filter) =>
        Dispatcher.Dispatch(new FilterCacheAction.RemoveFavoriteFilter(filter));
}
