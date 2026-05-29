// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.Database.Upgrade;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.UI.Database;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Collections.Immutable;

namespace EventLogExpert.UI.DatabaseTools.Tabs;

public sealed partial class ManageDatabasesTab : ComponentBase, IAsyncDisposable
{
    private static readonly TimeSpan s_cancelTimeout = TimeSpan.FromSeconds(30);

    private readonly Dictionary<string, bool> _pendingToggles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DatabaseEntryRow?> _removeButtonRefs = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _saveBlockedHelpId = $"manage-save-blocked-{Guid.NewGuid():N}";
    private readonly HashSet<string> _selectedForRemoval = new(StringComparer.OrdinalIgnoreCase);

    private ElementReference _bulkRemoveButtonRef;
    private CancellationTokenSource? _classificationObservationCts;
    private volatile bool _disposed;
    private (string FileName, FocusTarget Target)? _focusRestorationTarget;
    private ElementReference _importButtonRef;
    private ImmutableHashSet<string> _initialActiveSnapshot = ImmutableHashSet<string>.Empty;
    private bool _restorationOccurred;
    private bool _schemaUpgradeOccurred;
    private string _selectionAnnouncement = string.Empty;

    private enum FocusTarget
    {
        SameRowRemove,
        BulkRemoveButton,
        ImportButton
    }

    public bool HasDatabaseStateChanged
    {
        get
        {
            if (_schemaUpgradeOccurred || _restorationOccurred) { return true; }

            return !_initialActiveSnapshot.SetEquals(ComputeActiveSet());
        }
    }

    public bool HasPendingChanges => _pendingToggles.Count > 0;

    public bool HasSelectedForRemoval => _selectedForRemoval.Count > 0;

    public bool IsUpgradeInFlight => Coordinator.IsAnyUpgradeInFlight;

    [CascadingParameter] internal IInlineAlertSurface? AlertSurface { get; set; }

    [Inject] private IAnnouncementService AnnouncementService { get; init; } = null!;

    [Inject] private IDatabaseOperationCoordinator Coordinator { get; init; } = null!;

    [Inject] private IDatabaseService DatabaseService { get; init; } = null!;

    private bool IsClassificationPending => !DatabaseService.InitialClassificationTask.IsCompleted;

    private bool IsUpgradeBlocked => IsUpgradeInFlight;

