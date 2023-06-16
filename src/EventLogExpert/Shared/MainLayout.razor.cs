// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Store.Settings;
using EventLogExpert.UI.Services;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EventLogExpert.Shared;

public partial class MainLayout : IDisposable
{
    [Inject] private IActionSubscriber ActionSubscriber { get; set; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;

    [Inject] private IUpdateService UpdateService { get; set; } = null!;

    [Inject] private IAppTitleService AppTitleService { get; set; } = null!;

    public void Dispose()
    {
        ActionSubscriber.UnsubscribeFromAllActions(this);
        GC.SuppressFinalize(this);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await UpdateService.CheckForUpdates(SettingsState.Value.Config.IsPrereleaseEnabled, false);
            AppTitleService.SetLogName(null);
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override async Task OnInitializedAsync()
    {
        ActionSubscriber.SubscribeToAction<SettingsAction.OpenMenu>(this, OpenSettingsModal);

        await base.OnInitializedAsync();
    }

    private void OpenSettingsModal(SettingsAction.OpenMenu action)
    {

        JSRuntime.InvokeVoidAsync("openSettingsModal").AsTask();
    }
}
