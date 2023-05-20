// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Store.Settings;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EventLogExpert.Shared;

public partial class MainLayout : IDisposable
{
    [Inject] private IActionSubscriber ActionSubscriber { get; set; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;

    public void Dispose()
    {
        ActionSubscriber.UnsubscribeFromAllActions(this);
        GC.SuppressFinalize(this);
    }

    protected override void OnInitialized()
    {
        Utils.UpdateAppTitle();
        Dispatcher.Dispatch(new SettingsAction.LoadProviders(Utils.DatabasePath));
        Dispatcher.Dispatch(new SettingsAction.LoadSettings(Utils.SettingsPath));
        ActionSubscriber.SubscribeToAction<SettingsAction.OpenMenu>(this, OpenSettingsModal);
        Utils.CheckForUpdates();
        base.OnInitialized();
    }

    private void OpenSettingsModal(SettingsAction.OpenMenu action) =>
        JSRuntime.InvokeVoidAsync("openSettingsModal").AsTask();
}
