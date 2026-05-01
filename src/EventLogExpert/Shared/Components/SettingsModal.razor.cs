// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Shared.Base;
using EventLogExpert.UI;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.EventLog;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components;

public sealed partial class SettingsModal : ModalBase<bool>
{
    private readonly Dictionary<string, bool> _pendingToggles = new(StringComparer.OrdinalIgnoreCase);

    private CopyType _copyType;
    private bool _databaseStateChanged;
    private bool _isPreReleaseEnabled;
    private LogLevel _logLevel;
    private bool _showDisplayPaneOnSelectionChange;
    private Theme _theme;
    private string _timeZoneId = string.Empty;

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IDatabaseService DatabaseService { get; init; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IState<EventLogState> EventLogState { get; init; } = null!;

    [Inject] private ISettingsService Settings { get; init; } = null!;

    protected override async ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing)
        {
            DatabaseService.EntriesChanged -= OnDatabaseEntriesChanged;
        }

        await base.DisposeAsyncCore(disposing);
    }

    protected override async Task OnClosingAsync()
    {
        if (_databaseStateChanged)
        {
            await ReloadOpenLogs();
            _databaseStateChanged = false;
        }
    }

    protected override void OnInitialized()
    {
        LoadFromSettings();
        DatabaseService.EntriesChanged += OnDatabaseEntriesChanged;

        base.OnInitialized();
    }

    protected override async Task OnSaveAsync()
    {
        await ApplyPendingToggles();

        Settings.CopyType = _copyType;
        Settings.IsPreReleaseEnabled = _isPreReleaseEnabled;
        Settings.LogLevel = _logLevel;
        Settings.ShowDisplayPaneOnSelectionChange = _showDisplayPaneOnSelectionChange;
        Settings.Theme = _theme;
        Settings.TimeZoneId = _timeZoneId;

        await CompleteAsync(true);
    }

    private static (string Title, string Message) BuildImportSummary(ImportResult importResult)
    {
        var imported = importResult.Imported;
        var failures = importResult.Failures;

        if (failures.Count == 0)
        {
            var successMessage = imported > 1
                ? $"{imported} databases have successfully been imported"
                : "1 database has successfully been imported";

            return ("Import Successful", successMessage);
        }

        var failureLines = string.Join(Environment.NewLine,
            failures.Select(failure => $"\u2022 {failure.FileName}: {failure.Reason}"));

        if (imported == 0)
        {
            return ("Import Failed", $"No databases were imported.{Environment.NewLine}{Environment.NewLine}Failed:{Environment.NewLine}{failureLines}");
        }

        var partialMessage = imported > 1
            ? $"{imported} databases imported successfully."
            : "1 database imported successfully.";

        return ("Import Completed with Errors",
            $"{partialMessage}{Environment.NewLine}{Environment.NewLine}Failed:{Environment.NewLine}{failureLines}");
    }

    private async Task ApplyPendingToggles()
    {
        if (_pendingToggles.Count == 0) { return; }

        var toApply = _pendingToggles.ToArray();
        _pendingToggles.Clear();

        foreach (var (fileName, _) in toApply)
        {
            try
            {
                DatabaseService.Toggle(fileName);
            }
            catch (Exception ex)
            {
                await AlertDialogService.ShowAlert("Failed to Update Database",
                    $"An exception occurred while updating '{fileName}': {ex.Message}",
                    "OK");
            }
        }
    }

    private bool GetEffectiveEnabled(DatabaseEntry entry) =>
        _pendingToggles.TryGetValue(entry.FileName, out var pending) ? pending : entry.IsEnabled;

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

            if (result.Length <= 0) { return; }

            var sourcePaths = result
                .Where(item => item is not null && !string.IsNullOrEmpty(item.FullPath))
                .Select(item => item!.FullPath)
                .ToList();

            var importResult = await DatabaseService.ImportAsync(sourcePaths);

            if (importResult.Imported == 0 && importResult.Failures.Count == 0)
            {
                await AlertDialogService.ShowAlert("Import Failed", "No valid database files were selected.", "OK");
                return;
            }

            var (title, message) = BuildImportSummary(importResult);
            await AlertDialogService.ShowAlert(title, message, "OK");

            if (importResult.Imported > 0)
            {
                await InvokeAsync(OnSaveAsync);
            }
        }
        catch (Exception ex)
        {
            await AlertDialogService.ShowAlert("Import Failed",
                $"An exception occurred while importing provider databases: {ex.Message}",
                "OK");
        }
    }

    private void LoadFromSettings()
    {
        _copyType = Settings.CopyType;
        _isPreReleaseEnabled = Settings.IsPreReleaseEnabled;
        _logLevel = Settings.LogLevel;
        _showDisplayPaneOnSelectionChange = Settings.ShowDisplayPaneOnSelectionChange;
        _theme = Settings.Theme;
        _timeZoneId = Settings.TimeZoneId;
    }

    private void OnDatabaseEntriesChanged(object? sender, EventArgs e)
    {
        _databaseStateChanged = true;
        _ = InvokeAsync(StateHasChanged);
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

    private async Task RemoveDatabase(string fileName)
    {
        var confirmed = await AlertDialogService.ShowAlert("Remove Database",
            $"Are you sure you want to remove {fileName}?",
            "Remove",
            "Cancel");

        if (!confirmed) { return; }

        try
        {
            DatabaseService.Remove(fileName);
            _pendingToggles.Remove(fileName);
        }
        catch (Exception ex)
        {
            await AlertDialogService.ShowAlert("Failed to Remove Database",
                $"An exception occurred while removing provider databases: {ex.Message}",
                "OK");
        }
    }

    private void ToggleDatabase(string fileName)
    {
        var entry = DatabaseService.Entries.FirstOrDefault(
            e => string.Equals(e.FileName, fileName, StringComparison.OrdinalIgnoreCase));

        if (entry is null) { return; }

        var newValue = !GetEffectiveEnabled(entry);

        if (newValue == entry.IsEnabled)
        {
            _pendingToggles.Remove(fileName);
        }
        else
        {
            _pendingToggles[fileName] = newValue;
        }
    }
}
