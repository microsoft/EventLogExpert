// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EventLogExpert.Shared.Base;

public abstract class SelectComponent<T> : BaseComponent<T>
{
    protected ElementReference selectComponent;

    private bool _isDropDownVisible;

    protected string IsDropDownVisible => _isDropDownVisible.ToString().ToLower();

    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;

    protected bool CheckIfSelected(T value) => value is not null && value.Equals(Value);

    protected async void CloseDropDown()
    {
        _isDropDownVisible = false;
        await JSRuntime.InvokeVoidAsync("toggleDropdown", selectComponent, _isDropDownVisible);
    }

    protected async void ToggleDropDownVisibility()
    {
        _isDropDownVisible = !_isDropDownVisible;
        await JSRuntime.InvokeVoidAsync("toggleDropdown", selectComponent, _isDropDownVisible);
    }

    protected async Task UpdateValue(T value)
    {
        Value = value;
        await ValueChanged.InvokeAsync(Value);
    }
}