    [Inject] private ILogReloadCoordinator LogReloadCoordinator { get; init; } = null!;

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

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusRestorationTarget is { } target)
        {
            _focusRestorationTarget = null;

            try
            {
                await (target.Target switch
                {
                    FocusTarget.SameRowRemove =>
                        _removeButtonRefs.GetValueOrDefault(target.FileName)?.FocusRemoveButtonAsync() ?? _importButtonRef.FocusAsync(preventScroll: true),
                    FocusTarget.BulkRemoveButton when HasSelectedForRemoval =>
                        _bulkRemoveButtonRef.FocusAsync(preventScroll: true),
                    FocusTarget.ImportButton =>
                        _importButtonRef.FocusAsync(preventScroll: true),
                    _ => ValueTask.CompletedTask
                });
            }
            catch (ObjectDisposedException) { }
            catch (JSDisconnectedException) { }
            catch (JSException) { }
        }

        await base.OnAfterRenderAsync(firstRender);
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

    private static string BuildBulkPlainMessage(IReadOnlyList<string> fileNames)
    {
        var list = string.Join(", ", fileNames.Take(5));
        var more = fileNames.Count > 5 ? $", and {fileNames.Count - 5} more" : string.Empty;

        return $"Are you sure you want to remove these {fileNames.Count} databases? ({list}{more})";
    }

    private string AppendCloseReopenWarningIfNeeded(string baseMessage, IReadOnlyList<string> fileNames)
    {
        if (!LogReloadCoordinator.HasActiveLogs) { return baseMessage; }

        bool anyRemovalAffectsActiveLog = fileNames.Any(f =>
            DatabaseService.Entries.Any(e =>
                string.Equals(e.FileName, f, StringComparison.OrdinalIgnoreCase) &&
                e.IsEnabled &&
                e.Status == DatabaseStatus.Ready));

        if (!anyRemovalAffectsActiveLog) { return baseMessage; }

        string warning = fileNames.Count == 1
            ? "Removing will close and reopen any affected log views."
            : "Removing these databases will close and reopen any affected log views.";

        return $"{baseMessage} {warning}";
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

    private string BuildCancelThenRemoveMessage(IReadOnlyList<string> fileNames, IReadOnlyList<string> upgradingFiles)
    {
        var upgradingList = string.Join(", ", upgradingFiles.Take(5));
        var moreUpgrading = upgradingFiles.Count > 5 ? $", and {upgradingFiles.Count - 5} more" : string.Empty;
        var fileList = string.Join(", ", fileNames.Take(5));
        var moreFiles = fileNames.Count > 5 ? $", and {fileNames.Count - 5} more" : string.Empty;

        string baseMessage = $"Upgrade in progress for: {upgradingList}{moreUpgrading}. " +
            $"This will cancel the upgrade batch(es) \u2014 which may include other files not in your selection \u2014 " +
            $"and then remove: {fileList}{moreFiles}. Are you sure?";

        return AppendCloseReopenWarningIfNeeded(baseMessage, fileNames);
    }

    private string BuildPlainRemoveMessage(IReadOnlyList<string> fileNames)
    {
        string baseMessage = fileNames.Count == 1
            ? $"Are you sure you want to remove {fileNames[0]}?"
            : BuildBulkPlainMessage(fileNames);

        return AppendCloseReopenWarningIfNeeded(baseMessage, fileNames);
    }

    private async Task CancelUpgradesAndAwaitCompletionAsync(IReadOnlyList<string> fileNames)
    {
        var batchesToCancel = new Dictionary<UpgradeBatchId, Action>();
        var batchToFiles = new Dictionary<UpgradeBatchId, List<string>>();
        var entries = DatabaseService.Entries.ToList();

        foreach (var fileName in fileNames)
        {
            var entry = entries.FirstOrDefault(
                e => string.Equals(e.FileName, fileName, StringComparison.OrdinalIgnoreCase));

            if (entry is null) { continue; }

            var progress = GetUpgradeProgressForEntry(entry);

            if (progress is null) { continue; }

            batchesToCancel.TryAdd(progress.BatchId, progress.Cancel);

            if (!batchToFiles.TryGetValue(progress.BatchId, out var files))
            {
                files = [];
                batchToFiles[progress.BatchId] = files;
            }

            files.Add(fileName);
        }

        bool coordinatorTrackedAny = fileNames.Any(f => Coordinator.IsUpgradeInFlight(f));

        if (batchesToCancel.Count == 0 && !coordinatorTrackedAny) { return; }

        var pendingBatches = new Dictionary<UpgradeBatchId, TaskCompletionSource>();

        foreach (var batchId in batchesToCancel.Keys)
        {
            pendingBatches[batchId] = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        var coordinatorSettleTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void CompletionHandler(object? sender, UpgradeBatchCompletedEventArgs args)
        {
            if (pendingBatches.TryGetValue(args.BatchId, out var tcs))
            {
                tcs.TrySetResult();
            }
        }

        void StateChangedHandler()
        {
            if (!fileNames.Any(f => Coordinator.IsUpgradeInFlight(f)))
            {
                coordinatorSettleTcs.TrySetResult();
            }
        }

        DatabaseService.UpgradeBatchCompleted += CompletionHandler;
        Coordinator.UpgradeStateChanged += StateChangedHandler;

        try
        {
            foreach (var (batchId, files) in batchToFiles)
            {
                bool stillAlive = files.Any(file =>
                {
                    var entry = DatabaseService.Entries.FirstOrDefault(
                        e => string.Equals(e.FileName, file, StringComparison.OrdinalIgnoreCase));
                    return Coordinator.IsUpgradeInFlight(file) ||
                           (entry is not null && GetUpgradeProgressForEntry(entry) is not null);
                });
                if (!stillAlive) { pendingBatches[batchId].TrySetResult(); }
            }

            StateChangedHandler();

            foreach (var cancel in batchesToCancel.Values)
            {
                try { cancel(); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    TraceLogger.Warning(
                        $"{nameof(ManageDatabasesTab)}.{nameof(CancelUpgradesAndAwaitCompletionAsync)}: cancel threw: {ex}");
                }
            }

            using var timeoutCts = new CancellationTokenSource(s_cancelTimeout);

            var batchTasks = pendingBatches.Values.Select(tcs => tcs.Task);
            var allTasks = batchTasks.Append(coordinatorSettleTcs.Task).ToArray();

            try
            {
                await Task.WhenAll(allTasks).WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                TraceLogger.Warning(
                    $"{nameof(ManageDatabasesTab)}.{nameof(CancelUpgradesAndAwaitCompletionAsync)}: timeout waiting for upgrade cancellation; proceeding anyway.");
            }
        }
        finally
        {
            DatabaseService.UpgradeBatchCompleted -= CompletionHandler;
            Coordinator.UpgradeStateChanged -= StateChangedHandler;
        }
    }

    private void ClearSelection()
    {
        if (_selectedForRemoval.Count == 0) { return; }
        _selectedForRemoval.Clear();
        UpdateSelectionAnnouncement();
    }

    private ImmutableHashSet<string> ComputeActiveSet() =>
        DatabaseService.Entries
            .Where(entry => entry is { IsEnabled: true, Status: DatabaseStatus.Ready })
            .Select(entry => entry.FileName)
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

    private async Task ConfirmAndRemoveAsync(
        IReadOnlyList<string> fileNames,
        FocusTarget cancelTarget,
        FocusTarget completeTarget)
    {
        if (_disposed || AlertSurface is null || fileNames.Count == 0) { return; }

        var validFileNames = fileNames
            .Where(f => DatabaseService.Entries.Any(e =>
                string.Equals(e.FileName, f, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (validFileNames.Count == 0) { return; }

        bool requiresCancelFirst = IsAnyFileUpgrading(validFileNames, out var upgradingFiles);

        string title = validFileNames.Count == 1
            ? "Remove Database"
            : $"Remove {validFileNames.Count} Databases";

        string acceptLabel = requiresCancelFirst
            ? $"Cancel {upgradingFiles.Count} upgrade{(upgradingFiles.Count == 1 ? "" : "s")} and remove {validFileNames.Count} database{(validFileNames.Count == 1 ? "" : "s")}"
            : "Remove";

        string message = requiresCancelFirst
            ? BuildCancelThenRemoveMessage(validFileNames, upgradingFiles)
            : BuildPlainRemoveMessage(validFileNames);

        InlineAlertResult result;
        try
        {
            result = await AlertSurface.ShowInlineAlertAsync(
                new InlineAlertRequest(
                    Title: title,
                    Message: message,
                    AcceptLabel: acceptLabel,
                    CancelLabel: "Cancel",
                    IsPrompt: false,
                    PromptInitialValue: null),
                CancellationToken.None);
        }
        catch (ObjectDisposedException) { return; }

        if (!result.Accepted)
        {
            _focusRestorationTarget = (validFileNames[0], cancelTarget);
            return;
        }
        if (_disposed) { return; }

        if (requiresCancelFirst)
        {
            await CancelUpgradesAndAwaitCompletionAsync(upgradingFiles);
            if (_disposed) { return; }
        }

        await RemoveDatabasesAsync(validFileNames, completeTarget);
    }

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

    private BannerProgressEntry? GetUpgradeProgressForEntry(DatabaseEntry entry)
    {
        var manage = ProgressBannerService.ManageDatabasesProgress;
        if (manage is not null &&
            string.Equals(manage.CurrentEntryName, entry.FileName, StringComparison.OrdinalIgnoreCase))
        {
            return manage;
        }

        var background = ProgressBannerService.BackgroundProgress;
        if (background is not null &&
            string.Equals(background.CurrentEntryName, entry.FileName, StringComparison.OrdinalIgnoreCase))
        {
            return background;
        }

        return null;
    }

    private async Task ImportDatabase()
    {
        var outcome = await Coordinator.ImportAsync(AskOverwriteAsync);

        if (_disposed) { return; }

        if (outcome.DatabaseStateChanged)
        {
            _schemaUpgradeOccurred = true;
            AnnouncementService.Announce("Database imported");
        }
    }

    private async Task InvokeAsyncSafe()
    {
        if (_disposed) { return; }
        try { await InvokeAsync(StateHasChanged); }
        catch (ObjectDisposedException) { }
    }

    private bool IsAnyFileUpgrading(IReadOnlyCollection<string> fileNames, out IReadOnlyList<string> upgradingFiles)
    {
        var upgrading = new List<string>();
        var entries = DatabaseService.Entries.ToList();

        foreach (var fileName in fileNames)
        {
            var entry = entries.FirstOrDefault(
                e => string.Equals(e.FileName, fileName, StringComparison.OrdinalIgnoreCase));

            if (entry is null) { continue; }

            bool coordinatorSays = Coordinator.IsUpgradeInFlight(fileName);
            bool bannerSays = GetUpgradeProgressForEntry(entry) is not null;

            if (coordinatorSays || bannerSays) { upgrading.Add(fileName); }
        }

        upgradingFiles = upgrading;

        return upgrading.Count > 0;
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

    private async Task OnBulkRemoveClickAsync()
    {
        var snapshot = _selectedForRemoval.ToArray();

        await ConfirmAndRemoveAsync(snapshot, FocusTarget.BulkRemoveButton, FocusTarget.SameRowRemove);
    }

    private void OnCoordinatorStateChanged()
    {
        if (_disposed) { return; }

        _ = InvokeAsyncSafe();
    }

    private void OnDatabaseEntriesChanged(object? sender, EventArgs e)
    {
        if (_disposed) { return; }

        var currentNames = new HashSet<string>(
            DatabaseService.Entries.Select(entry => entry.FileName),
            StringComparer.OrdinalIgnoreCase);

        var orphans = _selectedForRemoval.Where(name => !currentNames.Contains(name)).ToArray();

        foreach (var orphan in orphans) { _selectedForRemoval.Remove(orphan); }
        
        if (orphans.Length > 0) { UpdateSelectionAnnouncement(); }

        var deadRefs = _removeButtonRefs
            .Where(kvp => !currentNames.Contains(kvp.Key) || kvp.Value is null)
            .Select(kvp => kvp.Key)
            .ToArray();
        
        foreach (var key in deadRefs) { _removeButtonRefs.Remove(key); }

        _ = InvokeAsyncSafe();
    }

    private async Task OnSaveClickAsync()
    {
        if (IsUpgradeBlocked)
        {
            AnnouncementService.Announce("Cannot save: a database upgrade is in progress.");

            return;
        }

        await ApplyPendingTogglesAsync();
    }

    private void OnUpgradeBatchCompleted(object? sender, UpgradeBatchCompletedEventArgs args)
    {
        if (_disposed) { return; }

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

    private async Task RemoveDatabase(DatabaseEntry entry) =>
        await ConfirmAndRemoveAsync([entry.FileName], FocusTarget.SameRowRemove, FocusTarget.SameRowRemove);

    private async Task RemoveDatabasesAsync(IReadOnlyList<string> fileNames, FocusTarget completeTarget)
    {
        var preRemovalEntries = DatabaseService.Entries.ToList();

        int anchorIdx = fileNames
            .Select(f => preRemovalEntries.FindIndex(e =>
                string.Equals(e.FileName, f, StringComparison.OrdinalIgnoreCase)))
            .Where(i => i >= 0)
            .DefaultIfEmpty(-1)
            .Min();

        var succeeded = new List<string>();
        var failed = new List<(string FileName, string Reason)>();
        bool anyLogsReopened = false;

        foreach (var fileName in fileNames)
        {
            try
            {
                var outcome = await Coordinator.RemoveDatabaseAsync(
                    fileName,
                    static (_, _) => Task.FromResult(true));

                if (outcome.Removed)
                {
                    succeeded.Add(fileName);
                    _pendingToggles.Remove(fileName);

                    if (outcome.LogsReopened) { anyLogsReopened = true; }
                }
                else if (outcome.Confirmed)
                {
                    failed.Add((fileName, "removal failed after confirmation"));
                    TraceLogger.Warning(
                        $"{nameof(ManageDatabasesTab)}.{nameof(RemoveDatabasesAsync)}: removal of '{fileName}' was confirmed but did not complete.");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed.Add((fileName, ex.Message));
                TraceLogger.Warning(
                    $"{nameof(ManageDatabasesTab)}.{nameof(RemoveDatabasesAsync)}: removal of '{fileName}' threw: {ex}");
            }
        }

        foreach (var fileName in fileNames)
        {
            _selectedForRemoval.Remove(fileName);
        }
        UpdateSelectionAnnouncement();

        if (anyLogsReopened)
        {
            ConsumeReopenedAsBaseline();
        }

        if (succeeded.Count > 0)
        {
            AnnouncementService.Announce(
                $"Removed {succeeded.Count} database{(succeeded.Count == 1 ? "" : "s")}.");
        }

        if (failed.Count > 0)
        {
            var (firstFailedFile, firstFailedReason) = failed[0];
            AnnouncementService.Announce(
                $"{failed.Count} removal{(failed.Count == 1 ? "" : "s")} failed. First: {firstFailedFile} ({firstFailedReason}).");
        }

        var remainingEntries = DatabaseService.Entries.ToList();

        if (remainingEntries.Count == 0)
        {
            _focusRestorationTarget = (string.Empty, FocusTarget.ImportButton);
        }
        else
        {
            int clampedIdx = Math.Clamp(anchorIdx, 0, remainingEntries.Count - 1);
            string anchorFileName = remainingEntries[clampedIdx].FileName;
            _focusRestorationTarget = (anchorFileName, completeTarget);
        }
    }

    private async Task RestoreFromBackup(DatabaseEntry entry)
    {
        if (_disposed) { return; }

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

    private void ToggleSelection(string fileName)
    {
        bool added = _selectedForRemoval.Add(fileName);

        if (!added) { _selectedForRemoval.Remove(fileName); }

        UpdateSelectionAnnouncement();
    }

    private void UpdateSelectionAnnouncement()
    {
        int count = _selectedForRemoval.Count;
        _selectionAnnouncement = count == 0
            ? "Selection cleared."
            : $"{count} database{(count == 1 ? "" : "s")} selected.";
    }

    private async Task UpgradeEntry(string fileName)
    {
        await Coordinator.UpgradeDatabaseAsync(fileName);

        if (_disposed) { return; }

        await InvokeAsyncSafe();
    }
}
