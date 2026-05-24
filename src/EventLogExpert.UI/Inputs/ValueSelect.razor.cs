// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace EventLogExpert.UI.Inputs;

public sealed partial class ValueSelect<T> : InputComponent<T>, IAsyncDisposable
{
    private readonly string _itemId = $"select_{Guid.NewGuid().ToString()[..8]}";
    private readonly List<ValueSelectItem<T>> _items = [];
    private readonly HashSet<T> _selectedValues = [];

    /// <summary>
    ///     Set to <c>true</c> in <see cref="DisposeAsync" /> before the JS unregister is awaited. Read by the
    ///     <see cref="JSInvokable" /> <see cref="OnIsOpenChanged" /> callback to suppress state updates that would land on a
    ///     disposed renderer. <c>volatile</c> because the JS callback executes on the JS-interop thread.
    /// </summary>
    private volatile bool _disposed;
    private ValueSelectItem<T>? _highlightedItem;
    private bool _isOpen;
    private bool _preventDefault;
    private ElementReference _selectComponent;
    private DotNetObjectReference<ValueSelect<T>>? _selfRef;

    [Parameter] public string? AriaDescribedBy { get; set; }

    [Parameter] public string? AriaLabel { get; set; }

    [Parameter] public string? AriaLabelledBy { get; set; }

    [Parameter]
    public RenderFragment ChildContent { get; set; } = null!;

    [Parameter] public string? DataHighlight { get; set; }

    /// <summary>
    ///     Text shown by a multi-select <see cref="ValueSelect{T}" /> when no values are selected. Defaults to "Empty";
    ///     consumers should override with a domain-appropriate label such as "All" when an empty selection means "no filter
    ///     applied".
    /// </summary>
    [Parameter]
    public string EmptyText { get; set; } = "Empty";

    public bool HasAnySelection => _selectedValues.Count > 0;

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

            if (Values.Count <= 0) { return EmptyText; }

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

    public Task CloseDropDown() => SetOpenStateAsync(false, "closeDropdown");

    public async ValueTask DisposeAsync()
    {
        _disposed = true;

        try
        {
            await JSRuntime.InvokeVoidAsync("unregisterDropdown", _selectComponent);
        }
        catch (JSDisconnectedException)
        {
            // Expected during app shutdown
        }

        _selfRef?.Dispose();
    }

    public bool IsItemSelected(T value) => _selectedValues.Contains(value);

    [JSInvokable]
    public void OnIsOpenChanged(bool isOpen)
    {
        if (_disposed) { return; }
        if (_isOpen == isOpen) { return; }

        _isOpen = isOpen;

        try
        {
            _ = InvokeAsync(() =>
            {
                if (_disposed) { return; }
                StateHasChanged();
            });
        }
        catch (ObjectDisposedException)
        {
            // Disposed between _disposed check and dispatch; ignore.
        }
    }

    public Task OpenDropDown() => SetOpenStateAsync(true, "openDropdown");

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
            _selfRef = DotNetObjectReference.Create(this);
            await JSRuntime.InvokeVoidAsync("registerDropdown", _selectComponent, _selfRef);
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

    private async Task SetOpenStateAsync(bool targetState, string jsAction)
    {
        if (_isOpen == targetState) { return; }

        _isOpen = targetState;
        StateHasChanged();
        await JSRuntime.InvokeVoidAsync(jsAction, _selectComponent);
    }

    private Task ToggleDropDownVisibility() => SetOpenStateAsync(!_isOpen, "toggleDropdown");
}
