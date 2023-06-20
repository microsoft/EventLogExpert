// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EventLogExpert.Shared.Components.Filters;

public partial class FilterCacheModal
{
    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;

    private async void Close() => await JSRuntime.InvokeVoidAsync("closeFilterCacheModal");
}
