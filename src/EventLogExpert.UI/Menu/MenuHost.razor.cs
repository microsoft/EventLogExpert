// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Menu;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Globalization;

namespace EventLogExpert.UI.Menu;

public sealed partial class MenuHost : IAsyncDisposable
{
    private bool _disposed;
    private long _focusedMenuId;
    private IJSObjectReference? _menuOverlayModule;
    private bool _ownedViewportListeners;
    private ElementReference _popupElement;

    private bool IsActive => ReferenceEquals(Registry.ActiveHost, this);

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private IMenuService MenuService { get; init; } = null!;

    private string PositionStyle =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"left: {MenuService.PositionX}px; top: {MenuService.PositionY}px;");

    [Inject] private IMenuHostRegistry Registry { get; init; } = null!;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) { return; }

        _disposed = true;

        // Unsubscribe BEFORE Close() — Close() synchronously raises StateChanged,
        // which would re-enter OnStateChanged and queue an InvokeAsync that runs
        // after dispose completes.
        MenuService.StateChanged -= OnStateChanged;
        Registry.ActiveHostChanged -= OnActiveHostChanged;

        // Close on live MenuService state, not on the queued-lambda-mutated
        // _focusedMenuId — that mirror can lag the singleton when dispose
        // races a fresh OpenAt.
        if (IsActive && MenuService.ActiveItems is not null)
        {
            MenuService.Close();
        }

        Registry.Unregister(this);

        if (_focusedMenuId != 0 || _ownedViewportListeners)
        {
            try { await (await GetMenuOverlayAsync()).InvokeVoidAsync("detachMenuViewportListeners"); }
            catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException) { }

            await ReleaseFocusReturnAsync();
            _ownedViewportListeners = false;
        }

        await JsModuleInterop.DisposeModuleSafelyAsync(_menuOverlayModule);

        _menuOverlayModule = null;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_disposed || !IsActive)
        {
            await base.OnAfterRenderAsync(firstRender);

            return;
        }

        // Re-clamp every render — popup is hidden until clampMenuPopup reveals it within the viewport.
        if (MenuService.ActiveItems is not null)
        {
            try { await (await GetMenuOverlayAsync()).InvokeVoidAsync("clampMenuPopup", _popupElement); }
            catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException) { }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override void OnInitialized()
    {
        Registry.Register(this);
        Registry.ActiveHostChanged += OnActiveHostChanged;
        MenuService.StateChanged += OnStateChanged;
        SyncMenuOwnershipMirror();
        base.OnInitialized();
    }

    private async ValueTask<IJSObjectReference> GetMenuOverlayAsync() =>
        _menuOverlayModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>(
            "import", "./_content/EventLogExpert.UI/Menu/MenuOverlay.js");

    private void HandleActivated() => MenuService.Close();

    private void HandleKeyDown(KeyboardEventArgs args)
    {
        // Host-level Escape fallback when focus has drifted outside the menu list (e.g., onto the overlay).
        if (args.Key == "Escape") { MenuService.Close(); }
    }

    private void HandleNavigateBar(int direction) => MenuService.NavigateBar(direction);

    private void HandleOverlayClick() => MenuService.Close();

    private void OnActiveHostChanged()
    {
        if (_disposed) { return; }

        _ = InvokeAsync(() =>
        {
            if (_disposed) { return; }

            SyncMenuOwnershipMirror();
            StateHasChanged();
        });
    }

    private void OnStateChanged()
    {
        // Inactive hosts stay subscribed but skip render + JS interop so the
        // active topmost host owns the menu lifecycle.
        if (_disposed || !IsActive) { return; }

        // StateChanged may fire from arbitrary threads — marshal through the renderer dispatcher.
        _ = InvokeAsync(async () =>
        {
            if (_disposed || !IsActive) { return; }

            var nowOpen = MenuService.ActiveItems is not null;
            var wasOpen = _focusedMenuId != 0;

            if (nowOpen && MenuService.ActiveMenuId != _focusedMenuId)
            {
                // Update synchronously before any await so a re-entrant OnStateChanged
                // (open immediately followed by close) sees the correct wasOpen.
                _focusedMenuId = MenuService.ActiveMenuId;

                // Capture the opener before StateHasChanged so document.activeElement is still
                // the element that triggered the menu, not an item the renderer is about to focus.
                if (MenuService.ActiveCaptureOpener)
                {
                    try { await (await GetMenuOverlayAsync()).InvokeVoidAsync("captureMenuOpener"); }
                    catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException) { }
                }

                if (!wasOpen)
                {
                    try
                    {
                        await (await GetMenuOverlayAsync()).InvokeVoidAsync("attachMenuViewportListeners");
                        _ownedViewportListeners = true;
                    }
                    catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException) { }
                }
            }
            else if (!nowOpen && wasOpen)
            {
                _focusedMenuId = 0;

                try
                {
                    await (await GetMenuOverlayAsync()).InvokeVoidAsync("detachMenuViewportListeners");
                    _ownedViewportListeners = false;
                }
                catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException) { }

                await ReleaseFocusReturnAsync();
            }

            StateHasChanged();
        });
    }

    private async ValueTask ReleaseFocusReturnAsync()
    {
        try { await (await GetMenuOverlayAsync()).InvokeVoidAsync("restoreMenuOpenerFocus"); }
        catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException) { }
    }

    private void SyncMenuOwnershipMirror()
    {
        if (MenuService.ActiveItems is null) { return; }

        if (IsActive && _focusedMenuId == 0)
        {
            _focusedMenuId = MenuService.ActiveMenuId;
            _ownedViewportListeners = true;
        }
        else if (!IsActive && _focusedMenuId != 0)
        {
            _focusedMenuId = 0;
            _ownedViewportListeners = false;
        }
    }
}
