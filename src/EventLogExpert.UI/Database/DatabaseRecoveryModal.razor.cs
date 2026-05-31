// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.UI.Modal;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Database;

public sealed partial class DatabaseRecoveryModal : ModalBase<bool>
{
    private readonly HashSet<string> _failedFileNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RecoveryAction> _selectedActions = new(StringComparer.OrdinalIgnoreCase);

    private bool _disposed;
    private IReadOnlyList<DatabaseEntry> _entries = [];
    private bool _isApplying;

    private enum RecoveryAction
    {
        Restore,
        Delete
    }

    protected override ModalScope Scope => ModalScope.Critical;

    [Inject] private IErrorBannerService ErrorBannerService { get; init; } = null!;

    [Inject] private IDatabaseService DatabaseService { get; init; } = null!;

    [Inject] private ITraceLogger TraceLogger { get; init; } = null!;

    protected override async ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
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
        StateHasChanged();

        var rowsToProcess = _entries.ToArray();

        try
        {
            foreach (var rowEntry in rowsToProcess)
            {
                var fileName = rowEntry.FileName;

                if (!_selectedActions.TryGetValue(fileName, out var action)) { continue; }

                var liveEntry = DatabaseService.Entries.FirstOrDefault(entry =>
                    string.Equals(entry.FileName, fileName, StringComparison.OrdinalIgnoreCase) &&
                    entry.BackupExists);

                if (liveEntry is null) { continue; }

                _failedFileNames.Remove(fileName);

                bool success;

                try
                {
                    success = action switch
                    {
                        RecoveryAction.Restore => await DatabaseService.RestoreFromBackupAsync(fileName),
                        RecoveryAction.Delete => await DatabaseService.DeleteEntryWithBackupAsync(fileName),
                        _ => false
                    };
                }
                catch (InvalidOperationException invalidOperation)
                {
                    TraceLogger.Warning(
                        $"{nameof(DatabaseRecoveryModal)}: skipped '{fileName}' ({action}) — {invalidOperation.Message}");
                    continue;
                }
                catch (Exception unexpected)
                {
                    TraceLogger.Error(
                        $"{nameof(DatabaseRecoveryModal)}.{nameof(ApplyAsync)} threw for '{fileName}' ({action}): {unexpected}");
                    success = false;
                }

                if (!success)
                {
                    _failedFileNames.Add(fileName);

                    var message = action == RecoveryAction.Restore
                        ? $"Failed to restore '{fileName}' from backup."
                        : $"Failed to delete '{fileName}'.";

                    ErrorBannerService.ReportError("Database recovery failed", message);
                }
            }
        }
        finally
        {
            _isApplying = false;

            if (!_disposed) { StateHasChanged(); }
        }
    }

    private void DeleteAll()
    {
        foreach (var entry in _entries)
        {
            _selectedActions[entry.FileName] = RecoveryAction.Delete;
        }
    }

    private async Task HandleEntriesChangedAsync()
    {
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

    private void OnEntriesChanged(object? sender, EventArgs e) =>
        _ = InvokeAsync(HandleEntriesChangedAsync);

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
        foreach (var entry in _entries)
        {
            _selectedActions[entry.FileName] = RecoveryAction.Restore;
        }
    }

    private void SelectAction(string fileName, RecoveryAction action) =>
        _selectedActions[fileName] = action;
}
