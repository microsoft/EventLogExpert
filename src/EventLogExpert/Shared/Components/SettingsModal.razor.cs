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

public sealed partial class SettingsModal : IDisposable
{
    private readonly Dictionary<string, bool> _databases = [];

    private bool _hasDatabasesChanged = false;
    private SettingsModel _request = new();

    [Inject] private IActionSubscriber ActionSubscriber { get; init; } = null!;

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IState<EventLogState> EventLogState { get; init; } = null!;

    [Inject] private FileLocationOptions FileLocationOptions { get; init; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    public void Dispose() => ActionSubscriber.UnsubscribeFromAllActions(this);

    protected override void OnInitialized()
    {
        ActionSubscriber.SubscribeToAction<SettingsAction.OpenMenu>(this, action => Open().AndForget());

        base.OnInitialized();
    }

    private async Task Close() => await JSRuntime.InvokeVoidAsync("closeSettingsModal");

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

    private async Task Open()
    {
        _request = SettingsState.Value.Config with
        {
            DisabledDatabases = SettingsState.Value.Config.DisabledDatabases.Select(x => x).ToList()
        };

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

        await OpenModal();
    }

    private async Task OpenModal() => await JSRuntime.InvokeVoidAsync("openSettingsModal");

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

        await Close();
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
