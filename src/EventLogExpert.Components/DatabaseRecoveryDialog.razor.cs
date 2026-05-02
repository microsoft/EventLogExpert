// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Components.Base;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Components;

/// <summary>
///     Lets the user restore-from-backup or permanently delete each database whose upgrade was
///     interrupted (i.e., still has a <c>.upgrade.bak</c> sidecar on disk). The dialog chrome
///     (<see cref="ModalChrome" />) owns <c>&lt;dialog&gt;</c> open/close and Esc handling; this
///     component owns the per-row state and the EntriesChanged subscription so rows resolved
///     externally (or by this dialog itself) disappear live and the dialog auto-dismisses when the
///     affected set becomes empty.
/// </summary>
public sealed partial class DatabaseRecoveryDialog : ComponentBase, IAsyncDisposable
{
    private readonly HashSet<string> _failedFileNames =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RecoveryAction> _selectedActions =
        new(StringComparer.OrdinalIgnoreCase);

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
            // The recovery set was already resolved (e.g. concurrently between the parent's mount
            // decision and our first render). The conditional in the .razor never rendered the
            // ModalChrome; defer the dismiss past the current render lifecycle so the parent's
            // OnDismissed handler doesn't run inside our own OnAfterRenderAsync.
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
        // Subscribe BEFORE snapshotting so any EntriesChanged that fires between the snapshot and the
        // subscribe is observed (the symmetric race that BannerService also avoids).
        DatabaseService.EntriesChanged += OnEntriesChanged;
        RefreshEntriesFromService();

        base.OnInitialized();
    }

    private async Task ApplyAsync()
    {
        if (_isApplying) { return; }

        _isApplying = true;
        StateHasChanged();

        // Snapshot to iterate a stable list even if EntriesChanged fires (and mutates _entries) mid-loop
        // from one of our own successful Restore/Delete calls.
        var rowsToProcess = _entries.ToArray();

        try
        {
            foreach (var rowEntry in rowsToProcess)
            {
                var fileName = rowEntry.FileName;

                if (!_selectedActions.TryGetValue(fileName, out var action)) { continue; }

                // Re-check current service state — concurrent recovery (e.g. another open dialog or a
                // separate code path) could have resolved this row between our snapshot and now. Skip
                // silently rather than calling the service and getting back a misleading
                // InvalidOperationException ("entry not found").
                var liveEntry = DatabaseService.Entries.FirstOrDefault(entry =>
                    string.Equals(entry.FileName, fileName, StringComparison.OrdinalIgnoreCase) &&
                    entry.BackupExists);

                if (liveEntry is null) { continue; }

                // Clear any prior failure mark for this row before attempting; we'll re-add only if
                // this attempt itself fails. Per-row scoping (instead of a global pre-clear) preserves
                // failure marks for rows we don't end up attempting in this Apply.
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
                    // DatabaseService raises InvalidOperationException for "entry not found" and
                    // "another operation in progress". Both mean the world changed underneath us;
                    // treat as benign skip rather than a user-visible recovery failure.
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
            // EntriesChanged fired by successful Restore/Delete calls will refilter _entries; if the
            // affected set ends up empty, OnEntriesChanged → DismissAsync auto-closes the dialog.
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
            // Best-effort early close so the dialog disappears immediately rather than waiting for
            // the parent's re-render to unmount us. ModalChrome.CloseAsync is idempotent and never
            // throws; a missing _chrome (e.g. empty-set path that never rendered the chrome) is
            // also fine.
            if (_chrome is not null)
            {
                await _chrome.CloseAsync();
            }
        }
        finally
        {
            // Always notify the parent so it can remove us from the tree, even if CloseAsync threw
            // unexpectedly. The conditional render then unmounts ModalChrome, whose DisposeAsync
            // makes a final idempotent JS closeModal call.
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

        // Default Restore for any newly-appeared entry; preserve prior selection for surviving entries.
        foreach (var entry in snapshot)
        {
            _selectedActions.TryAdd(entry.FileName, RecoveryAction.Restore);
        }

        // Drop selections + failure marks for entries no longer present (they were resolved externally
        // or by a previous Apply iteration).
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
