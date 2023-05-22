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

    protected override async Task OnInitializedAsync()
    {
        Dispatcher.Dispatch(new SettingsAction.LoadSettings(Utils.SettingsPath));
        Dispatcher.Dispatch(new SettingsAction.LoadProviders(Utils.DatabasePath));

        ActionSubscriber.SubscribeToAction<SettingsAction.OpenMenu>(this, OpenSettingsModal);
        ActionSubscriber.SubscribeToAction<SettingsAction.CheckForUpdates>(this, CheckForUpdates);

        await Utils.CheckForUpdates(SettingsState.Value.IsPrereleaseEnabled);
        Utils.UpdateAppTitle();

        await base.OnInitializedAsync();
    }

    private async void CheckForUpdates(SettingsAction.CheckForUpdates action)
    {
        bool result = await Utils.CheckForUpdates(SettingsState.Value.IsPrereleaseEnabled);

        if (result is false && Application.Current?.MainPage is not null)
        {
            await Application.Current.MainPage.DisplayAlert("No Updates Available",
                "You are currently running the latest version.",
                "Ok");
        }
    }

    private void OpenSettingsModal(SettingsAction.OpenMenu action)
    {

        JSRuntime.InvokeVoidAsync("openSettingsModal").AsTask();
    }
}
