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
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components;

public partial class SettingsModal : IDisposable
{
    private readonly Dictionary<string, bool> _databases = new();

    private bool _hasDatabasesChanged = false;
    private SettingsModel _request = new();

    [Inject] private IActionSubscriber ActionSubscriber { get; set; } = null!;

    [Inject] private IAlertDialogService AlertDialogService { get; set; } = null!;

    [Inject] private IDispatcher Dispatcher { get; set; } = null!;

    [Inject] private IState<EventLogState> EventLogState { get; set; } = null!;

    [Inject] private FileLocationOptions FileLocationOptions { get; set; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;

    [SuppressMessage("Usage",
        "CA1816:Dispose methods should call SuppressFinalize",
        Justification = "Not a redundant GC call since we are just calling unsubscribe")]
    public void Dispose() => ActionSubscriber.UnsubscribeFromAllActions(this);

    protected override void OnInitialized()
    {
        ActionSubscriber.SubscribeToAction<SettingsAction.OpenMenu>(this, action => ResetSettingsModel().AndForget());

        base.OnInitializedAsync();
    }

    private async void Close() => await JSRuntime.InvokeVoidAsync("closeSettingsModal");

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

            var message = result.Count > 1 ?
                $"{result.Count} databases have successfully been imported" :
                $"{result.First().FileName} has successfully been imported";

            await AlertDialogService.ShowAlert("Import Successful", message, "OK");

            Close();
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
        if (!EventLogState.Value.ActiveLogs.Any()) { return; }

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

    private async Task ResetSettingsModel()
    {
        _request = SettingsState.Value.Config with { DisabledDatabases = new List<string>() };

        _databases.Clear();

        foreach (var database in SettingsState.Value.LoadedDatabases)
        {
            _databases.TryAdd(database, SettingsState.Value.Config.DisabledDatabases.Contains(database) is false);
        }

        foreach (var database in SettingsState.Value.Config.DisabledDatabases)
        {
            _databases.TryAdd(database, false);
        }

        await InvokeAsync(StateHasChanged);
    }

    private async void Save()
    {
        if (_hasDatabasesChanged)
        {
            Dispatcher.Dispatch(new SettingsAction.Save(_request));
            Dispatcher.Dispatch(new SettingsAction.LoadDatabases());

            await ReloadOpenLogs();
        }
        else
        {
            Dispatcher.Dispatch(new SettingsAction.Save(_request));
        }

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
