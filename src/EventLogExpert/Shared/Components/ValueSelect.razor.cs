// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Shared.Base;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace EventLogExpert.Shared.Components;

public partial class ValueSelect<T> : BaseComponent<T>
{
    private readonly List<ValueSelectItem<T>> _items = new();
    private readonly HashSet<T> _selectedValues = new();

    private bool _isDropDownVisible;
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

            if (!Values.Any()) { return "Empty"; }

            return converter is null ?
                string.Join(", ", Values.Select(x => $"{x}")) :
                string.Join(", ", Values.Select(converter.Set));
        }
    }

    private string IsDropDownVisible => _isDropDownVisible.ToString().ToLower();

    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;

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

    public async void CloseDropDown()
    {
        _isDropDownVisible = false;
        await JSRuntime.InvokeVoidAsync("toggleDropdown", _selectComponent, _isDropDownVisible);
    }

    public void RemoveItem(ValueSelectItem<T> item) => _items.Remove(item);

    public async Task UpdateValue(T item)
    {
        if (IsMultiSelect)
        {
            if (item is null)
            {
                Values.Clear();
            }
            else if (_selectedValues.Contains(item))
            {
                _selectedValues.Remove(item);
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

    protected async void ToggleDropDownVisibility()
    {
        _isDropDownVisible = !_isDropDownVisible;
        await JSRuntime.InvokeVoidAsync("toggleDropdown", _selectComponent, _isDropDownVisible);

        await ScrollToSelectedItem();
    }

    private async void HandleKeyDown(KeyboardEventArgs args)
    {
        switch (args.Code)
        {
            case "Space" :
                if (!IsInput) { ToggleDropDownVisibility(); }

                break;
            case "ArrowUp" :
                if (!_isDropDownVisible) { ToggleDropDownVisibility(); }

                await SelectAdjacentItem(-1);
                break;
            case "ArrowDown" :
                if (!_isDropDownVisible) { ToggleDropDownVisibility(); }

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
        var item = _items.FirstOrDefault(item => item.Value?.Equals(_selectedValues.FirstOrDefault()) is true);

        if (item is null) { return; }

        await JSRuntime.InvokeVoidAsync("scrollToItem", item.ItemId);
    }

    private async Task SelectAdjacentItem(int direction)
    {
        // TODO: Should highlight next line but not change selection
        if (IsMultiSelect || IsInput) { return; }

        var index = _items.FindIndex(x => x.ItemId.Equals(
            _items.FirstOrDefault(item => item.Value?.Equals(_selectedValues.FirstOrDefault()) is true)?.ItemId));

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

            break;
        }

        if (_isDropDownVisible) { await ScrollToSelectedItem(); }
    }
}
