// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Shared.Base;
using EventLogExpert.UI;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Options;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store.EventLog;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System.IO.Compression;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components;

public sealed partial class SettingsModal : BaseModal<bool>
{
    private readonly List<(string name, bool isEnabled, bool hasChanged)> _databases = [];

    private CopyType _copyType;
    private bool _databaseRemoved;
    private bool _isPreReleaseEnabled;
    private LogLevel _logLevel;
    private bool _shouldReload;
    private bool _showDisplayPaneOnSelectionChange;
    private Theme _theme;
    private string _timeZoneId = string.Empty;

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IDatabaseService DatabaseService { get; init; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IState<EventLogState> EventLogState { get; init; } = null!;

    [Inject] private FileLocationOptions FileLocationOptions { get; init; } = null!;

    [Inject] private ISettingsService Settings { get; init; } = null!;

    protected override async Task OnClosingAsync()
    {
        if (_databaseRemoved)
        {
            DatabaseService.UpdateDisabledDatabases(_databases.Where(db => !db.isEnabled).Select(db => db.name));
        }

        if (_shouldReload)
        {
            await ReloadOpenLogs();
            _shouldReload = false;
        }
    }

    protected override void OnInitialized()
    {
        LoadFromSettings();

        base.OnInitialized();
    }

    protected override async Task OnSaveAsync()
    {
        Settings.CopyType = _copyType;
        Settings.IsPreReleaseEnabled = _isPreReleaseEnabled;
        Settings.LogLevel = _logLevel;
        Settings.ShowDisplayPaneOnSelectionChange = _showDisplayPaneOnSelectionChange;
        Settings.Theme = _theme;
        Settings.TimeZoneId = _timeZoneId;

        if (_databases.Any(database => database.hasChanged))
        {
            DatabaseService.UpdateDisabledDatabases(_databases.Where(db => !db.isEnabled).Select(db => db.name));

            _shouldReload = true;
        }

        await CompleteAsync(true);
    }

    private async Task ImportDatabase()
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
            var result = (await FilePicker.Default.PickMultipleAsync(options)).ToArray();

            // User canceled or no files selected
            if (result.Length <= 0) { return; }

            Directory.CreateDirectory(FileLocationOptions.DatabasePath);

            List<string> importedFiles = [];

            foreach (var item in result)
            {
                if (string.IsNullOrEmpty(item?.FileName) || string.IsNullOrEmpty(item.FullPath)) { continue; }

                var destination = Path.Join(FileLocationOptions.DatabasePath, item.FileName);
                File.Copy(item.FullPath, destination, true);

                importedFiles.Add(item.FileName);

                if (!Path.GetExtension(destination).Equals(".zip", StringComparison.OrdinalIgnoreCase)) { continue; }

                await ZipFile.ExtractToDirectoryAsync(destination, FileLocationOptions.DatabasePath, true);
                File.Delete(destination);
            }

            if (importedFiles.Count == 0)
            {
                await AlertDialogService.ShowAlert("Import Failed", "No valid database files were selected.", "OK");
                return;
            }

            var message = importedFiles.Count > 1 ?
                $"{importedFiles.Count} databases have successfully been imported" :
                $"{importedFiles[0]} has successfully been imported";

            await AlertDialogService.ShowAlert("Import Successful", message, "OK");

            await InvokeAsync(() => CompleteAsync(true));
        }
        catch (Exception ex)
        {
            await AlertDialogService.ShowAlert("Import Failed",
                $"An exception occurred while importing provider databases: {ex.Message}",
                "OK");

            return;
        }

        DatabaseService.LoadDatabases();

        await ReloadOpenLogs();
    }

    private void LoadFromSettings()
    {
        _copyType = Settings.CopyType;
        _isPreReleaseEnabled = Settings.IsPreReleaseEnabled;
        _logLevel = Settings.LogLevel;
        _showDisplayPaneOnSelectionChange = Settings.ShowDisplayPaneOnSelectionChange;
        _theme = Settings.Theme;
        _timeZoneId = Settings.TimeZoneId;

        _databases.Clear();

        foreach (var database in DatabaseService.LoadedDatabases)
        {
            _databases.Add((database, true, false));
        }

        foreach (var database in DatabaseService.DisabledDatabases)
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
    }

    private async Task ReloadOpenLogs()
    {
        if (EventLogState.Value.ActiveLogs.IsEmpty) { return; }

        bool answer = await AlertDialogService.ShowAlert("Reload Open Logs Now?",
            "In order for these changes to take effect, all currently open logs must be reloaded. Would you like to reload all open logs now?",
            "Yes",
            "No");

        if (!answer) { return; }

        var logsToReopen = EventLogState.Value.ActiveLogs.Values;

        Dispatcher.Dispatch(new EventLogAction.CloseAll());

        foreach (var log in logsToReopen)
        {
            Dispatcher.Dispatch(new EventLogAction.OpenLog(log.Name, log.Type));
        }
    }

    private async Task RemoveDatabase(string name)
    {
        try
        {
            var databaseDirectory = new DirectoryInfo(FileLocationOptions.DatabasePath);

            // Using wildcard to also remove the db-shm and db-wal files
            foreach (var file in databaseDirectory.GetFiles($"{name}*"))
            {
                file.Delete();
            }

            _databases.RemoveAll(db => string.Equals(db.name, name));

            _databaseRemoved = true;
        }
        catch (Exception ex)
        {
            await AlertDialogService.ShowAlert("Failed to Remove Database",
                $"An exception occurred while removing provider databases: {ex.Message}",
                "OK");
        }
    }

    private void ToggleDatabase(string database)
    {
        var index = _databases.FindIndex(x => string.Equals(x.name, database));

        if (index < 0) { return; }

        var db = _databases[index];

        _databases[index] = (db.name, !db.isEnabled, !db.hasChanged);
    }
}
