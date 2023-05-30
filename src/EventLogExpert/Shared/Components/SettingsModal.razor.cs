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
    private List<string>? _databases;
    private bool _databasesHasChanged;
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

    private void DisableDatabase(string database)
    {
        _request.DisabledDatabases ??= new List<string>();

        _request.DisabledDatabases?.Add(database);
        _databases?.Remove(database);

        _databasesHasChanged = true;
    }

    private void EnableDatabase(string database)
    {
        _request.DisabledDatabases?.Remove(database);
        _databases?.Add(database);

        _databasesHasChanged = true;
    }

    private async void ImportDatabase()
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
            "In order to use these databases, a restart of the application is required. Would you like to restart now?",
            "Yes", "No");

        if (!answer)
        {
            Dispatcher.Dispatch(new SettingsAction.LoadDatabases());
            return;
        }

        uint res = NativeMethods.RegisterApplicationRestart(null, NativeMethods.RestartFlags.NONE);

        if (res == 0) { Application.Current.Quit(); }
    }

    private async void RemoveDatabase(string database)
    {
        bool answer = await Application.Current!.MainPage!.DisplayAlert("Remove Database",
            "Are you sure you want to remove this database?",
            "Yes", "No");

        if (!answer) { return; }

        try
        {
            var destination = Path.Join(Utils.DatabasePath, database);
            File.Delete(destination);
        }
        catch
        { // TODO: Log Error
            return;
        }

        Dispatcher.Dispatch(new SettingsAction.LoadDatabases());
    }

    private void ResetSettingsModel()
    {
        _request = SettingsState.Value.Config with { };

        _databases = SettingsState.Value.LoadedDatabases.ToList();
        _databasesHasChanged = false;

        StateHasChanged();
    }

    private void Save()
    {

        if (_databasesHasChanged)
        {
            Dispatcher.Dispatch(new SettingsAction.Save(_request));
            Dispatcher.Dispatch(new SettingsAction.LoadDatabases());
        }
        else
        {
            Dispatcher.Dispatch(new SettingsAction.Save(_request));
        }

        Close();
    }
}
