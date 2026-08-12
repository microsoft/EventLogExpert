// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Common.Interop;
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

        MenuService.StateChanged -= OnStateChanged;
        Registry.ActiveHostChanged -= OnActiveHostChanged;

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
        if (_disposed || !IsActive) { return; }

        _ = InvokeAsync(async () =>
        {
            if (_disposed || !IsActive) { return; }

            var nowOpen = MenuService.ActiveItems is not null;
            var wasOpen = _focusedMenuId != 0;

            if (nowOpen && MenuService.ActiveMenuId != _focusedMenuId)
            {
                _focusedMenuId = MenuService.ActiveMenuId;

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
