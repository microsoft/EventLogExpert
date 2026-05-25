// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.Database.Upgrade;
using EventLogExpert.Runtime.DetailsPane;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.Modal;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System.Text;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.UI.Settings;

public sealed partial class SettingsModal : ModalBase<bool>
{
    private readonly HashSet<string> _entriesUpgrading = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _pendingToggles = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _classificationCts;
    private EventCopyFormat _copyFormat;
    private bool _databaseStateChanged;
    private volatile bool _disposed;
    private bool _isPreReleaseEnabled;
    private LogLevel _logLevel;
    private bool _showDisplayPaneOnSelectionChange;
    private Theme _theme;
    private string _timeZoneId = string.Empty;

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IBannerService BannerService { get; init; } = null!;

    [Inject] private IDatabaseService DatabaseService { get; init; } = null!;

    [Inject] private IDetailsPanePreferencesProvider DetailsPanePreferences { get; init; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IEventLogCommands EventLogCommands { get; init; } = null!;

    [Inject] private IState<EventLogState> EventLogState { get; init; } = null!;

    [Inject] private IFilePickerService FilePickerService { get; init; } = null!;

    private bool IsAnyUpgradeInFlight => _entriesUpgrading.Count > 0 || BannerService.SettingsProgress is not null;

    private bool IsClassificationPending => !DatabaseService.InitialClassificationTask.IsCompleted;

    private bool IsCloseBlocked => IsAnyUpgradeInFlight;

    [Inject] private ILogReloadCoordinator LogReloadCoordinator { get; init; } = null!;

    [Inject] private ISettingsService Settings { get; init; } = null!;

    protected override async ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            _classificationCts?.CancelAsync();
            _classificationCts?.Dispose();
            _classificationCts = null;
            DatabaseService.EntriesChanged -= OnDatabaseEntriesChanged;
            BannerService.StateChanged -= OnBannerStateChanged;
        }

