// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Shared.Base;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace EventLogExpert.Shared.Components;

public sealed partial class ValueSelect<T> : BaseComponent<T>
{
    private readonly string _itemId = $"select_{Guid.NewGuid().ToString()[..8]}";
    private readonly List<ValueSelectItem<T>> _items = [];
    private readonly HashSet<T> _selectedValues = [];

    private ValueSelectItem<T>? _highlightedItem;
    private bool _preventDefault;
    private ElementReference _selectComponent;

    [Parameter] public string? AriaLabel { get; set; }

    [Parameter] public string? AriaLabelledBy { get; set; }

    [Parameter]
    public RenderFragment ChildContent { get; set; } = null!;

    public ValueSelectItem<T>? HighlightedItem
    {
        get => _highlightedItem;
        set
        {
            _highlightedItem = value;
            StateHasChanged();
        }
    }

    [Parameter]
    public bool IsInput { get; set; }

    [Parameter]
    public bool IsMultiSelect { get; set; }

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
        if (_items.Contains(item))
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

    public async Task OpenDropDown() => await JSRuntime.InvokeVoidAsync("openDropdown", _selectComponent);

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

                return;
            case "ArrowUp":
                _preventDefault = true;

                await OpenDropDown();
                await SelectAdjacentItem(-1);

                return;
            case "ArrowDown":
                _preventDefault = true;

                await OpenDropDown();
                await SelectAdjacentItem(+1);

                return;
            case "Enter":
                if ((IsInput || IsMultiSelect) && HighlightedItem is not null)
                {
                    if (HighlightedItem.ClearItem)
                    {
                        ClearSelected();
                    }

                    await UpdateValue(HighlightedItem.Value);
                }
                else
                {
                    await CloseDropDown();
                }

                return;
            case "Escape":
                await CloseDropDown();

                return;
        }

        _preventDefault = false;
    }

    private async void OnInputChange(ChangeEventArgs args)
    {
        Value = (T)Convert.ChangeType(args.Value, typeof(T))!;
        await ValueChanged.InvokeAsync(Value);
    }

    private async Task SelectAdjacentItem(int direction)
    {
        int index;

        if (IsMultiSelect || IsInput)
        {
            index = _items.FindIndex(x => x.Equals(_items.FirstOrDefault(item => item.Equals(HighlightedItem))));
        }
        else
        {
            index = _items.FindIndex(x => x.Equals(
                _items.FirstOrDefault(item => item.Value?.Equals(_selectedValues.FirstOrDefault()) is true)));
        }

        // Need to account for first item being an empty placeholder
        if (index < 0) { index = 0; }

        for (int i = 0; i < _items.Count; i++)
        {
            index += direction;

            if (index < 0) { index = 0; }

            if (index >= _items.Count) { index = _items.Count - 1; }

            if (_items[index].IsDisabled) { continue; }

            if (IsMultiSelect || IsInput)
            {
                HighlightedItem = _items[index];

                StateHasChanged();

                await JSRuntime.InvokeVoidAsync("scrollToHighlightedItem", _selectComponent);
            }
            else
            {
                _selectedValues.Clear();
                _selectedValues.Add(_items[index].Value);

                await UpdateValue(_items[index].Value);

                await JSRuntime.InvokeVoidAsync("scrollToSelectedItem", _selectComponent);
            }

            return;
        }
    }

    private async void ToggleDropDownVisibility() =>
        await JSRuntime.InvokeVoidAsync("toggleDropdown", _selectComponent);
}
