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
using System.Text;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components;

public sealed partial class SettingsModal : ModalBase<bool>
{
    private readonly Dictionary<string, bool> _pendingToggles = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _classificationCts;
    private CopyType _copyType;
    private bool _databaseStateChanged;
    private volatile bool _disposed;
    private bool _isPreReleaseEnabled;
    private bool _isUpgradeInFlight;
    private LogLevel _logLevel;
    private bool _showDisplayPaneOnSelectionChange;
    private Theme _theme;
    private string _timeZoneId = string.Empty;

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IBannerService BannerService { get; init; } = null!;

    [Inject] private IDatabaseService DatabaseService { get; init; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IState<EventLogState> EventLogState { get; init; } = null!;

    /// <summary>
    ///     <c>true</c> while <see cref="IDatabaseService.InitialClassificationTask" /> has not completed. Drives the
    ///     "Classifying databases…" header indicator and disables every per-entry toggle so the user cannot trigger an
    ///     upgrade-required prompt against a status snapshot that is still being computed.
    /// </summary>
    private bool IsClassificationPending => !DatabaseService.InitialClassificationTask.IsCompleted;

    /// <summary>
    ///     <c>true</c> while a settings-triggered upgrade has been initiated from this modal but not yet observed to
    ///     drain through the BannerService progress slot. Covers the queued-but-not-yet-started window (
    ///     <see cref="IDatabaseService.UpgradeBatchAsync" /> can sit behind another batch before
    ///     <see cref="IDatabaseService.UpgradeBatchStarted" /> fires) where <see cref="IBannerService.SettingsProgress" />
    ///     alone would still be <c>null</c>. Composed with the banner slot so once the consumer picks up the batch and the
    ///     slot is populated, the predicate stays true through completion.
    /// </summary>
    private bool IsCloseBlocked => _isUpgradeInFlight || BannerService.SettingsProgress is not null;

    [Inject] private ISettingsService Settings { get; init; } = null!;

    protected override async ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            _classificationCts?.Cancel();
            _classificationCts?.Dispose();
            _classificationCts = null;
            DatabaseService.EntriesChanged -= OnDatabaseEntriesChanged;
            BannerService.StateChanged -= OnBannerStateChanged;
        }

