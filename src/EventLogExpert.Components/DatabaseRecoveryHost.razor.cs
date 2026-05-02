// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Components;

/// <summary>
///     App-root host that watches <see cref="IDatabaseService.Entries" /> for entries with
///     <see cref="DatabaseEntry.BackupExists" /> set (i.e., interrupted-upgrade backups still on
///     disk), surfaces an error banner with a "Resolve" action, and opens the
///     <see cref="DatabaseRecoveryDialog" /> when the user clicks the action. Mounted inside
///     <c>UnhandledExceptionHandler</c> so any crash in the host or the dialog routes through the
///     critical-error path instead of bypassing it.
/// </summary>
public sealed partial class DatabaseRecoveryHost : ComponentBase, IDisposable
{
    private bool _dialogOpen;
    private bool _disposed;
    private HashSet<string> _promptedFor = new(StringComparer.OrdinalIgnoreCase);
    private Guid? _recoveryBannerId;

    [Inject] private IBannerService BannerService { get; init; } = null!;

    [Inject] private IDatabaseService DatabaseService { get; init; } = null!;

    [Inject] private ITraceLogger TraceLogger { get; init; } = null!;

    public void Dispose()
    {
        if (_disposed) { return; }

        _disposed = true;
        DatabaseService.EntriesChanged -= OnEntriesChanged;
        BannerService.StateChanged -= OnBannerStateChanged;

        // Dismiss the banner we own so the host's lifetime — not the BannerService's queue — bounds
        // the recovery prompt. Without this, a crash that disposes us via UnhandledExceptionHandler
        // would leave a stale banner whose Resolve action targets this dead instance; after Recover
        // the new host would post a second banner alongside it.
        if (_recoveryBannerId is { } id)
        {
            BannerService.DismissError(id);
            _recoveryBannerId = null;
        }
    }

    protected override void OnInitialized()
    {
        // Subscribe BEFORE pulling initial state so an EntriesChanged tick that races with our
        // OnInitialized can't be dropped between the initial Entries read and the subscription
        // taking effect.
        DatabaseService.EntriesChanged += OnEntriesChanged;
        BannerService.StateChanged += OnBannerStateChanged;

        EvaluateState();
    }

    private void EvaluateState()
    {
        if (_disposed) { return; }

        HashSet<string> currentBackupSet = DatabaseService.Entries
            .Where(entry => entry.BackupExists)
            .Select(entry => entry.FileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (currentBackupSet.Count == 0)
        {
            if (_recoveryBannerId is { } activeId)
            {
                BannerService.DismissError(activeId);
                _recoveryBannerId = null;
            }

            _promptedFor.Clear();
            return;
        }

        if (_recoveryBannerId is { } visibleId)
        {
            // Banner currently visible — refresh count whenever the set changes (grow OR shrink),
            // otherwise leave the banner alone so we don't churn the UI.
            if (currentBackupSet.SetEquals(_promptedFor)) { return; }

            BannerService.DismissError(visibleId);
            _recoveryBannerId = ReportRecoveryBanner(currentBackupSet.Count);
            _promptedFor = currentBackupSet;
            return;
        }

        // No active banner: either initial state or user dismissed previously. Re-prompt only if
        // the set differs from the one the user dismissed.
        if (currentBackupSet.SetEquals(_promptedFor)) { return; }

        _recoveryBannerId = ReportRecoveryBanner(currentBackupSet.Count);
        _promptedFor = currentBackupSet;
    }

    private void HandleBannerStateChanged()
    {
        if (_disposed) { return; }

        if (_recoveryBannerId is { } id && BannerService.ErrorBanners.All(banner => banner.Id != id))
        {
            // The banner with our recorded id is no longer in the queue, so the user dismissed it
            // via the banner card's X button (the only path that mutates ErrorBanners without our
            // involvement). Clear the id but KEEP _promptedFor so we don't immediately re-prompt
            // for the same set on the next EntriesChanged tick.
            _recoveryBannerId = null;
        }
    }

    private void OnBannerStateChanged() => _ = InvokeAsync(HandleBannerStateChanged);

    private void OnDialogDismissed()
    {
        if (_disposed) { return; }

        _dialogOpen = false;
        StateHasChanged();
    }

    private void OnEntriesChanged(object? sender, EventArgs args) => _ = InvokeAsync(EvaluateState);

    private Task OpenRecoveryDialogAsync()
    {
        // BannerHost currently invokes the action callback on the renderer dispatcher, but route
        // through InvokeAsync defensively so the host stays correct if a future caller invokes the
        // action from a non-UI context.
        return InvokeAsync(() =>
        {
            if (_disposed) { return; }

            _dialogOpen = true;

            StateHasChanged();
        });
    }

    private Guid ReportRecoveryBanner(int count)
    {
        string message = count == 1
            ? "1 database needs recovery from interrupted upgrade."
            : $"{count} databases need recovery from interrupted upgrade.";

        return BannerService.ReportError(
            "Database upgrade recovery",
            message,
            "Resolve",
            OpenRecoveryDialogAsync);
    }
}
