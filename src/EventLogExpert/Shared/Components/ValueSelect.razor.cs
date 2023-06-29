// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Shared.Base;
using EventLogExpert.UI.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace EventLogExpert.Shared.Components;

public partial class ValueSelect<T> : BaseComponent<T>
{
    private readonly List<ValueSelectItem<T>> _items = new();

    private bool _isDropDownVisible;
    private ElementReference _selectComponent;
    private ValueSelectItem<T>? _selectedItem;
    private Func<T?, string?> _toStringFunc = x => x?.ToString();

    [Parameter]
    public RenderFragment ChildContent { get; set; } = null!;

    public DisplayConverter<T?, string?>? DisplayConverter { get; private set; }

    [Parameter]
    public bool IsInput { get; set; } = false;

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

    private string? DisplayString
    {
        get
        {
            var converter = DisplayConverter;
            return converter is null ? $"{Value}" : converter.Set(Value);
        }
    }

    private string IsDropDownVisible => _isDropDownVisible.ToString().ToLower();

    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;

    public void AddItem(ValueSelectItem<T>? item)
    {
        if (item is null) { return; }

        if (_items.Select(x => x.Value).Contains(item.Value)) { return; }

        _items.Add(item);

        if (Value?.Equals(item.Value) is true) { _selectedItem = item; }
    }

    public void RemoveItem(ValueSelectItem<T> item) => _items.Remove(item);

    public async Task UpdateValue(ValueSelectItem<T> item)
    {
        _selectedItem = item;
        Value = item.Value;
        await ValueChanged.InvokeAsync(Value);
    }

    protected async void ToggleDropDownVisibility()
    {
        _isDropDownVisible = !_isDropDownVisible;
        await JSRuntime.InvokeVoidAsync("toggleDropdown", _selectComponent, _isDropDownVisible);

        await ScrollToSelectedItem();
    }

    private async void CloseDropDown()
    {
        _isDropDownVisible = false;
        await JSRuntime.InvokeVoidAsync("toggleDropdown", _selectComponent, _isDropDownVisible);
    }

    private async void HandleKeyDown(KeyboardEventArgs args)
    {
        switch (args.Code)
        {
            case "Space" :
                if (!IsInput) { ToggleDropDownVisibility(); }

                break;
            case "ArrowUp" :
                await SelectAdjacentItem(-1);
                break;
            case "ArrowDown" :
                await SelectAdjacentItem(+1);
                break;
            case "Enter" :
            case "Escape" :
                CloseDropDown();
                break;
        }
    }

    private async void OnInputChange(ChangeEventArgs args)
    {
        Value = (T)Convert.ChangeType(args.Value, typeof(T))!;
        await ValueChanged.InvokeAsync(Value);
    }

    private async Task ScrollToSelectedItem()
    {
        if (_selectedItem is not null)
        {
            await JSRuntime.InvokeVoidAsync("scrollToItem", _selectedItem.ItemId);
        }
    }

    private async Task SelectAdjacentItem(int direction)
    {
        var index = _items.FindIndex(x => x.ItemId == _selectedItem?.ItemId);

        if (direction < 0 && index < 0) { index = 0; }

        for (int i = 0; i < _items.Count; i++)
        {
            index += direction;

            if (index < 0) { index = 0; }

            if (index >= _items.Count) { index = _items.Count - 1; }

            if (_items[index].IsDisabled) { continue; }

            await UpdateValue(_items[index]);
            break;
        }

        if (_isDropDownVisible) { await ScrollToSelectedItem(); }
    }
}