        await base.DisposeAsyncCore(disposing);
    }

    protected override Task OnCancelAsync() =>
        IsCloseBlocked
            ? Task.CompletedTask
            : base.OnCancelAsync();

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
            // EntriesChanged fires synchronously inside ClassifyEntriesAsync (DatabaseService.cs:163-165),
            // before StartInitialClassificationAsync returns — so the entries-changed handler observes
            // InitialClassificationTask.IsCompleted == false. We need a separate continuation that runs
            // after the task object itself completes to flip IsClassificationPending in the UI.
            // The CTS lets us cancel the WaitAsync proxy on dispose so the captured `this` reference
            // is released even if the underlying classification task is still running, preventing the
            // modal from leaking across open/close cycles during a long classification.
            _classificationCts = new CancellationTokenSource();
            _ = ObserveClassificationCompletionAsync(_classificationCts.Token);
        }

        base.OnInitialized();
    }

    protected override async Task OnSaveAsync()
    {
        if (IsCloseBlocked) { return; }

        if (!await TryRunUpgradesAsync()) { return; }

        await ApplyPendingToggles();

        Settings.CopyType = _copyType;
        Settings.IsPreReleaseEnabled = _isPreReleaseEnabled;
        Settings.LogLevel = _logLevel;
        Settings.ShowDisplayPaneOnSelectionChange = _showDisplayPaneOnSelectionChange;
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
        _copyType = Settings.CopyType;
        _isPreReleaseEnabled = Settings.IsPreReleaseEnabled;
        _logLevel = Settings.LogLevel;
        _showDisplayPaneOnSelectionChange = Settings.ShowDisplayPaneOnSelectionChange;
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
            // Modal was disposed before classification completed; drop the captured `this` reference.
            return;
        }
        catch
        {
            // InitialClassificationTask is contractually never-faulting (DatabaseService.cs:1200-1212);
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

    /// <summary>
    ///     Identify pending enable-toggles whose target entry needs an upgrade first, prompt for confirmation, run the
    ///     batch upgrade through the settings-scope progress slot, then re-stage the entries that succeeded so they are
    ///     committed by the normal Save flow (no immediate <see cref="IDatabaseService.Toggle" /> calls). Surfaces per-entry
    ///     failures via <see cref="IAlertDialogService.ShowErrorAlert" />. Returns <c>false</c> when the user declines the
    ///     upgrade prompt, the upgrade throws, OR the user cancels the in-flight upgrade — the caller should abort the save in
    ///     any of those cases so the modal stays open and the user can either retry or click Exit to discard. The principle:
    ///     <see cref="IDatabaseService.Toggle" /> represents the "commit" step and must only run as part of a successful Save,
    ///     never as a side effect of the upgrade batch itself.
    /// </summary>
    private async Task<bool> TryRunUpgradesAsync()
    {
        if (_pendingToggles.Count == 0) { return true; }

        var entriesByName = DatabaseService.Entries.ToDictionary(
            entry => entry.FileName,
            StringComparer.OrdinalIgnoreCase);

        var fileNamesNeedingUpgrade = _pendingToggles
            .Where(pair =>
                pair.Value &&
                entriesByName.TryGetValue(pair.Key, out var entry) &&
                entry.Status is DatabaseStatus.UpgradeRequired or DatabaseStatus.UpgradeFailed)
            .Select(pair => pair.Key)
            .ToList();

        if (fileNamesNeedingUpgrade.Count == 0) { return true; }

        var confirmed = await AlertDialogService.ShowAlert(
            "Upgrade Required",
            fileNamesNeedingUpgrade.Count == 1
                ? "1 database requires an upgrade before it can be enabled. Upgrade now?"
                : $"{fileNamesNeedingUpgrade.Count} databases require an upgrade before they can be enabled. Upgrade now?",
            "Upgrade and enable",
            "Cancel");

        if (!confirmed) { return false; }

        foreach (var fileName in fileNamesNeedingUpgrade)
        {
            _pendingToggles.Remove(fileName);
        }

        UpgradeBatchResult result;

        _isUpgradeInFlight = true;

        try
        {
            result = await DatabaseService.UpgradeBatchAsync(
                fileNamesNeedingUpgrade,
                UpgradeProgressScope.SettingsTriggered);
        }
        catch (Exception ex)
        {
            // Restore every entry whose toggle we removed so the user can retry or click Exit to discard.
            // Without this restore, returning to OnSaveAsync would either commit a now-empty pending set
            // or (with the previous return-true behavior) silently drop the user's enable intent.
            foreach (var fileName in fileNamesNeedingUpgrade)
            {
                _pendingToggles[fileName] = true;
            }

            await AlertDialogService.ShowAlert("Database Upgrade Failed",
                $"An exception occurred while upgrading databases: {ex.Message}",
                "OK");

            return false;
        }
        finally
        {
            _isUpgradeInFlight = false;
        }

        // Re-stage successes as pending so ApplyPendingToggles enables them as part of the normal Save commit.
        // We do NOT call DatabaseService.Toggle here directly: per the modal contract, no toggle should take
        // effect until the user's Save click reaches CompleteAsync — otherwise a subsequent Cancel/Exit would
        // leave partial commits behind.
        foreach (var fileName in result.Succeeded)
        {
            _pendingToggles[fileName] = true;
        }

        foreach (var failure in result.Failed)
        {
            await AlertDialogService.ShowErrorAlert(
                "Database Upgrade Failed",
                $"Failed to upgrade '{failure.FileName}': {failure.Message}");
        }

        // Cancellation is the user's "abort" signal — restore the cancelled enable-toggles to pending so
        // they re-appear next Save, and abort this save so the modal stays open. Without this restore,
        // cancelled entries' toggles would be silently consumed and the user would have to re-toggle.
        if (result.Cancelled.Count <= 0) { return true; }

        foreach (var fileName in result.Cancelled)
        {
            _pendingToggles[fileName] = true;
        }

        return false;
    }
}
