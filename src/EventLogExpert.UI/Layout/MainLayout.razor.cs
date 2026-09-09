// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.AppTitle;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.Runtime.Update;
using EventLogExpert.UI.Keyboard;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EventLogExpert.UI.Layout;

public sealed partial class MainLayout : IAsyncDisposable
{
    private bool _disposed;
    private Task<IJSObjectReference>? _focusRingModuleLoad;
    private Task<IJSObjectReference>? _focusTooltipModuleLoad;
    private Task<IJSObjectReference>? _themeModuleLoad;

    [Inject] private IAppTitleService AppTitleService { get; init; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private KeyboardShortcutService KeyboardShortcutService { get; init; } = null!;

    [Inject] private ISettingsService Settings { get; init; } = null!;

    [Inject] private IUpdateService UpdateService { get; init; } = null!;

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        Settings.ThemeChanged -= OnThemeChanged;

        await KeyboardShortcutService.UnregisterAsync();

        if (_themeModuleLoad is not null)
        {
            try
            {
                var module = await _themeModuleLoad;
                await module.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
            catch (JSException) { }
            catch (ObjectDisposedException) { }
            catch (TaskCanceledException) { }

            _themeModuleLoad = null;
        }

        if (_focusRingModuleLoad is not null)
        {
            try
            {
                var module = await _focusRingModuleLoad;
                await module.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
            catch (JSException) { }
            catch (ObjectDisposedException) { }
            catch (TaskCanceledException) { }

            _focusRingModuleLoad = null;
        }

        if (_focusTooltipModuleLoad is not null)
        {
            try
            {
                var module = await _focusTooltipModuleLoad;
                await module.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
            catch (JSException) { }
            catch (ObjectDisposedException) { }
            catch (TaskCanceledException) { }

            _focusTooltipModuleLoad = null;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await ApplyThemeAsync();
            await KeyboardShortcutService.EnsureRegisteredAsync(JSRuntime);
            await RegisterKeyboardFocusRingAsync();
            await RegisterFocusTooltipAsync();
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override async Task OnInitializedAsync()
    {
        Settings.ThemeChanged += OnThemeChanged;

        await UpdateService.CheckForUpdates(Settings.IsPreReleaseEnabled);
        AppTitleService.SetLogName(null);

        await base.OnInitializedAsync();
    }

    private async Task ApplyThemeAsync()
    {
        if (_disposed) { return; }

        var load = _themeModuleLoad ??= JSRuntime
            .InvokeAsync<IJSObjectReference>("import", "./_content/EventLogExpert.UI/Layout/MainLayout.razor.js")
            .AsTask();

        try
        {
            var module = await load;
            await module.InvokeVoidAsync("setTheme", Settings.Theme.ToString().ToLowerInvariant());
        }
        catch (Exception ex) when (ex is JSDisconnectedException or JSException or ObjectDisposedException or TaskCanceledException)
        {
            ClearFailedThemeLoad(load);
        }
    }

    private void ClearFailedThemeLoad(Task<IJSObjectReference> load)
    {
        if (!load.IsCompletedSuccessfully && ReferenceEquals(_themeModuleLoad, load))
        {
            _themeModuleLoad = null;
        }
    }

    private void OnThemeChanged() => _ = InvokeAsync(ApplyThemeAsync);

    private async Task RegisterFocusTooltipAsync()
    {
        if (_disposed) { return; }

        var load = _focusTooltipModuleLoad ??= JSRuntime.InvokeAsync<IJSObjectReference>(
            "import",
            "./_content/EventLogExpert.UI/Common/focusTooltip.js").AsTask();

        try
        {
            var module = await load;
            await module.InvokeVoidAsync("registerFocusTooltip");
        }
        catch (Exception ex) when (ex is JSDisconnectedException or JSException or ObjectDisposedException or TaskCanceledException)
        {
            if (!load.IsCompletedSuccessfully && ReferenceEquals(_focusTooltipModuleLoad, load))
            {
                _focusTooltipModuleLoad = null;
            }
        }
    }

    private async Task RegisterKeyboardFocusRingAsync()
    {
        if (_disposed) { return; }

        var load = _focusRingModuleLoad ??= JSRuntime.InvokeAsync<IJSObjectReference>(
            "import",
            "./_content/EventLogExpert.UI/Common/keyboardFocusRing.js").AsTask();

        try
        {
            var module = await load;
            await module.InvokeVoidAsync("registerKeyboardFocusRing");
        }
        catch (Exception ex) when (ex is JSDisconnectedException or JSException or ObjectDisposedException or TaskCanceledException)
        {
            if (!load.IsCompletedSuccessfully && ReferenceEquals(_focusRingModuleLoad, load))
            {
                _focusRingModuleLoad = null;
            }
        }
    }
}
