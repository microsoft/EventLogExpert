// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Base;

public abstract class SelectComponent<T> : ComponentBase
{
    private bool _isDropDownVisible;

    [Parameter]
    public T Value { get; set; } = default!;

    [Parameter]
    public EventCallback<T> ValueChanged { get; set; }

    protected string IsDropDownVisible => _isDropDownVisible.ToString().ToLower();

    protected void SetDropDownVisibility(bool visible) => _isDropDownVisible = visible;

    protected async Task UpdateValue(T value)
    {
        Value = value;
        await ValueChanged.InvokeAsync(Value);
    }
}
