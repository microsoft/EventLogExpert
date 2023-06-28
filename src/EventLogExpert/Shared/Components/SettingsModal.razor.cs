// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using EventLogExpert.UI.Options;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.Settings;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.IO.Compression;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components;

public partial class SettingsModal
{
    private Dictionary<string, bool> _databases = new();
    private bool _hasDatabasesChanged;
    private SettingsModel _request = new();

    [Inject] private IAlertDialogService AlertDialogService { get; set; } = null!;

    [Inject] private IDispatcher Dispatcher { get; set; } = null!;

    [Inject] private IState<EventLogState> EventLogState { get; set; } = null!;

    [Inject] private FileLocationOptions FileLocationOptions { get; set; } = null!;

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

            Directory.CreateDirectory(FileLocationOptions.DatabasePath);

            foreach (var item in result)
            {
                var destination = Path.Join(FileLocationOptions.DatabasePath, item.FileName);
                File.Copy(item.FullPath, destination, true);

                if (Path.GetExtension(destination) == ".zip")
                {
                    ZipFile.ExtractToDirectory(destination, FileLocationOptions.DatabasePath, overwriteFiles: true);
                    File.Delete(destination);
                }
            }
        }
        catch (Exception ex)
        {
            await AlertDialogService.ShowAlert("Import Failed",
                $"An exception occurred while importing provider databases: {ex.Message}",
                "OK");

            return;
        }

        Dispatcher.Dispatch(new SettingsAction.LoadDatabases());

        await ReloadOpenLogs();
    }

    private async Task ReloadOpenLogs()
    {
        if (!EventLogState.Value.ActiveLogs.Any())
        {
            return;
        }

        bool answer = await AlertDialogService.ShowAlert("Reload Open Logs Now?",
            "In order to use these databases, all currently open logs must be reopened. Would you like to reopen all open logs now?",
            "Yes", "No");

        if (!answer) { return; }

        var logsToReopen = EventLogState.Value.ActiveLogs.Values;

        Dispatcher.Dispatch(new EventLogAction.CloseAll());

        foreach (var log in logsToReopen)
        {
            Dispatcher.Dispatch(new EventLogAction.OpenLog(log.Name, log.Type));
        }
    }

    private async void RemoveDatabase(string database)
    {
        bool answer = await AlertDialogService.ShowAlert("Remove Database",
            "Are you sure you want to remove this database?",
            "Yes", "No");

        if (!answer) { return; }

        try
        {
            var destination = Path.Join(FileLocationOptions.DatabasePath, database);
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

        foreach (var database in SettingsState.Value.LoadedDatabases)
        {
            _databases.TryAdd(database, _request.DisabledDatabases.Contains(database) is false);
        }

        foreach (var database in SettingsState.Value.Config.DisabledDatabases)
        {
            _databases.TryAdd(database, false);
        }

        StateHasChanged();
    }

    private async void Save()
    {
        if (_hasDatabasesChanged)
        {
            Dispatcher.Dispatch(new SettingsAction.Save(_request));
            Dispatcher.Dispatch(new SettingsAction.LoadDatabases());
        }
        else
        {
            Dispatcher.Dispatch(new SettingsAction.Save(_request));
        }

        await ReloadOpenLogs();

        Close();
    }

    private void ToggleDatabase(string database)
    {
        _databases[database] = !_databases[database];

        bool isDisabled = _request.DisabledDatabases.Contains(database);

        switch (_databases[database])
        {
            case true when isDisabled : 
                _request.DisabledDatabases.Remove(database);
                _hasDatabasesChanged = true;
                break;
            case false when !isDisabled : 
                _request.DisabledDatabases.Add(database);
                _hasDatabasesChanged = true;
                break;
        }
    }
}
