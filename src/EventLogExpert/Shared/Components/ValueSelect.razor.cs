// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Shared.Base;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace EventLogExpert.Shared.Components;

public sealed partial class ValueSelect<T> : BaseComponent<T>, IAsyncDisposable
{
    private readonly string _itemId = $"select_{Guid.NewGuid().ToString()[..8]}";
    private readonly List<ValueSelectItem<T>> _items = [];
    private readonly HashSet<T> _selectedValues = [];

    private ValueSelectItem<T>? _highlightedItem;
    private bool _preventDefault;
    private ElementReference _selectComponent;

    [Parameter] public string? AriaLabel { get; set; }

    [Parameter] public string? AriaLabelledBy { get; set; }

    [Parameter] public string? DataHighlight { get; set; }

    [Parameter]
    public RenderFragment ChildContent { get; set; } = null!;

    public ValueSelectItem<T>? HighlightedItem
    {
        get => _highlightedItem;
        set
        {
            if (ReferenceEquals(_highlightedItem, value)) { return; }

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

    public void AddItem(ValueSelectItem<T> item)
    {
        if (!_items.Contains(item))
        {
            _items.Add(item);
        }
    }

    public async Task ClearAll()
    {
        _selectedValues.Clear();
        _highlightedItem = null;

        if (IsMultiSelect)
        {
            Values.Clear();
            await ValuesChanged.InvokeAsync(Values);
        }
        else
        {
            Value = default!;
            await ValueChanged.InvokeAsync(Value);
        }
    }

    public async Task CloseDropDown() => await JSRuntime.InvokeVoidAsync("closeDropdown", _selectComponent);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await JSRuntime.InvokeVoidAsync("unregisterDropdown", _selectComponent);
        }
        catch (JSDisconnectedException)
        {
            // Expected during app shutdown
        }
    }

    public bool IsItemSelected(T value) => _selectedValues.Contains(value);

    public async Task OpenDropDown() => await JSRuntime.InvokeVoidAsync("openDropdown", _selectComponent);

    public void RemoveItem(ValueSelectItem<T> item)
    {
        _items.Remove(item);

        if (ReferenceEquals(_highlightedItem, item))
        {
            HighlightedItem = null;
        }
    }

    public async Task UpdateValue(T item)
    {
        if (IsMultiSelect)
        {
            if (_selectedValues.Remove(item))
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

    protected override void OnParametersSet()
    {
        _selectedValues.Clear();

        if (IsMultiSelect)
        {
            foreach (var v in Values)
            {
                _selectedValues.Add(v);
            }
        }
        else if (Value is not null)
        {
            _selectedValues.Add(Value);
        }
    }

    private async Task HandleKeyDown(KeyboardEventArgs args)
    {
        _preventDefault = false;

        switch (args.Code)
        {
            case "Space":
                if (!IsInput) { await ToggleDropDownVisibility(); }

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
                        await ClearAll();
                    }
                    else
                    {
                        await UpdateValue(HighlightedItem.Value);
                    }
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
    }

    private async Task OnInputChange(ChangeEventArgs args)
    {
        if (BindConverter.TryConvertTo<T>($"{args.Value}", null, out var result))
        {
            Value = result;
            await ValueChanged.InvokeAsync(Value);
        }
    }

    private async Task SelectAdjacentItem(int direction)
    {
        int index;

        if (IsMultiSelect || IsInput)
        {
            index = HighlightedItem is not null ? _items.IndexOf(HighlightedItem) : -1;
        }
        else
        {
            index = _items.FindIndex(item => item.Value?.Equals(_selectedValues.FirstOrDefault()) is true);
        }

        if (index < 0)
        {
            index = direction > 0 ? -1 : _items.Count;
        }

        for (int i = 0; i < _items.Count; i++)
        {
            index += direction;

            if (index < 0 || index >= _items.Count) { return; }

            if (_items[index].IsDisabled) { continue; }

            if (IsMultiSelect || IsInput)
            {
                HighlightedItem = _items[index];

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

    private async Task ToggleDropDownVisibility() =>
        await JSRuntime.InvokeVoidAsync("toggleDropdown", _selectComponent);
}
