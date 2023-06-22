// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor.Blazor.Web.Components;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Base;

public abstract class InputComponent<T> : ComponentBase
{
    [Parameter]
    public T Value { get; set; } = default!;

    [Parameter]
    public EventCallback<T> ValueChanged { get; set; }

    protected abstract Task UpdateValue(ChangeEventArgs args);
}
