// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace EventLogExpert.Shared.Components.Menu;

public sealed partial class MenuRenderer
{
    // Constant rather than method-level call because Blazor's @onkeydown:preventDefault must
    // resolve at render time. Tab is handled in HandleListKeyDown so it can close the menu first.
    private const bool PreventDefaultKeyDown = true;

    // Type-ahead matches reset after this idle window (WAI-ARIA Authoring Practices guidance).
    private static readonly TimeSpan s_typeAheadResetWindow = TimeSpan.FromMilliseconds(500);

    private int _focusedIndex = -1;
    private bool _focusOnNextRender;
    private ElementReference[] _itemElements = [];
    private MenuItem? _openItem;
    private bool _openSubmenuFocusesFirstChild;
    private IReadOnlyList<MenuItem>? _previousItems;
    private bool _previousSuppressInitialFocus;
    private IReadOnlyList<MenuItem>? _resolvedChildren;
    private ElementReference _submenuElement;
    private string _typeAheadBuffer = string.Empty;
    private DateTimeOffset _typeAheadLastInputAt = DateTimeOffset.MinValue;

    /// <summary>0 for first enabled item, -1 for last. Used by hosts that open the menu via ArrowUp/ArrowDown.</summary>
    [Parameter] public int InitialFocusIndex { get; set; }

    /// <summary>True for the inner <see cref="MenuRenderer" /> instance rendered inside an open submenu.</summary>
    [Parameter] public bool IsSubmenu { get; set; }

    [Parameter] public IReadOnlyList<MenuItem>? Items { get; set; }

    /// <summary>
    ///     Raised when a leaf item (one without children) is activated. Hosts use this to close the popup. Bubbles up
    ///     through nested renderers.
    /// </summary>
    [Parameter] public EventCallback OnActivated { get; set; }

    /// <summary>Raised when the user presses ArrowLeft inside a submenu so the parent can collapse and refocus.</summary>
    [Parameter] public EventCallback OnCloseSubmenu { get; set; }

    /// <summary>
    ///     Raised when the top-level menu wants the menubar to switch to the previous (-1) or next (+1) bar item.
    ///     Hosts that don't sit on a menubar can ignore this.
    /// </summary>
    [Parameter] public EventCallback<int> OnNavigateBar { get; set; }

    /// <summary>
    ///     When true, the renderer will not set <c>_focusedIndex</c> from <see cref="InitialFocusIndex" /> on
    ///     parameter changes and will not auto-focus the first enabled item on first render. Used for hover-opened
    ///     submenus so keyboard focus stays on the parent item per WAI-ARIA menu guidance — focus only enters the
    ///     submenu when the user explicitly opens it via Enter/Space/ArrowRight.
    /// </summary>
    [Parameter] public bool SuppressInitialFocus { get; set; }

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    /// <summary>Programmatically focus the first/last item; called by hosts after the popup is in the DOM.</summary>
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
            try { await JSRuntime.InvokeVoidAsync("positionMenuSubmenu", _submenuElement); }
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
                // Hover-opened submenu: keep focus on the parent until the user explicitly enters
                // via Enter/Space/ArrowRight (which re-opens without SuppressInitialFocus).
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
            // SuppressInitialFocus flipped false while Items stayed the same reference — user
            // stepped into a hover-opened submenu, so move focus into it.
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

            if (!candidate.IsSeparator && candidate.IsEnabled) { return i; }
        }

        return -1;
    }

    private async Task HandleArrowLeftAsync()
    {
        // Submenu: collapse to parent. Top-level menu: ask the menubar to switch entries.
        if (IsSubmenu) { await OnCloseSubmenu.InvokeAsync(); }
        else { await OnNavigateBar.InvokeAsync(-1); }
    }

    private async Task HandleArrowRightAsync()
    {
        if (Items is null || _focusedIndex < 0) { return; }

        var item = Items[_focusedIndex];

        if (item.Children is not null || item.ChildrenLoader is not null)
        {
            await OpenSubmenu(item, focusFirstChild: true);
            return;
        }

        // Top-level leaf: ArrowRight advances the menubar. Submenus stay put so users don't lose
        // their place while navigating.
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
                // Block auto-repeat on activation keys; navigation keys above intentionally allow it.
                if (args.Repeat) { return; }
                if (_focusedIndex >= 0) { await OnItemActivate(Items[_focusedIndex], _focusedIndex); }

                return;
            case "Escape":
                if (IsSubmenu) { await OnCloseSubmenu.InvokeAsync(); }
                else { await OnActivated.InvokeAsync(); }

                return;
            case "Tab":
                // WAI-ARIA: Tab/Shift+Tab closes the entire menu. preventDefault on the <ul>
                // blocks browser tab traversal so focus-restore lands on the opener first.
                await OnActivated.InvokeAsync();

                return;
        }

        // Type-ahead: any single printable character. KeyboardEventArgs.Key respects keyboard layout.
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

        // A buffer of all-the-same-character cycles through matches (e.g., 'S' twice: Save -> System).
        // Multi-character buffers do exact prefix matching.
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

        // Cycling matches start after the focused item; multi-letter buffers match from the start.
        int startIndex = isCycling
            ? (_focusedIndex < 0 ? 0 : (_focusedIndex + 1) % Items.Count)
            : 0;

        for (int offset = 0; offset < Items.Count; offset++)
        {
            int index = (startIndex + offset) % Items.Count;
            var candidate = Items[index];

            if (candidate.IsSeparator || !candidate.IsEnabled) { continue; }

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

            if (candidate.IsSeparator || !candidate.IsEnabled) { continue; }

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
            await OpenSubmenu(item, focusFirstChild: true);
            return;
        }

        if (item.OnClickAsync is not null)
        {
            // Surface activation BEFORE invoking so the popup tears down before any modal opens.
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
        if (!item.IsEnabled) { return; }

        if (item.Children is null && item.ChildrenLoader is null)
        {
            // Hovering a leaf collapses any open sibling submenu and moves focus to the leaf
            // so roving tabindex stays in sync with _focusedIndex.
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
        _ = OpenSubmenu(item, focusFirstChild: false);
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
            // preventScroll keeps the page steady; scrollMenuItemIntoView scrolls within the menu panel.
            await _itemElements[_focusedIndex].FocusAsync(true);

            try { await JSRuntime.InvokeVoidAsync("scrollMenuItemIntoView", _itemElements[_focusedIndex]); }
            catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException) { }
        }
        catch
        {
            // Element may have been replaced or detached between render frames.
        }
    }
}

