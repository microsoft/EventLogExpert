// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Globalization;

namespace EventLogExpert.Shared.Components.Menu;

public sealed partial class MenuHost : IAsyncDisposable
{
    private long _focusedMenuId;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private IMenuService MenuService { get; init; } = null!;

    private string PositionStyle =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"left: {MenuService.PositionX}px; top: {MenuService.PositionY}px;");

    public async ValueTask DisposeAsync()
    {
        MenuService.StateChanged -= OnStateChanged;
        await ReleaseFocusReturnAsync();
    }

    protected override void OnInitialized()
    {
        MenuService.StateChanged += OnStateChanged;
        base.OnInitialized();
    }

    private void HandleActivated() => MenuService.Close();

    private void HandleKeyDown(KeyboardEventArgs args)
    {
        // Host-level Escape fallback. MenuRenderer's <ul> stops keydown propagation, so this
        // handler does not run for events originating inside the menu list — those are closed by
        // the renderer itself. This branch only fires when focus has drifted outside the renderer
        // (e.g., onto the overlay) so Escape still closes the menu.
        if (args.Key == "Escape") { MenuService.Close(); }
    }

    private void HandleNavigateBar(int direction) => MenuService.NavigateBar(direction);

    private void HandleOverlayClick() => MenuService.Close();

    private void OnStateChanged()
    {
        // StateChanged can be raised from arbitrary threads (Fluxor effects, JS-callback paths).
        // Marshal everything — including the JS focus capture/restore — through the renderer
        // dispatcher so IJSRuntime stays on its required thread and StateHasChanged is sequenced
        // after.
        _ = InvokeAsync(async () =>
        {
            var nowOpen = MenuService.ActiveItems is not null;
            var wasOpen = _focusedMenuId != 0;

            if (nowOpen && MenuService.ActiveMenuId != _focusedMenuId)
            {
                // Capture the opener BEFORE StateHasChanged so document.activeElement is still the
                // element that triggered the currently active menu (a menubar button, table header,
                // context-menu trigger, etc.) rather than an item inside the popup the renderer is
                // about to focus. Re-capture on every active-menu change so transitions like
                // context-menu -> menubar dropdown restore focus to the most recent trigger; the
                // caller passes captureOpener=false (e.g., menubar arrow-key navigation) when the
                // original opener should be preserved instead.
                if (MenuService.ActiveCaptureOpener)
                {
                    try { await JSRuntime.InvokeVoidAsync("captureMenuOpener"); }
                    catch { /* JS may be disconnected during teardown */ }
                }

                _focusedMenuId = MenuService.ActiveMenuId;
            }
            else if (!nowOpen && wasOpen)
            {
                _focusedMenuId = 0;
                await ReleaseFocusReturnAsync();
            }

            StateHasChanged();
        });
    }

    private async ValueTask ReleaseFocusReturnAsync()
    {
        try { await JSRuntime.InvokeVoidAsync("restoreMenuOpenerFocus"); }
        catch { /* JS may be disconnected during teardown */ }
    }
}
