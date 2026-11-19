// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Database;
using EventLogExpert.UI.Banner;
using EventLogExpert.UI.Modal;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Database;

public sealed partial class DatabaseRecoveryModal : ModalBase<bool>
{
    private readonly HashSet<string> _failedFileNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RecoveryAction> _selectedActions = new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<DatabaseEntry> _entries = [];
    private bool _isApplying;

    private enum RecoveryAction
    {
        Restore,
        Delete
    }

    protected override ModalScope Scope => ModalScope.Critical;

    [Inject] private IDatabaseService DatabaseService { get; init; } = null!;

    [Inject] private IErrorBannerService ErrorBannerService { get; init; } = null!;

    [Inject] private IStringLocalizer<SharedResource> Localizer { get; init; } = null!;

    [Inject] private ITraceLogger TraceLogger { get; init; } = null!;

    protected override async ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing)
        {
            DatabaseService.EntriesChanged -= OnEntriesChanged;
        }

        await base.DisposeAsyncCore(disposing);
    }

    protected override Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && _entries.Count == 0)
        {
            _ = InvokeAsync(async () =>
            {
                try
                {
                    await CompleteAsync(false);
                }
                catch (Exception unexpected)
                {
                    TraceLogger.Error(
                        $"{nameof(DatabaseRecoveryModal)} deferred empty-set dismiss threw: {unexpected}");
                }
            });
        }

        return base.OnAfterRenderAsync(firstRender);
    }

    protected override void OnInitialized()
    {
        DatabaseService.EntriesChanged += OnEntriesChanged;
        RefreshEntriesFromService();

        base.OnInitialized();
    }

    protected override Task<bool> OnRequestCloseAsync(ModalCloseRequest request) =>
        Task.FromResult(!_isApplying);

    private async Task ApplyAsync()
    {
        if (_isApplying) { return; }

        _isApplying = true;

        var rowsToProcess = new List<(DatabaseEntry Entry, RecoveryAction Action)>();
        var changedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in _entries)
        {
            if (_selectedActions.TryGetValue(entry.FileName, out var action))
            {
                rowsToProcess.Add((entry, action));
            }
        }

        try
        {
            foreach (var (rowEntry, action) in rowsToProcess)
            {
                var fileName = rowEntry.FileName;

                var liveEntry = DatabaseService.Entries.FirstOrDefault(entry =>
                    string.Equals(entry.FileName, fileName, StringComparison.OrdinalIgnoreCase) &&
                    entry.BackupExists);

                if (liveEntry is null) { continue; }

                _failedFileNames.Remove(fileName);

                bool success;
                bool backupRetained = false;

                try
                {
                    switch (action)
                    {
                        case RecoveryAction.Restore:
                            var restoreResult = await DatabaseService.RestoreFromBackupAsync(fileName);
                            success = restoreResult == DatabaseRestoreResult.Restored;
                            backupRetained = restoreResult == DatabaseRestoreResult.RestoredBackupRetained;

                            break;
                        case RecoveryAction.Delete:
                            success = await DatabaseService.DeleteEntryWithBackupAsync(fileName);

                            break;
                        default:
                            success = false;

                            break;
                    }
                }
                catch (InvalidOperationException invalidOperation)
                {
                    TraceLogger.Warning(
                        $"{nameof(DatabaseRecoveryModal)}: skipped '{fileName}' ({action}) - {invalidOperation.Message}");
                    continue;
                }
                catch (Exception unexpected)
                {
                    TraceLogger.Error(
                        $"{nameof(DatabaseRecoveryModal)}.{nameof(ApplyAsync)} threw for '{fileName}' ({action}): {unexpected}");
                    success = false;
                }

                if (success || backupRetained)
                {
                    changedFileNames.Add(fileName);
                }

                if (backupRetained)
                {
                    _failedFileNames.Add(fileName);

                    ErrorBannerService.ReportError(new Preformatted(
                        Localizer["Banner_Recovery_BackupRetained_Title"],
                        Localizer["Banner_Recovery_BackupRetained", fileName]));
                }
                else if (!success)
                {
                    _failedFileNames.Add(fileName);

                    var message = action == RecoveryAction.Restore ?
                        Localizer["Banner_Recovery_Failed_Restore", fileName] :
                        Localizer["Banner_Recovery_Failed_Delete", fileName];

                    ErrorBannerService.ReportError(new Preformatted(Localizer["Banner_Recovery_Failed_Title"], message));
                }
            }
        }
        finally
        {
            _isApplying = false;

            foreach (var changedFileName in changedFileNames)
            {
                _selectedActions.Remove(changedFileName);
            }

            await HandleEntriesChangedAsync();
        }
    }

    private void DeleteAll()
    {
        if (_isApplying) { return; }

        foreach (var entry in _entries)
        {
            _selectedActions[entry.FileName] = RecoveryAction.Delete;
        }
    }

    private async Task HandleEntriesChangedAsync()
    {
        if (_isApplying || IsDisposed) { return; }

        try
        {
            RefreshEntriesFromService();

            if (_entries.Count == 0)
            {
                await CompleteAsync(false);

                return;
            }

            StateHasChanged();
        }
        catch (Exception unexpected)
        {
            TraceLogger.Error(
                $"{nameof(DatabaseRecoveryModal)}.{nameof(HandleEntriesChangedAsync)} threw: {unexpected}");
        }
    }

    private void OnEntriesChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) { return; }

        _ = InvokeAsync(HandleEntriesChangedAsync);
    }

    private void RefreshEntriesFromService()
    {
        var snapshot = DatabaseService.Entries
            .Where(entry => entry.BackupExists)
            .ToArray();

        _entries = snapshot;

        var presentFileNames = new HashSet<string>(
            snapshot.Select(entry => entry.FileName),
            StringComparer.OrdinalIgnoreCase);

        foreach (var entry in snapshot)
        {
            _selectedActions.TryAdd(entry.FileName, RecoveryAction.Restore);
        }

        var staleSelections = _selectedActions.Keys
            .Where(name => !presentFileNames.Contains(name))
            .ToArray();

        foreach (var stale in staleSelections)
        {
            _selectedActions.Remove(stale);
        }

        _failedFileNames.RemoveWhere(name => !presentFileNames.Contains(name));
    }

    private void RestoreAll()
    {
        if (_isApplying) { return; }

        foreach (var entry in _entries)
        {
            _selectedActions[entry.FileName] = RecoveryAction.Restore;
        }
    }

    private void SelectAction(string fileName, RecoveryAction action) => _selectedActions[fileName] = action;
}
