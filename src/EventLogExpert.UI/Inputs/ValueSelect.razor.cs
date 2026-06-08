// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Common.Interop;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace EventLogExpert.UI.Inputs;

public sealed partial class ValueSelect<T> : InputComponent<T>, IAsyncDisposable
{
    private readonly string _itemId = $"select_{Guid.NewGuid().ToString()[..8]}";
    private readonly List<ValueSelectItem<T>> _items = [];
    private readonly HashSet<T> _selectedValues = [];

    private volatile bool _disposed;

    private IJSObjectReference? _dropdownModule;
    private ValueSelectItem<T>? _highlightedItem;
    private bool _isOpen;
    private bool _preventDefault;
    private ElementReference _selectComponent;
    private DotNetObjectReference<ValueSelect<T>>? _selfRef;

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

    [Parameter] public string? Id { get; set; }

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

    public Task CloseDropDown() => SetOpenStateAsync(false);

    public async ValueTask DisposeAsync()
    {
        _disposed = true;

        await JsModuleInterop.DisposeModuleSafelyAsync(
            _dropdownModule,
            module => module.InvokeVoidAsync("unregisterDropdown", _selectComponent));

        _dropdownModule = null;

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

    public Task OpenDropDown() => SetOpenStateAsync(true);

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

            _dropdownModule = await JSRuntime.InvokeAsync<IJSObjectReference>(
                "import",
                "./_content/EventLogExpert.UI/Inputs/ValueSelect.razor.js");

            await _dropdownModule.InvokeVoidAsync("registerDropdown", _selectComponent, _selfRef);
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

                if (_dropdownModule is not null) { await _dropdownModule.InvokeVoidAsync("scrollToHighlightedItem", _selectComponent); }
            }
            else
            {
                _selectedValues.Clear();
                _selectedValues.Add(_items[index].Value);

                await UpdateValue(_items[index].Value);

                if (_dropdownModule is not null) { await _dropdownModule.InvokeVoidAsync("scrollToSelectedItem", _selectComponent); }
            }

            return;
        }
    }

    private async Task SetOpenStateAsync(bool targetState)
    {
        if (_isOpen == targetState) { return; }

        _isOpen = targetState;
        StateHasChanged();
        var jsAction = targetState ? "openDropdown" : "closeDropdown";

        if (_dropdownModule is not null) { await _dropdownModule.InvokeVoidAsync(jsAction, _selectComponent); }
    }

    private Task ToggleDropDownVisibility() => SetOpenStateAsync(!_isOpen);
}
