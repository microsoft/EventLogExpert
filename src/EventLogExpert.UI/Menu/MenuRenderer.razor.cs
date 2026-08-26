// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.UI.Common.Interop;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace EventLogExpert.UI.Menu;

public sealed partial class MenuRenderer : IAsyncDisposable
{
    private const bool PreventDefaultKeyDown = true;

    private static readonly TimeSpan s_typeAheadResetWindow = TimeSpan.FromMilliseconds(500);
    private static long s_rendererIdCounter;

    private readonly long _rendererId = Interlocked.Increment(ref s_rendererIdCounter);

    private bool _focusOnNextRender;
    private int _focusedIndex = -1;
    private ElementReference[] _itemElements = [];
    private IJSObjectReference? _menuOverlayModule;
    private MenuItem? _openItem;
    private bool _openSubmenuFocusesFirstChild;
    private IReadOnlyList<MenuItem>? _previousItems;
    private bool _previousSuppressInitialFocus;
    private IJSObjectReference? _rendererModule;
    private IReadOnlyList<MenuItem>? _resolvedChildren;
    private ElementReference _submenuElement;
    private string _typeAheadBuffer = string.Empty;
    private DateTimeOffset _typeAheadLastInputAt = DateTimeOffset.MinValue;

    [Parameter] public int InitialFocusIndex { get; set; }

    [Parameter] public bool IsSubmenu { get; set; }

    [Parameter] public IReadOnlyList<MenuItem>? Items { get; set; }

    [Parameter] public EventCallback OnActivated { get; set; }

    [Parameter] public EventCallback OnCloseSubmenu { get; set; }

    [Parameter] public EventCallback<int> OnNavigateBar { get; set; }

    [Parameter] public bool SuppressInitialFocus { get; set; }

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private IStringLocalizer<SharedResource> Localizer { get; init; } = null!;

    public async ValueTask DisposeAsync()
    {
        await JsModuleInterop.DisposeModuleSafelyAsync(_menuOverlayModule);

        _menuOverlayModule = null;

        await JsModuleInterop.DisposeModuleSafelyAsync(_rendererModule);

        _rendererModule = null;
    }

    public Task FocusInitialAsync(bool focusFirst)
    {
        if (Items is null) { return Task.CompletedTask; }

        int index = focusFirst
            ? FindEnabledIndex(0, +1)
            : FindEnabledIndex(Items.Count - 1, -1);

        if (index < 0) { return Task.CompletedTask; }

        _focusedIndex = index;
        _focusOnNextRender = true;
        StateHasChanged();
        return Task.CompletedTask;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusOnNextRender)
        {
            _focusOnNextRender = false;
            await TryFocusCurrentAsync();
        }
        else if (firstRender && _focusedIndex >= 0 && !SuppressInitialFocus)
        {
            await TryFocusCurrentAsync();
        }

