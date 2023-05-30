// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Helpers;
using EventLogExpert.Library.Models;
using EventLogExpert.Store.Settings;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.IO.Compression;

namespace EventLogExpert.Shared.Components;

public partial class SettingsModal
{
    private List<string>? _providers;
    private bool _providersHasChanged;
    private SettingsModel _request = new();

    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;

    protected override void OnInitialized()
    {
        SettingsState.StateChanged += (s, e) => ResetSettingsModel();

        base.OnInitialized();
    }

    private async void Close()
    {
        await JSRuntime.InvokeVoidAsync("closeSettingsModal");
        ResetSettingsModel();
    }

    private void DisableProvider(string provider)
    {
        _request.DisabledProviders ??= new List<string>();

        _request.DisabledProviders?.Add(provider);
        _providers?.Remove(provider);

        _providersHasChanged = true;
    }

    private void EnableProvider(string provider)
    {
        _request.DisabledProviders?.Remove(provider);
        _providers?.Add(provider);

        _providersHasChanged = true;
    }

    private async void ImportProvider()
    {
        PickOptions options = new()
        {
            PickerTitle = "Please select a database file",
            FileTypes = new FilePickerFileType(
                new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".db", ".zip" } }
                })
        };

        try
        {
            var result = (await FilePicker.Default.PickMultipleAsync(options)).ToList();

            // User canceled or no files selected
            if (!result.Any()) { return; }

            Directory.CreateDirectory(Utils.DatabasePath);

            foreach (var item in result)
            {
                var destination = Path.Join(Utils.DatabasePath, item.FileName);
                File.Copy(item.FullPath, destination, true);

                if (Path.GetExtension(destination) == ".zip")
                {
                    ZipFile.ExtractToDirectory(destination, Utils.DatabasePath, overwriteFiles: true);
                    File.Delete(destination);
                }
            }
        }
        catch
        { // TODO: Log Error
            return;
        }

        bool answer = await Application.Current!.MainPage!.DisplayAlert("Application Restart Required",
            "In order to use these providers, a restart of the application is required. Would you like to restart now?",
            "Yes",
            "No");

        if (!answer)
        {
            Dispatcher.Dispatch(new SettingsAction.LoadProviders());
            return;
        }

        uint res = NativeMethods.RegisterApplicationRestart(null, NativeMethods.RestartFlags.NONE);

        if (res == 0) { Application.Current.Quit(); }
    }

    private async void RemoveProvider(string provider)
    {
        bool answer = await Application.Current!.MainPage!.DisplayAlert("Remove Provider",
            "Are you sure you want to remove this provider?",
            "Yes",
            "No");

        if (!answer) { return; }

        try
        {
            var destination = Path.Join(Utils.DatabasePath, provider);
            File.Delete(destination);
        }
        catch
        { // TODO: Log Error
            return;
        }

        Dispatcher.Dispatch(new SettingsAction.LoadProviders());
    }

    private void ResetSettingsModel()
    {
        _request = SettingsState.Value.Config with { };

        _providers = SettingsState.Value.LoadedProviders.ToList();
        _providersHasChanged = false;

        StateHasChanged();
    }

    private void Save()
    {

        if (_providersHasChanged)
        {
            Dispatcher.Dispatch(new SettingsAction.Save(_request));
            Dispatcher.Dispatch(new SettingsAction.LoadProviders());
        }
        else
        {
            Dispatcher.Dispatch(new SettingsAction.Save(_request));
        }

        Close();
    }
}
