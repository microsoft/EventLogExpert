// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EventLogExpert.Shared.Base;

public abstract class SelectComponent<T> : BaseComponent<T>
{
    protected ElementReference selectComponent;

    private Func<T?, string?> _toStringFunc = x => x?.ToString();

    private protected bool isDropDownVisible;

    public DisplayConverter<T?, string?>? DisplayConverter { get; private set; }

    [Parameter]
    public Func<T?, string?> ToStringFunc
    {
        get => _toStringFunc;
        set
        {
            if (_toStringFunc.Equals(value)) { return; }

            _toStringFunc = value;

            DisplayConverter = new DisplayConverter<T?, string?> { SetFunc = _toStringFunc };
        }
    }

    protected string? DisplayString
    {
        get
        {
            var converter = DisplayConverter;
            return converter is null ? $"{Value}" : converter.Set(Value);
        }
    }

    protected string IsDropDownVisible => isDropDownVisible.ToString().ToLower();

    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;

    protected async void CloseDropDown()
    {
        isDropDownVisible = false;
        await JSRuntime.InvokeVoidAsync("toggleDropdown", selectComponent, isDropDownVisible);
    }

    protected virtual async void ToggleDropDownVisibility()
    {
        isDropDownVisible = !isDropDownVisible;
        await JSRuntime.InvokeVoidAsync("toggleDropdown", selectComponent, isDropDownVisible);
    }
}
