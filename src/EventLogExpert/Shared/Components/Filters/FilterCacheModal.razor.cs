// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Store.FilterCache;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components.Filters;

public partial class FilterCacheModal
{
    [Inject] private IDispatcher Dispatcher { get; set; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;

    private async void Close() => await JSRuntime.InvokeVoidAsync("closeFilterCacheModal");

    private void RemoveRecentFilter(string filter) =>
        Dispatcher.Dispatch(new FilterCacheAction.RemoveRecentFilter(filter));
}
