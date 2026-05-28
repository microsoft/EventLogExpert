// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.Database.Upgrade;
using Microsoft.AspNetCore.Components;
using System.Collections.Immutable;

namespace EventLogExpert.UI.DatabaseTools.Tabs;

public sealed partial class ManageDatabasesTab : ComponentBase, IAsyncDisposable
{
    private readonly Dictionary<string, bool> _pendingToggles = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _saveBlockedHelpId = $"manage-save-blocked-{Guid.NewGuid():N}";

    private CancellationTokenSource? _classificationObservationCts;
    private volatile bool _disposed;
    private ImmutableHashSet<string> _initialActiveSnapshot = ImmutableHashSet<string>.Empty;
    private bool _restorationOccurred;
    private bool _schemaUpgradeOccurred;

    public bool HasDatabaseStateChanged
    {
        get
        {
            if (_schemaUpgradeOccurred || _restorationOccurred) { return true; }

            return !_initialActiveSnapshot.SetEquals(ComputeActiveSet());
        }
    }

    public bool HasPendingChanges => _pendingToggles.Count > 0;

    public bool IsUpgradeInFlight => Coordinator.IsAnyUpgradeInFlight;

    [CascadingParameter] internal IInlineAlertSurface? AlertSurface { get; set; }

    [Inject] private IAnnouncementService AnnouncementService { get; init; } = null!;

    [Inject] private IDatabaseOperationCoordinator Coordinator { get; init; } = null!;

    [Inject] private IDatabaseService DatabaseService { get; init; } = null!;

    private bool IsClassificationPending => !DatabaseService.InitialClassificationTask.IsCompleted;

    private bool IsUpgradeBlocked => IsUpgradeInFlight;

    [Inject] private IProgressBannerService ProgressBannerService { get; init; } = null!;

