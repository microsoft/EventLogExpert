// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EventLogExpert.Shared.Base;

public abstract class SelectComponent<T> : ComponentBase
{
    public ElementReference selectComponent;

    private bool _isDropDownVisible;

    [Parameter]
    public T Value { get; set; } = default!;

    [Parameter]
    public EventCallback<T> ValueChanged { get; set; }

    protected string IsDropDownVisible => _isDropDownVisible.ToString().ToLower();

    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;

    protected async void SetDropDownVisibility(bool visible)
    {
        _isDropDownVisible = visible;
        await JSRuntime.InvokeVoidAsync("toggleDropdown", selectComponent, _isDropDownVisible);
    }

    protected async Task UpdateValue(T value)
    {
        Value = value;
        await ValueChanged.InvokeAsync(Value);
    }
}
