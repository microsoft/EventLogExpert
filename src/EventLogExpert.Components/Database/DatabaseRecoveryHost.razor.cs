// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Logging;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Components.Database;

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

        if (_recoveryBannerId is not { } id) { return; }

        BannerService.DismissError(id);
        _recoveryBannerId = null;
    }

    protected override void OnInitialized()
    {
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
            DismissCurrentBannerIfAny();
            _promptedFor.Clear();

            return;
        }

        if (currentBackupSet.SetEquals(_promptedFor)) { return; }

        DismissCurrentBannerIfAny();

        _recoveryBannerId = ReportRecoveryBanner(currentBackupSet.Count);
        _promptedFor = currentBackupSet;
    }

    private void DismissCurrentBannerIfAny()
    {
        if (_recoveryBannerId is not { } activeId) { return; }

        BannerService.DismissError(activeId);
        _recoveryBannerId = null;
    }

    private void HandleBannerStateChanged()
    {
        if (_disposed) { return; }

        if (_recoveryBannerId is not { } id) { return; }

        if (BannerService.ErrorBanners.Any(banner => banner.Id == id)) { return; }

        _recoveryBannerId = null;
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

        return BannerService.ReportError("Database upgrade recovery", message, "Resolve", OpenRecoveryDialogAsync);
    }
}
