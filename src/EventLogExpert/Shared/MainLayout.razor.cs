// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Services;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EventLogExpert.Shared;

public sealed partial class MainLayout : IAsyncDisposable
{
    [Inject] private IAppTitleService AppTitleService { get; init; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private KeyboardShortcutService KeyboardShortcutService { get; init; } = null!;

    [Inject] private ISettingsService Settings { get; init; } = null!;

    [Inject] private IUpdateService UpdateService { get; init; } = null!;

    public async ValueTask DisposeAsync()
    {
        Settings.ThemeChanged -= OnThemeChanged;

        await KeyboardShortcutService.UnregisterAsync();
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

        await UpdateService.CheckForUpdates(Settings.IsPreReleaseEnabled, false);
        AppTitleService.SetLogName(null);

        await base.OnInitializedAsync();
    }

    private async Task ApplyThemeAsync() =>
        await JSRuntime.InvokeVoidAsync("setTheme", Settings.Theme.ToString().ToLowerInvariant());

    private void OnThemeChanged() => _ = InvokeAsync(ApplyThemeAsync);
}
