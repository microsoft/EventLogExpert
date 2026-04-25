// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace EventLogExpert.Shared.Components.Menu;

public sealed partial class MenuRenderer
{
    // Type-ahead matches reset after this idle window. Mirrors common menu type-ahead behavior
    // (e.g. Win32 menubar, WAI-ARIA Authoring Practices) so users can either type a single letter
    // to step through matches or quickly type a longer prefix.
    private static readonly TimeSpan s_typeAheadResetWindow = TimeSpan.FromMilliseconds(500);

    private int _focusedIndex = -1;
    private bool _focusOnNextRender;
    private ElementReference[] _itemElements = [];
    private MenuItem? _openItem;
    private bool _openSubmenuFocusesFirstChild;

    // Always preventDefault on the menu list so arrow keys / Space don't scroll the page and Tab
    // doesn't escape past the focus-restore plumbing. We model this as a constant rather than a
    // method-level call because Blazor's `@onkeydown:preventDefault` directive must resolve at
    // render time. Tab is handled explicitly in HandleListKeyDown to close the menu first.
    private bool _preventDefaultKeyDown = true;
    private IReadOnlyList<MenuItem>? _previousItems;
    private bool _previousSuppressInitialFocus;
    private IReadOnlyList<MenuItem>? _resolvedChildren;
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
                // Hover-opened submenu: leave _focusedIndex at -1 so no item is marked focused.
                // Keyboard focus stays on the parent menu item, and the parent list handles
                // ArrowUp/Down/Home/End for sibling navigation. The user explicitly steps into
                // this submenu by pressing Enter, Space, or ArrowRight on the parent item, which
                // re-opens the submenu via OpenSubmenu without SuppressInitialFocus.
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
            // SuppressInitialFocus flipped from true -> false while Items stayed the same reference
            // (e.g., a hover-opened submenu the user explicitly enters via Enter/Space/ArrowRight,
            // which re-renders this child with SuppressInitialFocus=false but the same Items list).
            // Pick the first enabled item and schedule focus so keyboard focus actually moves into
            // the submenu instead of staying stuck on the parent.
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
        // In a submenu: ArrowLeft collapses back to the parent. In a top-level menu it asks the
        // menubar to switch to the previous bar entry.
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

        // Leaf item in a top-level menu: ArrowRight moves to the next menubar entry. Submenus
        // intentionally do nothing so users can keep navigating up/down without losing place.
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
                // Suppress auto-repeat for activation keys so holding Enter/Space doesn't
                // re-fire the action repeatedly. Navigation keys above intentionally allow
                // repeat so users can hold ArrowDown/Up/Home/End to traverse long menus.
                if (args.Repeat) { return; }
                if (_focusedIndex >= 0) { await OnItemActivate(Items[_focusedIndex], _focusedIndex); }

                return;
            case "Escape":
                if (IsSubmenu) { await OnCloseSubmenu.InvokeAsync(); }
                else { await OnActivated.InvokeAsync(); }

                return;
            case "Tab":
                // WAI-ARIA: Tab/Shift+Tab closes the entire menu (not just the current submenu).
                // preventDefault on the <ul> blocks the browser's tab traversal so OnActivated's
                // focus-restore lands on the opener button first; from there the user presses Tab
                // again to advance normally. Bubble OnActivated upward through every nested
                // renderer so a single Tab consistently exits the whole menu chain.
                await OnActivated.InvokeAsync();

                return;
        }

        // Type-ahead: any single printable character. KeyboardEventArgs.Key contains the typed
        // character (respects keyboard layout), unlike Code which is physical-key based.
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

        // Treat a buffer of all-the-same-character as cycling so repeated taps of the same letter
        // step through matches (e.g. pressing 'S' twice moves Save -> System), matching common
        // menu behavior. Otherwise multi-character buffers do exact prefix matching.
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

        // Cycling matches starting after the focused item; multi-letter buffer matches by prefix
        // from the start of the list so users can disambiguate quickly.
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

        // Wrap when scanning past either end so Down/Up cycle through enabled items only.
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

        // Keep the focused item in sync with click-driven activation so subsequent keyboard
        // navigation starts from the user's pointer position.
        _focusedIndex = index;

        if (item.Children is not null || item.ChildrenLoader is not null)
        {
            await OpenSubmenu(item, focusFirstChild: true);
            return;
        }

        if (item.OnClickAsync is not null)
        {
            // Surface the activation to the host BEFORE invoking the action so the popup tears down
            // before any modal the action might open — otherwise the menu would briefly overlap.
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
            // Hovering a leaf collapses any open sibling submenu — matches native menu behavior.
            // Move DOM focus to the hovered item so roving tabindex and the focus ring stay in
            // sync with _focusedIndex (otherwise focus can remain on an item now tabindex=-1).
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

        // Submenus are autonomous renderers; they pick up InitialFocusIndex from their own
        // OnParametersSet so we don't need to chase the inner ElementReference here.
        if (!focusFirstChild)
        {
            // Hover-open: keep focus on the parent item so arrow keys still target the parent menu.
        }
    }

    private async Task TryFocusCurrentAsync()
    {
        if (_focusedIndex < 0 || _focusedIndex >= _itemElements.Length) { return; }

        try
        {
            await _itemElements[_focusedIndex].FocusAsync(true);
        }
        catch
        {
            // Element may have been replaced or detached between render frames — best effort only.
        }
    }
}

