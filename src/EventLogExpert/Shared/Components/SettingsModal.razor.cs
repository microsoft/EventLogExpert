// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using EventLogExpert.Store.Settings;
using Microsoft.JSInterop;

namespace EventLogExpert.Shared.Components;

public partial class SettingsModal
{
    private readonly SettingsModel _request = new();

    protected override void OnInitialized()
    {
        SubscribeToAction<SettingsAction.OpenMenu>(Load);
        base.OnInitialized();
    }

    private async void Close() => await JsRuntime.InvokeVoidAsync("closeSettingsModal");

    private void Load(SettingsAction.OpenMenu action) => _request.TimeZoneId = SettingsState.Value.TimeZoneId;

    private void Save()
    {
        Dispatcher.Dispatch(new SettingsAction.Save(_request, Utils.SettingsPath));
        Close();
    }
}
