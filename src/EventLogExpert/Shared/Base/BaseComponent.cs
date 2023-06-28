// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Base;

public abstract class BaseComponent<T> : ComponentBase
{
    [Parameter]
    public string CssClass { get; set; } = string.Empty;

    [Parameter]
    public T Value { get; set; } = default!;

    [Parameter]
    public EventCallback<T> ValueChanged { get; set; }
}
