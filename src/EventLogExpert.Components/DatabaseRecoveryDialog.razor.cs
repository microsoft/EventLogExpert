// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Components.Base;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Components;

public sealed partial class DatabaseRecoveryDialog : ComponentBase, IAsyncDisposable
{
    private readonly HashSet<string> _failedFileNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RecoveryAction> _selectedActions = new(StringComparer.OrdinalIgnoreCase);

    private ModalChrome? _chrome;
    private IReadOnlyList<DatabaseEntry> _entries = [];
    private bool _isApplying;
    private bool _isDismissed;

    private enum RecoveryAction
    {
        Restore,
        Delete
    }

    [Parameter] public EventCallback OnDismissed { get; set; }

    [Inject] private IBannerService BannerService { get; init; } = null!;

    [Inject] private IDatabaseService DatabaseService { get; init; } = null!;

    [Inject] private ITraceLogger TraceLogger { get; init; } = null!;

    public ValueTask DisposeAsync()
    {
        DatabaseService.EntriesChanged -= OnEntriesChanged;
        return ValueTask.CompletedTask;
    }

    protected override Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && _entries.Count == 0)
        {
            _ = InvokeAsync(async () =>
            {
                try
                {
                    await DismissAsync();
                }
                catch (Exception unexpected)
                {
                    TraceLogger.Error(
                        $"DatabaseRecoveryDialog deferred empty-set dismiss threw: {unexpected}");
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
                    TraceLogger.Warn(
                        $"DatabaseRecoveryDialog: skipped '{fileName}' ({action}) — {invalidOperation.Message}");
                    continue;
                }
                catch (Exception unexpected)
                {
                    TraceLogger.Error(
                        $"DatabaseRecoveryDialog.ApplyAsync threw for '{fileName}' ({action}): {unexpected}");
                    success = false;
                }

                if (!success)
                {
                    _failedFileNames.Add(fileName);

                    var message = action == RecoveryAction.Restore
                        ? $"Failed to restore '{fileName}' from backup."
                        : $"Failed to delete '{fileName}'.";

                    BannerService.ReportError("Database recovery failed", message);
                }
            }
        }
        finally
        {
            _isApplying = false;
            StateHasChanged();
        }
    }

    private void DeleteAll()
    {
        foreach (var entry in _entries)
        {
            _selectedActions[entry.FileName] = RecoveryAction.Delete;
        }
    }

    private async Task DismissAsync()
    {
        if (_isDismissed) { return; }

        _isDismissed = true;

        try
        {
            if (_chrome is not null)
            {
                await _chrome.CloseAsync();
            }
        }
        finally
        {
            await OnDismissed.InvokeAsync();
        }
    }

    private Task HandleCancelOrEscAsync() =>
        _isApplying ? Task.CompletedTask : DismissAsync();

    private async Task HandleEntriesChangedAsync()
    {
        try
        {
            RefreshEntriesFromService();

            if (_entries.Count == 0)
            {
                await DismissAsync();
                return;
            }

            StateHasChanged();
        }
        catch (Exception unexpected)
        {
            TraceLogger.Error(
                $"DatabaseRecoveryDialog.HandleEntriesChangedAsync threw: {unexpected}");
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
