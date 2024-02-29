// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Shared.Base;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace EventLogExpert.Shared.Components;

public sealed partial class ValueSelect<T> : BaseComponent<T>
{
    private readonly List<ValueSelectItem<T>> _items = [];
    private readonly HashSet<T> _selectedValues = [];

    private ElementReference _selectComponent;

    [Parameter]
    public RenderFragment ChildContent { get; set; } = null!;

    [Parameter]
    public bool IsInput { get; set; }

    [Parameter]
    public bool IsMultiSelect { get; set; } = false;

    private string? DisplayString
    {
        get
        {
            var converter = DisplayConverter;

            if (!IsMultiSelect)
            {
                return converter is null ? $"{Value}" : converter.Set(Value);
            }

            if (Values.Count <= 0) { return "Empty"; }

            return converter is null ?
                string.Join(", ", Values.Select(x => $"{x}")) :
                string.Join(", ", Values.Select(converter.Set));
        }
    }

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    public bool AddItem(ValueSelectItem<T> item)
    {
        if (_items.Select(x => x.Value).Contains(item.Value))
        {
            return _selectedValues.Contains(item.Value);
        }

        _items.Add(item);

        if (IsMultiSelect && Values.Contains(item.Value))
        {
            _selectedValues.Add(item.Value);
            return true;
        }

        if (Value?.Equals(item.Value) is not true) { return false; }

        _selectedValues.Clear();
        _selectedValues.Add(item.Value);

        return true;
    }

    public void ClearSelected() => _selectedValues.Clear();

    public async Task CloseDropDown() => await JSRuntime.InvokeVoidAsync("closeDropdown", _selectComponent);

    public void RemoveItem(ValueSelectItem<T> item) => _items.Remove(item);

    public async Task UpdateValue(T item)
    {
        if (IsMultiSelect)
        {
            if (item is null)
            {
                Values.Clear();
            }
            else if (_selectedValues.Remove(item))
            {
                Values.Remove(item);
            }
            else
            {
                _selectedValues.Add(item);
                Values.Add(item);
            }

            await ValuesChanged.InvokeAsync(Values);
        }
        else
        {
            _selectedValues.Clear();

            if (item is not null)
            {
                _selectedValues.Add(item);
            }

            Value = item;
            await ValueChanged.InvokeAsync(Value);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await JSRuntime.InvokeVoidAsync("registerDropdown", _selectComponent);
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    private async void HandleKeyDown(KeyboardEventArgs args)
    {
        switch (args.Code)
        {
            case "Space":
                if (!IsInput) { ToggleDropDownVisibility(); }

                break;
            case "ArrowUp":
                await SelectAdjacentItem(-1);

                break;
            case "ArrowDown":
                await SelectAdjacentItem(+1);

                break;
            case "Enter":
            case "Escape":
                await CloseDropDown();

                break;
        }
    }

    private async void OnInputChange(ChangeEventArgs args)
    {
        Value = (T)Convert.ChangeType(args.Value, typeof(T))!;
        await ValueChanged.InvokeAsync(Value);
    }

    private async Task SelectAdjacentItem(int direction)
    {
        // TODO: Should highlight next line but not change selection
        if (IsMultiSelect || IsInput) { return; }

        var index = _items.FindIndex(x => x.Equals(
            _items.FirstOrDefault(item => item.Value?.Equals(_selectedValues.FirstOrDefault()) is true)));

        if (direction < 0 && index < 0) { index = 0; }

        for (int i = 0; i < _items.Count; i++)
        {
            index += direction;

            if (index < 0) { index = 0; }

            if (index >= _items.Count) { index = _items.Count - 1; }

            if (_items[index].IsDisabled) { continue; }

            _selectedValues.Clear();
            _selectedValues.Add(_items[index].Value);
            await UpdateValue(_items[index].Value);

            await JSRuntime.InvokeVoidAsync("scrollToSelectedItem", _selectComponent);

            break;
        }
    }

    private async void ToggleDropDownVisibility() =>
        await JSRuntime.InvokeVoidAsync("toggleDropdown", _selectComponent);
}