    [Inject] private ITraceLogger TraceLogger { get; init; } = null!;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) { return; }
        _disposed = true;

        if (_classificationObservationCts is not null)
        {
            try { await _classificationObservationCts.CancelAsync(); }
            catch (ObjectDisposedException) { }
            _classificationObservationCts.Dispose();
            _classificationObservationCts = null;
        }

        DatabaseService.EntriesChanged -= OnDatabaseEntriesChanged;
        DatabaseService.UpgradeBatchCompleted -= OnUpgradeBatchCompleted;
        ProgressBannerService.StateChanged -= OnBannerStateChanged;
        Coordinator.UpgradeStateChanged -= OnCoordinatorStateChanged;
    }

    /// <summary>
    ///     Save wrapper invoked by <see cref="DatabaseToolsModal" /> on the unsaved-changes close prompt and by the
    ///     inline Save button. Returns true on success or no-op; false when blocked by an in-flight upgrade or when the
    ///     coordinator apply faulted. Sets <see cref="HasDatabaseStateChanged" /> bookkeeping when active state actually
    ///     changed.
    /// </summary>
    internal async Task<bool> ApplyPendingTogglesAsync()
    {
        if (IsUpgradeBlocked) { return false; }

        var snapshot = _pendingToggles.Keys.ToArray();
        if (snapshot.Length == 0) { return true; }

        try
        {
            await Coordinator.ApplyPendingTogglesAsync(snapshot, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TraceLogger.Warning(
                $"{nameof(ManageDatabasesTab)}.{nameof(ApplyPendingTogglesAsync)}: {ex}");
            return false;
        }

        if (_disposed) { return false; }

        _pendingToggles.Clear();

        if (HasDatabaseStateChanged)
        {
            AnnouncementService.Announce("Database changes applied");
        }

        return true;
    }

    protected override void OnInitialized()
    {
        _initialActiveSnapshot = ComputeActiveSet();

        DatabaseService.EntriesChanged += OnDatabaseEntriesChanged;
        DatabaseService.UpgradeBatchCompleted += OnUpgradeBatchCompleted;
        ProgressBannerService.StateChanged += OnBannerStateChanged;
        Coordinator.UpgradeStateChanged += OnCoordinatorStateChanged;

        if (!DatabaseService.InitialClassificationTask.IsCompleted)
        {
            _classificationObservationCts = new CancellationTokenSource();
            _ = ObserveClassificationCompletionAsync(_classificationObservationCts.Token);
        }

        base.OnInitialized();
    }

    private async Task<bool> AskOverwriteAsync(string fileName, CancellationToken cancellationToken)
    {
        if (_disposed || AlertSurface is null) { return false; }

        try
        {
            var result = await AlertSurface.ShowInlineAlertAsync(
                new InlineAlertRequest(
                    Title: "Database already exists",
                    Message: $"{fileName} already exists. Overwrite?",
                    AcceptLabel: "Overwrite",
                    CancelLabel: "Skip",
                    IsPrompt: false,
                    PromptInitialValue: null),
                cancellationToken);

            return result.Accepted;
        }
        catch (ObjectDisposedException) { return false; }
    }

    private ImmutableHashSet<string> ComputeActiveSet() =>
        DatabaseService.Entries
            .Where(entry => entry is { IsEnabled: true, Status: DatabaseStatus.Ready })
            .Select(entry => entry.FileName)
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

    private void ConsumeReopenedAsBaseline()
    {
        _initialActiveSnapshot = ComputeActiveSet();
        _schemaUpgradeOccurred = false;
        _restorationOccurred = false;
    }

    private void DiscardPending()
    {
        if (_pendingToggles.Count == 0) { return; }
        _pendingToggles.Clear();
    }

    private bool GetEffectiveEnabled(DatabaseEntry entry) =>
        _pendingToggles.TryGetValue(entry.FileName, out var pending) ? pending : entry.IsEnabled;

    private async Task ImportDatabase()
    {
        var outcome = await Coordinator.ImportAsync(AskOverwriteAsync);

        if (_disposed) { return; }

        if (outcome.DatabaseStateChanged)
        {
            AnnouncementService.Announce("Database imported");
        }
    }

    private async Task InvokeAsyncSafe()
    {
        if (_disposed) { return; }
        try { await InvokeAsync(StateHasChanged); }
        catch (ObjectDisposedException) { }
    }

    private async Task ObserveClassificationCompletionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await DatabaseService.InitialClassificationTask
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            TraceLogger.Warning(
                $"{nameof(ManageDatabasesTab)}.{nameof(ObserveClassificationCompletionAsync)}: {ex}");
        }

        if (_disposed) { return; }

        _initialActiveSnapshot = ComputeActiveSet();
        await InvokeAsyncSafe();
    }

    private void OnBannerStateChanged()
    {
        if (_disposed) { return; }
        _ = InvokeAsyncSafe();
    }

    private void OnCoordinatorStateChanged()
    {
        if (_disposed) { return; }
        _ = InvokeAsyncSafe();
    }

    private void OnDatabaseEntriesChanged(object? sender, EventArgs e)
    {
        if (_disposed) { return; }
        _ = InvokeAsyncSafe();
    }

    private async Task OnSaveClickAsync()
    {
        if (IsUpgradeBlocked)
        {
            // aria-live="polite" may not re-announce identical text on repeated blocked clicks;
            // force an explicit announcement to guarantee SR feedback on every attempt.
            AnnouncementService.Announce("Cannot save: a database upgrade is in progress.");

            return;
        }

        await ApplyPendingTogglesAsync();
    }

    private void OnUpgradeBatchCompleted(object? sender, UpgradeBatchCompletedEventArgs args)
    {
        if (_disposed) { return; }

        // Only set the sticky flag when an upgraded file is currently in the active set; background
        // upgrades of disabled/non-Ready DBs don't affect open-log resolution and shouldn't prompt.
        if (args.Result.Succeeded.Count > 0)
        {
            var activeNow = ComputeActiveSet();

            if (args.Result.Succeeded.Any(activeNow.Contains))
            {
                _schemaUpgradeOccurred = true;
            }
        }

        _ = InvokeAsyncSafe();
    }

    private async Task RemoveDatabase(DatabaseEntry entry)
    {
        var fileName = entry.FileName;

        var outcome = await Coordinator.RemoveDatabaseAsync(
            fileName,
            async (showCloseReopenWarning, cancellationToken) =>
            {
                if (_disposed || AlertSurface is null) { return false; }

                var message = showCloseReopenWarning
                    ? $"{fileName} is currently enabled. Removing will close and reopen any affected log views. Are you sure?"
                    : $"Are you sure you want to remove {fileName}?";

                try
                {
                    var result = await AlertSurface.ShowInlineAlertAsync(
                        new InlineAlertRequest(
                            Title: "Remove Database",
                            Message: message,
                            AcceptLabel: "Remove",
                            CancelLabel: "Cancel",
                            IsPrompt: false,
                            PromptInitialValue: null),
                        cancellationToken);

                    return result.Accepted;
                }
                catch (ObjectDisposedException) { return false; }
            });

        if (_disposed) { return; }

        if (outcome.Confirmed) { _pendingToggles.Remove(fileName); }

        if (outcome.LogsReopened)
        {
            ConsumeReopenedAsBaseline();
        }
    }

    private async Task RestoreFromBackup(DatabaseEntry entry)
    {
        if (_disposed) { return; }

        // DatabaseRecoveryService.RestoreFromBackupAsync runs a full ClassifyEntriesAsync pass, whose
        // ProbeOrCleanupBackup deletes .upgrade.bak files for Ready entries — including a backup an
        // in-flight upgrade still needs for rollback/verify. Block at the handler as well as the button.
        if (IsUpgradeBlocked)
        {
            AnnouncementService.Announce("Cannot restore: a database upgrade is in progress.");

            return;
        }

        bool restored;
        try
        {
            restored = await DatabaseService.RestoreFromBackupAsync(entry.FileName, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TraceLogger.Warning(
                $"{nameof(ManageDatabasesTab)}.{nameof(RestoreFromBackup)} failed for '{entry.FileName}': {ex}");
            AnnouncementService.Announce($"Could not restore {entry.FileName} from backup.");

            return;
        }

        if (_disposed) { return; }

        if (restored)
        {
            _restorationOccurred = true;
            AnnouncementService.Announce($"Restored {entry.FileName} from backup.");
        }
        else
        {
            AnnouncementService.Announce($"Could not restore {entry.FileName} from backup.");
        }
    }

    private async Task RetryClassification(DatabaseEntry entry)
    {
        if (_disposed) { return; }

        try
        {
            await DatabaseService.RetryClassificationAsync(entry.FileName, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TraceLogger.Warning(
                $"{nameof(ManageDatabasesTab)}.{nameof(RetryClassification)} failed for '{entry.FileName}': {ex}");
        }
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
        await Coordinator.UpgradeDatabaseAsync(fileName);

        if (_disposed) { return; }

        await InvokeAsyncSafe();
    }
}
