// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Globalization;

namespace EventLogExpert.Shared.Components.Menu;

public sealed partial class MenuHost : IAsyncDisposable
{
    private long _focusedMenuId;
    private ElementReference _popupElement;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private IMenuService MenuService { get; init; } = null!;

    private string PositionStyle =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"left: {MenuService.PositionX}px; top: {MenuService.PositionY}px;");

    public async ValueTask DisposeAsync()
    {
        MenuService.StateChanged -= OnStateChanged;

        try { await JSRuntime.InvokeVoidAsync("detachMenuViewportListeners"); }
        catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException) { }

        await ReleaseFocusReturnAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Re-clamp every render — popup is hidden until clampMenuPopup reveals it within the viewport.
        if (MenuService.ActiveItems is not null)
        {
            try { await JSRuntime.InvokeVoidAsync("clampMenuPopup", _popupElement); }
            catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException) { }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override void OnInitialized()
    {
        MenuService.StateChanged += OnStateChanged;
        base.OnInitialized();
    }

    private void HandleActivated() => MenuService.Close();

    private void HandleKeyDown(KeyboardEventArgs args)
    {
        // Host-level Escape fallback when focus has drifted outside the menu list (e.g., onto the overlay).
        if (args.Key == "Escape") { MenuService.Close(); }
    }

    private void HandleNavigateBar(int direction) => MenuService.NavigateBar(direction);

    private void HandleOverlayClick() => MenuService.Close();

    private void OnStateChanged()
    {
        // StateChanged may fire from arbitrary threads — marshal through the renderer dispatcher.
        _ = InvokeAsync(async () =>
        {
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
                    try { await JSRuntime.InvokeVoidAsync("captureMenuOpener"); }
                    catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException) { }
                }

                if (!wasOpen)
                {
                    try { await JSRuntime.InvokeVoidAsync("attachMenuViewportListeners"); }
                    catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException) { }
                }
            }
            else if (!nowOpen && wasOpen)
            {
                _focusedMenuId = 0;

                try { await JSRuntime.InvokeVoidAsync("detachMenuViewportListeners"); }
                catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException) { }

                await ReleaseFocusReturnAsync();
            }

            StateHasChanged();
        });
    }

    private async ValueTask ReleaseFocusReturnAsync()
    {
        try { await JSRuntime.InvokeVoidAsync("restoreMenuOpenerFocus"); }
        catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException) { }
    }
}
