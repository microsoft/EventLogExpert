// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store.FilterPane;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared;

public sealed partial class MainLayout : IDisposable
{
    [Inject] private IAppTitleService AppTitleService { get; init; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private ISettingsService Settings { get; init; } = null!;

    [Inject] private IUpdateService UpdateService { get; init; } = null!;

    public void Dispose() => Settings.ThemeChanged -= OnThemeChanged;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await ApplyThemeAsync();
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

    private async Task ApplyThemeAsync()
    {
        await JSRuntime.InvokeVoidAsync("setTheme", Settings.Theme.ToString().ToLowerInvariant());
    }

    private void HandleKeyUp(KeyboardEventArgs args)
    {
        // https://developer.mozilla.org/en-US/docs/Web/API/UI_Events/Keyboard_event_key_values
        switch (args)
        {
            case { CtrlKey: true, Code: "KeyH" }:
                Dispatcher.Dispatch(new FilterPaneAction.ToggleIsEnabled());
                break;
        }
    }

    private void OnThemeChanged() => _ = InvokeAsync(ApplyThemeAsync);
}
