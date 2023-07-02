// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Services;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared;

public partial class MainLayout
{
    [Inject] private IAppTitleService AppTitleService { get; set; } = null!;

    [Inject] private ICurrentVersionProvider CurrentVersionProvider { get; set; } = null!;

    [Inject] private IUpdateService UpdateService { get; set; } = null!;

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
