// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Services;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store.FilterPane;
using EventLogExpert.UI.Store.Settings;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared;

public sealed partial class MainLayout
{
    [Inject] private IAppTitleService AppTitleService { get; init; } = null!;

    [Inject] private IClipboardService ClipboardService { get; init; } = null!;

    [Inject] private ICurrentVersionProvider CurrentVersionProvider { get; init; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IState<SettingsState> SettingsState { get; init; } = null!;

    [Inject] private IUpdateService UpdateService { get; init; } = null!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            if (CurrentVersionProvider.IsSupportedOS(DeviceInfo.Version))
            {
                await UpdateService.CheckForUpdates(SettingsState.Value.Config.IsPrereleaseEnabled, false);
            }

            AppTitleService.SetLogName(null);
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    private void HandleKeyUp(KeyboardEventArgs args)
    {
        // https://developer.mozilla.org/en-US/docs/Web/API/UI_Events/Keyboard_event_key_values
        switch (args)
        {
            case { CtrlKey: true, Code: "KeyC" } :
                ClipboardService.CopySelectedEvent();
                break;

            case { CtrlKey: true, Code: "KeyH" } :
                Dispatcher.Dispatch(new FilterPaneAction.ToggleIsEnabled());
                break;
        }
    }
}