        await base.DisposeAsyncCore(disposing);
    }

    protected override Task<bool> OnRequestCloseAsync(ModalCloseRequest request) =>
        Task.FromResult(!IsCloseBlocked);

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
        BannerService.StateChanged += OnBannerStateChanged;

        if (!DatabaseService.InitialClassificationTask.IsCompleted)
        {
            _classificationCts = new CancellationTokenSource();
            _ = ObserveClassificationCompletionAsync(_classificationCts.Token);
        }

        base.OnInitialized();
    }

    protected override async Task OnSaveAsync()
    {
        if (IsCloseBlocked) { return; }

        await ApplyPendingToggles();

        Settings.CopyFormat = _copyFormat;
        Settings.IsPreReleaseEnabled = _isPreReleaseEnabled;
        Settings.LogLevel = _logLevel;
        DetailsPanePreferences.DisplayPaneSelectionPreference = _showDisplayPaneOnSelectionChange;
        Settings.Theme = _theme;
        Settings.TimeZoneId = _timeZoneId;

        await CompleteAsync(true);
    }

    private static void AppendImportFailureSection(
        StringBuilder builder,
        string heading,
        IReadOnlyList<ImportFailure> entries)
    {
        builder.AppendLine();
        builder.AppendLine();
        builder.Append(heading);

        foreach (var entry in entries)
        {
            builder.AppendLine();
            builder.Append("\u2022 ");
            builder.Append(entry.FileName);
            builder.Append(": ");
            builder.Append(entry.Reason);
        }
    }

    private static (string Title, string Message) BuildImportSummary(ImportResult importResult)
    {
        var imported = importResult.Imported;
        var failures = importResult.Failures;
        var upgradeFailures = importResult.UpgradeFailures;

        if (failures.Count == 0 && upgradeFailures.Count == 0)
        {
            if (imported == 0) { return ("Import Successful", "No databases were imported."); }

            var successMessage = imported > 1
                ? $"{imported} databases have successfully been imported"
                : "1 database has successfully been imported";

            return ("Import Successful", successMessage);
        }

        var detail = new StringBuilder();

        if (failures.Count > 0) { AppendImportFailureSection(detail, "Failed:", failures); }

        if (upgradeFailures.Count > 0) { AppendImportFailureSection(detail, "Upgrade failures:", upgradeFailures); }

        if (imported == 0)
        {
            return ("Import Failed", $"No databases were imported.{detail}");
        }

        var partialMessage = imported > 1
            ? $"{imported} databases imported successfully."
            : "1 database imported successfully.";

        return ("Import Completed with Errors", $"{partialMessage}{detail}");
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
        try
        {
            var sourcePaths = await FilePickerService.PickMultipleAsync(
                "Please select database files to import",
                FilePickerFileTypes.Database);

            if (sourcePaths.Count == 0) { return; }

            var skipFileNames = await ResolveImportConflictsAsync(sourcePaths, CancellationToken.None);

            var importResult = await DatabaseService.ImportAsync(sourcePaths, skipFileNames, CancellationToken.None);

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
        _copyFormat = Settings.CopyFormat;
        _isPreReleaseEnabled = Settings.IsPreReleaseEnabled;
        _logLevel = Settings.LogLevel;
        _showDisplayPaneOnSelectionChange = DetailsPanePreferences.DisplayPaneSelectionPreference;
        _theme = Settings.Theme;
        _timeZoneId = Settings.TimeZoneId;
    }

    private async Task ObserveClassificationCompletionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await DatabaseService.InitialClassificationTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            // InitialClassificationTask is contractually never-faulting (DatabaseService.cs);
            // defensive try/catch so a future contract drift cannot orphan this fire-and-forget task.
        }

        if (_disposed) { return; }

        try
        {
            await InvokeAsync(StateHasChanged);
        }
        catch (ObjectDisposedException)
        {
            // Modal was torn down between the _disposed check and the dispatcher call; safe to ignore.
        }
    }

    private void OnBannerStateChanged() => _ = InvokeAsync(StateHasChanged);

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

        Dispatcher.Dispatch(new CloseAllLogsAction());

        foreach (var log in logsToReopen)
        {
            EventLogCommands.OpenLog(log.Name, log.Type);
        }
    }

    private async Task RemoveDatabase(DatabaseEntry entry)
    {
        var fileName = entry.FileName;
        var isReadyAndEnabled = entry is { IsEnabled: true, Status: DatabaseStatus.Ready };
        var hasOpenLogs = !EventLogState.Value.ActiveLogs.IsEmpty;

        var message = (isReadyAndEnabled && hasOpenLogs)
            ? $"{fileName} is currently enabled. Removing will close and reopen any affected log views. Are you sure?"
            : $"Are you sure you want to remove {fileName}?";

        var confirmed = await AlertDialogService.ShowAlert("Remove Database",
            message,
            "Remove",
            "Cancel");

        if (!confirmed) { return; }

        // Sink populated incrementally as the coordinator closes each log; the finally
        // block reopens every log that successfully closed regardless of whether the
        // delete succeeded or threw downstream.
        var snapshot = new LogReopenSnapshot();

        try
        {
            await DatabaseService.RemoveAsync(
                fileName,
                ct =>
                    LogReloadCoordinator.PrepareForDatabaseRemovalAsync(snapshot, ct));
        }
        catch (Exception ex)
        {
            await AlertDialogService.ShowAlert("Failed to Remove Database",
                $"An exception occurred while removing provider databases: {ex.Message}",
                "OK");
        }
        finally
        {
            if (snapshot.Items.Count > 0)
            {
                LogReloadCoordinator.ReopenAfterDatabaseRemoval(snapshot.Items);

                // Coordinator just rebuilt the resolver scopes for these logs against the
                // current enabled-database set, which subsumes any prior dirty state from
                // earlier modal actions. Suppress the modal-close global reload prompt so
                // the user isn't asked twice. When no logs were reopened we leave the flag
                // alone so unrelated prior mutations (e.g., a toggle earlier in the same
                // modal session) still trigger their pending reload on close.
                _databaseStateChanged = false;
            }

            _pendingToggles.Remove(fileName);
        }
    }

    private async Task<IReadOnlySet<string>> ResolveImportConflictsAsync(
        IReadOnlyList<string> sourcePaths,
        CancellationToken cancellationToken)
    {
        var existingNames = DatabaseService.Entries
            .Select(entry => entry.FileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (existingNames.Count == 0) { return new HashSet<string>(StringComparer.OrdinalIgnoreCase); }

        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sourcePath in sourcePaths)
        {
            if (string.IsNullOrEmpty(sourcePath)) { continue; }

            IReadOnlyList<string> candidateNames;

            if (Path.GetExtension(sourcePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                candidateNames = await DatabaseService.EnumerateZipDbEntryNamesAsync(sourcePath, cancellationToken);
            }
            else
            {
                var name = Path.GetFileName(sourcePath);
                candidateNames = string.IsNullOrEmpty(name) ? [] : [name];
            }

            foreach (var candidateName in candidateNames)
            {
                if (string.IsNullOrEmpty(candidateName)) { continue; }

                if (!existingNames.Contains(candidateName)) { continue; }

                if (!resolved.Add(candidateName)) { continue; }

                var overwrite = await AlertDialogService.ShowAlert(
                    "Database already exists",
                    $"{candidateName} already exists. Overwrite?",
                    "Overwrite",
                    "Skip");

                if (!overwrite) { skip.Add(candidateName); }
            }
        }

        return skip;
    }

    private void ToggleDatabase(string fileName)
    {
        var entry = DatabaseService.Entries.FirstOrDefault(e =>
            string.Equals(e.FileName, fileName, StringComparison.OrdinalIgnoreCase));

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

    private async Task UpgradeEntry(string fileName)
    {
        if (IsAnyUpgradeInFlight && !_entriesUpgrading.Contains(fileName)) { return; }

        if (!_entriesUpgrading.Add(fileName)) { return; }

        try
        {
            UpgradeBatchResult result;

            try
            {
                result = await DatabaseService.UpgradeBatchAsync(
                    [fileName],
                    UpgradeProgressScope.SettingsTriggered);
            }
            catch (Exception ex)
            {
                if (_disposed) { return; }

                try
                {
                    await AlertDialogService.ShowAlert("Database Upgrade Failed",
                        $"An exception occurred while upgrading '{fileName}': {ex.Message}",
                        "OK");
                }
                catch (ObjectDisposedException)
                {
                    // Alert dialog service was torn down with the modal; nothing to surface to.
                }

                return;
            }

            if (_disposed) { return; }

            foreach (var failure in result.Failed)
            {
                try
                {
                    await AlertDialogService.ShowErrorAlert(
                        "Database Upgrade Failed",
                        $"Failed to upgrade '{failure.FileName}': {failure.Message}");
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }
        }
        finally
        {
            _entriesUpgrading.Remove(fileName);

            if (!_disposed)
            {
                try
                {
                    await InvokeAsync(StateHasChanged);
                }
                catch (ObjectDisposedException)
                {
                    // Modal was torn down between the _disposed check and the dispatcher call; safe to ignore.
                }
            }
        }
    }
}
