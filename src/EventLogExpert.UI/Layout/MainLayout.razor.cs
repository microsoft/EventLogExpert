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
    [Inject] private IAppTitleService AppTitleService { get; init; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private KeyboardShortcutService KeyboardShortcutService { get; init; } = null!;

    [Inject] private ISettingsService Settings { get; init; } = null!;

    [Inject] private IUpdateService UpdateService { get; init; } = null!;

    private Task<IJSObjectReference>? _themeModuleLoad;
    private bool _disposed;

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
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await ApplyThemeAsync();
            await KeyboardShortcutService.EnsureRegisteredAsync(JSRuntime);
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

        var load = _themeModuleLoad ??= JSRuntime.InvokeAsync<IJSObjectReference>("import", "./_content/EventLogExpert.UI/Layout/MainLayout.razor.js").AsTask();

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
}