        if (_openItem is not null && _resolvedChildren is not null)
        {
            try
            {
                _menuOverlayModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>(
                    "import", "./_content/EventLogExpert.UI/Menu/MenuOverlay.js");

                await _menuOverlayModule.InvokeVoidAsync("positionMenuSubmenu", _submenuElement);
            }
            catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException) { }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(_previousItems, Items))
        {
            _previousItems = Items;
            _itemElements = Items is null ? [] : new ElementReference[Items.Count];

            if (Items is null)
            {
                _focusedIndex = -1;
            }
            else if (SuppressInitialFocus)
            {
                _focusedIndex = -1;
            }
            else
            {
                _focusedIndex = InitialFocusIndex == 0
                    ? FindEnabledIndex(0, +1)
                    : FindEnabledIndex(Items.Count - 1, -1);
            }
        }
        else if (_previousSuppressInitialFocus && !SuppressInitialFocus && _focusedIndex < 0 && Items is not null)
        {
            _focusedIndex = InitialFocusIndex == 0
                ? FindEnabledIndex(0, +1)
                : FindEnabledIndex(Items.Count - 1, -1);

            if (_focusedIndex >= 0) { _focusOnNextRender = true; }
        }

        _previousSuppressInitialFocus = SuppressInitialFocus;

        base.OnParametersSet();
    }

    private int FindEnabledIndex(int start, int direction)
    {
        if (Items is null || Items.Count == 0) { return -1; }

        for (int i = start; i >= 0 && i < Items.Count; i += direction)
        {
            var candidate = Items[i];

            if (candidate.IsFocusable) { return i; }
        }

        return -1;
    }

    private async Task HandleArrowLeftAsync()
    {
        if (IsSubmenu) { await OnCloseSubmenu.InvokeAsync(); }
        else { await OnNavigateBar.InvokeAsync(-1); }
    }

    private async Task HandleArrowRightAsync()
    {
        if (Items is null || _focusedIndex < 0) { return; }

        var item = Items[_focusedIndex];

        if (item.Children is not null || item.ChildrenLoader is not null)
        {
            await OpenSubmenu(item, true);

            return;
        }

        if (!IsSubmenu) { await OnNavigateBar.InvokeAsync(+1); }
    }

    private async Task HandleListKeyDown(KeyboardEventArgs args)
    {
        if (Items is null) { return; }

        switch (args.Key)
        {
            case "ArrowDown":
                MoveFocus(+1);
                return;
            case "ArrowUp":
                MoveFocus(-1);
                return;
            case "Home":
                MoveFocusTo(FindEnabledIndex(0, +1));
                return;
            case "End":
                MoveFocusTo(FindEnabledIndex(Items.Count - 1, -1));
                return;
            case "ArrowRight":
                await HandleArrowRightAsync();
                return;
            case "ArrowLeft":
                await HandleArrowLeftAsync();
                return;
            case "Enter":
            case " ":
                if (args.Repeat) { return; }

                if (_focusedIndex >= 0) { await OnItemActivate(Items[_focusedIndex], _focusedIndex); }

                return;
            case "Escape":
                if (IsSubmenu) { await OnCloseSubmenu.InvokeAsync(); }
                else { await OnActivated.InvokeAsync(); }

                return;
            case "Tab":
                await OnActivated.InvokeAsync();

                return;
        }

        if (args.Key.Length == 1 && !char.IsControl(args.Key, 0))
        {
            HandleTypeAhead(args.Key);
        }
    }

    private void HandleTypeAhead(string typedKey)
    {
        if (Items is null) { return; }

        var now = DateTimeOffset.UtcNow;

        if (now - _typeAheadLastInputAt > s_typeAheadResetWindow)
        {
            _typeAheadBuffer = string.Empty;
        }

        _typeAheadLastInputAt = now;
        _typeAheadBuffer += typedKey;

        bool repeatedSameChar = true;

        for (int charIndex = 1; charIndex < _typeAheadBuffer.Length; charIndex++)
        {
            if (char.ToUpperInvariant(_typeAheadBuffer[charIndex])
                != char.ToUpperInvariant(_typeAheadBuffer[0]))
            {
                repeatedSameChar = false;
                break;
            }
        }

        bool isCycling = _typeAheadBuffer.Length == 1 || repeatedSameChar;
        string matchPrefix = isCycling ? _typeAheadBuffer[..1] : _typeAheadBuffer;

        int startIndex = isCycling
            ? (_focusedIndex < 0 ? 0 : (_focusedIndex + 1) % Items.Count)
            : 0;

        for (int offset = 0; offset < Items.Count; offset++)
        {
            int index = (startIndex + offset) % Items.Count;
            var candidate = Items[index];

            if (!candidate.IsFocusable) { continue; }

            if (candidate.Label.StartsWith(matchPrefix, StringComparison.OrdinalIgnoreCase))
            {
                MoveFocusTo(index);
                return;
            }
        }
    }

    private void MoveFocus(int direction)
    {
        if (Items is null || Items.Count == 0) { return; }

        int start = _focusedIndex < 0
            ? (direction > 0 ? -1 : Items.Count)
            : _focusedIndex;

        for (int step = 0; step < Items.Count; step++)
        {
            start = ((start + direction) % Items.Count + Items.Count) % Items.Count;
            var candidate = Items[start];

            if (!candidate.IsFocusable) { continue; }

            MoveFocusTo(start);

            return;
        }
    }

    private void MoveFocusTo(int index)
    {
        if (index < 0 || Items is null || index >= Items.Count) { return; }

        _focusedIndex = index;
        _focusOnNextRender = true;

        StateHasChanged();
    }

    private async Task OnChildActivated()
    {
        _openItem = null;
        _resolvedChildren = null;

        await OnActivated.InvokeAsync();
    }

    private async Task OnItemActivate(MenuItem item, int index)
    {
        if (!item.IsEnabled) { return; }

        _focusedIndex = index;

        if (item.Children is not null || item.ChildrenLoader is not null)
        {
            await OpenSubmenu(item, true);

            return;
        }

        if (item.OnClickAsync is not null)
        {
            await OnActivated.InvokeAsync();
            await item.OnClickAsync();
        }
        else
        {
            await OnActivated.InvokeAsync();
        }
    }

    private void OnItemHover(MenuItem item, int index)
    {
        if (!item.IsFocusable) { return; }

        if (!item.IsEnabled)
        {
            if (_focusedIndex == index) { return; }

            _focusedIndex = index;
            _focusOnNextRender = true;

            StateHasChanged();

            return;
        }

        if (item.Children is null && item.ChildrenLoader is null)
        {
            if (_openItem is null && _focusedIndex == index) { return; }

            _openItem = null;
            _resolvedChildren = null;
            _focusedIndex = index;
            _focusOnNextRender = true;

            StateHasChanged();

            return;
        }

        if (ReferenceEquals(_openItem, item))
        {
            if (_focusedIndex == index) { return; }

            _focusedIndex = index;
            _focusOnNextRender = true;

            StateHasChanged();

            return;
        }

        _focusedIndex = index;
        _focusOnNextRender = true;
        _ = OpenSubmenu(item, false);
    }

    private async Task OnSubmenuRequestedClose()
    {
        if (_openItem is null) { return; }

        _openItem = null;
        _resolvedChildren = null;
        _focusOnNextRender = true;

        StateHasChanged();

        await Task.CompletedTask;
    }

    private async Task OpenSubmenu(MenuItem item, bool focusFirstChild)
    {
        _openItem = item;
        _resolvedChildren = item.Children;
        _openSubmenuFocusesFirstChild = focusFirstChild;

        if (item.Children is null && item.ChildrenLoader is not null)
        {
            StateHasChanged();

            try
            {
                var loaded = await item.ChildrenLoader();

                if (ReferenceEquals(_openItem, item))
                {
                    _resolvedChildren = loaded;
                    StateHasChanged();
                }
            }
            catch
            {
                if (ReferenceEquals(_openItem, item))
                {
                    _resolvedChildren = [];
                    StateHasChanged();
                }
            }
        }
        else
        {
            StateHasChanged();
        }
    }

    private async Task TryFocusCurrentAsync()
    {
        if (_focusedIndex < 0 || _focusedIndex >= _itemElements.Length) { return; }

        try
        {
            await _itemElements[_focusedIndex].FocusAsync(true);

            try
            {
                _rendererModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>(
                    "import", "./_content/EventLogExpert.UI/Menu/MenuRenderer.razor.js");

                await _rendererModule.InvokeVoidAsync("scrollMenuItemIntoView", _itemElements[_focusedIndex]);
            }
            catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException) { }
        }
        catch
        {
            // Element may have been replaced or detached between render frames.
        }
    }
}

