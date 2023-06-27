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

    private bool _isDropDownVisible;
    private ElementReference _selectComponent;
    private ValueSelectItem<T>? _selectedItem;

    [Parameter]
    public bool CanInput { get; set; } = false;

    [Parameter]
    public RenderFragment ChildContent { get; set; } = null!;

    private string IsDropDownVisible => _isDropDownVisible.ToString().ToLower();

    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;

    public void AddItem(ValueSelectItem<T>? item)
    {
        if (item is null) { return; }

        if (_items.Select(x => x.Value).Contains(item.Value)) { return; }

        _items.Add(item);

        if (Value?.Equals(item.Value) is true) { _selectedItem = item; }
    }

    public async Task UpdateValue(ValueSelectItem<T> item)
    {
        _selectedItem = item;
        Value = item.Value;
        await ValueChanged.InvokeAsync(Value);
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
                ToggleDropDownVisibility();
                break;
            case "ArrowUp" :
                await SelectAdjacentItem(-1);
                break;
            case "ArrowDown" :
                await SelectAdjacentItem(+1);
                break;
        }
    }

    private async Task SelectAdjacentItem(int direction)
    {
        var index = _items.FindIndex(x => x.ItemId == _selectedItem?.ItemId);

        index += direction;

        if (index < 0) { index = 0; }

        if (index >= _items.Count) { index = _items.Count - 1; }

        await UpdateValue(_items[index]);
    }

    private async void ToggleDropDownVisibility()
    {
        _isDropDownVisible = !_isDropDownVisible;
        await JSRuntime.InvokeVoidAsync("toggleDropdown", _selectComponent, _isDropDownVisible);
    }
}
