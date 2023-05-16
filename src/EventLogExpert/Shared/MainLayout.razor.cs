// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Store.Settings;
using Microsoft.JSInterop;

namespace EventLogExpert.Shared;

public partial class MainLayout : IDisposable
{
    public void Dispose()
    {
        ActionSubscriber.UnsubscribeFromAllActions(this);
        GC.SuppressFinalize(this);
    }

    protected override void OnInitialized()
    {
        Dispatcher.Dispatch(new SettingsAction.LoadProviders(Utils.DatabasePath));
        Dispatcher.Dispatch(new SettingsAction.LoadSettings(Utils.SettingsPath));
        ActionSubscriber.SubscribeToAction<SettingsAction.OpenMenu>(this, OpenSettingsModal);
        Utils.CheckForUpdates();
        base.OnInitialized();
    }

    private void OpenSettingsModal(SettingsAction.OpenMenu action) =>
        JsRuntime.InvokeVoidAsync("openSettingsModal").AsTask();
}
