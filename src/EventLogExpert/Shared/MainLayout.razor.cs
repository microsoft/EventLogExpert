// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store.Settings;
using Fluxor;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared;

public sealed partial class MainLayout
{
    [Inject] private IAppTitleService AppTitleService { get; init; } = null!;

    [Inject] private ICurrentVersionProvider CurrentVersionProvider { get; init; } = null!;

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
}
