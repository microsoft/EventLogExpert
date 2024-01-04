// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor.Blazor.Web.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EventLogExpert.Shared.Base;

public abstract class BaseModal : FluxorComponent
{
    protected ElementReference ElementReference { get; set; }

    [Inject] protected IJSRuntime JSRuntime { get; init; } = null!;

    protected async Task Close() => await JSRuntime.InvokeVoidAsync("closeModal", ElementReference);

    protected async Task Open() => await JSRuntime.InvokeVoidAsync("openModal", ElementReference);
}
