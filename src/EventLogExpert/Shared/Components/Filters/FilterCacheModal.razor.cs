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

    private void AddFilter(FilterCacheModel filter) =>
        Dispatcher.Dispatch(new FilterPaneAction.AddCachedFilter(filter));

    private async void Close() => await JSRuntime.InvokeVoidAsync("closeFilterCacheModal");

    private void RemoveRecent(FilterCacheModel filter) =>
        Dispatcher.Dispatch(new FilterCacheAction.RemoveRecentFilter(filter));

    private void ToggleFavorite(FilterCacheModel filter) =>
        Dispatcher.Dispatch(new FilterCacheAction.ToggleFavoriteFilter(filter));
}
