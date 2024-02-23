// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using EventLogExpert.UI.Options;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.Settings;
using Fluxor;
using Microsoft.AspNetCore.Components;
using System.IO.Compression;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components;

public sealed partial class SettingsModal
{
    private readonly List<(string name, bool isEnabled, bool hasChanged)> _databases = [];

    private SettingsModel _request = new();

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IState<EventLogState> EventLogState { get; init; } = null!;

    [Inject] private FileLocationOptions FileLocationOptions { get; init; } = null!;

    [Inject] private IState<SettingsState> SettingsState { get; init; } = null!;

    protected override void OnInitialized()
    {
        SubscribeToAction<SettingsAction.OpenMenu>(action => Load().AndForget());

        base.OnInitialized();
    }

    private async void ImportDatabase()
    {
        PickOptions options = new()
        {
            PickerTitle = "Please select a database file",
            FileTypes = new FilePickerFileType(
                new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, [".db", ".zip"] }
                })
        };

        try
        {
            var result = (await FilePicker.Default.PickMultipleAsync(options)).ToList();

            // User canceled or no files selected
            if (result.Count <= 0) { return; }

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

            var message = result.Count > 1 ?
                $"{result.Count} databases have successfully been imported" :
                $"{result.First().FileName} has successfully been imported";

            await AlertDialogService.ShowAlert("Import Successful", message, "OK");

            Close().AndForget();
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

    private async Task Load()
    {
        _request = SettingsState.Value.Config with { };

        _databases.Clear();

        foreach (var database in SettingsState.Value.LoadedDatabases)
        {
            _databases.Add((database, true, false));
        }

        foreach (var database in SettingsState.Value.DisabledDatabases)
        {
            var index = _databases.FindIndex(x => string.Equals(x.name, database));

            if (index < 0)
            {
                _databases.Add((database, false, false));
            }
            else
            {
                _databases[index] = (database, false, false);
            }
        }

        await InvokeAsync(StateHasChanged);

        await Open();
    }

    private async Task ReloadOpenLogs()
    {
        if (EventLogState.Value.ActiveLogs.IsEmpty) { return; }

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

    private async Task Save()
    {
        if (!SettingsState.Value.Config.Equals(_request))
        {
            Dispatcher.Dispatch(new SettingsAction.Save(_request));
        }

        if (_databases.Any(database => database.hasChanged))
        {
            Dispatcher.Dispatch(
                new SettingsAction.SaveDisabledDatabases(
                    _databases
                        .Where(db => !db.isEnabled)
                        .Select(db => db.name)
                        .ToList()));

            Dispatcher.Dispatch(new SettingsAction.LoadDatabases());

            await ReloadOpenLogs();
        }

        await Close();
    }

    private void ToggleDatabase(string database)
    {
        var index = _databases.FindIndex(x => string.Equals(x.name, database));

        if (index < 0) { return; }

        var db = _databases[index];

        _databases[index] = (db.name, !db.isEnabled, !db.hasChanged);
    }
}
