// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Shared.Base;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace EventLogExpert.Shared.Components;

public partial class ValueSelect<T> : SelectComponent<T>
{
    private readonly List<ValueSelectItem<T>> _items = new();

    private ValueSelectItem<T>? _selectedItem;

    [Parameter]
    public RenderFragment ChildContent { get; set; } = null!;

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

    protected override async void ToggleDropDownVisibility()
    {
        isDropDownVisible = !isDropDownVisible;
        await JSRuntime.InvokeVoidAsync("toggleDropdown", selectComponent, isDropDownVisible);

        await ScrollToSelectedItem();
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
            case "Enter":
            case "Escape":
                CloseDropDown();
                break;
            default: 
                // TODO: Input Filtering will filter here
                // May also update this to debounce a quick SelectFirst() like a normal select box would work
                break;
        }
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

        if (isDropDownVisible) { await ScrollToSelectedItem(); }
    }
}
