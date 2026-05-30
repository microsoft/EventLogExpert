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
using System.Text;

namespace EventLogExpert.UI.DatabaseTools.Tabs;

public sealed partial class ManageDatabasesTab : ComponentBase, IAsyncDisposable
{
    private static readonly TimeSpan s_cancelTimeout = TimeSpan.FromSeconds(30);

    private readonly Dictionary<string, bool> _pendingToggles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DatabaseEntryRow?> _rowRefs = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _saveBlockedHelpId = $"manage-save-blocked-{Guid.NewGuid():N}";
    private readonly HashSet<string> _selectedForBulk = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _upgradeBlockedHelpId = $"manage-upgrade-blocked-{Guid.NewGuid():N}";

    private ElementReference _bulkRemoveButtonRef;
    private ElementReference _bulkUpgradeButtonRef;
    private CancellationTokenSource? _classificationObservationCts;
    private volatile bool _disposed;
    private int _eligibleUpgradeCount;
    private (string FileName, FocusTarget Target)? _focusRestorationTarget;
    private ElementReference _importButtonRef;
    private ImmutableHashSet<string> _initialActiveSnapshot = ImmutableHashSet<string>.Empty;
    private bool _isSelectionModeActive;
    private ElementReference _masterCheckboxRef;
    private bool _restorationOccurred;
    private bool _schemaUpgradeOccurred;
    private ElementReference _selectButtonRef;
    private string _selectionAnnouncement = string.Empty;

    private enum FocusTarget
    {
        SameRowName,
        BulkRemoveButton,
        ImportButton
    }

    public bool HasBulkSelection => _selectedForBulk.Count > 0;

    public bool HasDatabaseStateChanged
    {
        get
        {
            if (_schemaUpgradeOccurred || _restorationOccurred) { return true; }

            return !_initialActiveSnapshot.SetEquals(ComputeActiveSet());
        }
    }

    public bool HasPendingChanges => _pendingToggles.Count > 0;

    public bool IsInSelectionMode => _isSelectionModeActive;

    public bool IsUpgradeInFlight => Coordinator.IsAnyUpgradeInFlight;

    [CascadingParameter] internal IInlineAlertSurface? AlertSurface { get; set; }

    [Inject] private IAnnouncementService AnnouncementService { get; init; } = null!;

    [Inject] private IDatabaseOperationCoordinator Coordinator { get; init; } = null!;

    [Inject] private IDatabaseService DatabaseService { get; init; } = null!;

    private bool IsClassificationPending => !DatabaseService.InitialClassificationTask.IsCompleted;

    private bool IsUpgradeBlocked => IsUpgradeInFlight;

    [Inject] private ILogReloadCoordinator LogReloadCoordinator { get; init; } = null!;

    private string MasterCheckboxAriaChecked =>
        _selectedForBulk.Count == 0 ? "false"
        : _selectedForBulk.Count >= DatabaseService.Entries.Count ? "true"
        : "mixed";

    private string MasterCheckboxAriaLabel =>
        _selectedForBulk.Count >= DatabaseService.Entries.Count && _selectedForBulk.Count > 0
            ? "Clear selection"
            : "Select all";

    private string MasterCheckboxIconClass =>
        _selectedForBulk.Count == 0 ? "bi-square"
        : _selectedForBulk.Count >= DatabaseService.Entries.Count ? "bi-check-square-fill"
        : "bi-dash-square-fill";

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

    public async Task ExitSelectionModeWithFocusAsync()
    {
        if (!_isSelectionModeActive) { return; }

        ExitSelectionMode();
        StateHasChanged();

        try { await _selectButtonRef.FocusAsync(preventScroll: true); }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
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
                    FocusTarget.SameRowName when !string.IsNullOrEmpty(target.FileName) =>
                        FocusEntryRowNameAsync(target.FileName),
                    FocusTarget.BulkRemoveButton when HasBulkSelection =>
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

    private static string GetSkipReason(DatabaseEntry entry, bool isUpgrading)
    {
        if (entry.BackupExists) { return "Restore from backup required"; }
        if (isUpgrading) { return "Upgrade in progress"; }
        return entry.Status switch
        {
            DatabaseStatus.Ready => "Already up to date",
            DatabaseStatus.NotClassified => "Classification pending",
            DatabaseStatus.UnrecognizedSchema => "Unrecognized schema",
            DatabaseStatus.ObsoleteSchema => "Obsolete schema",
            DatabaseStatus.ClassificationFailed => "Classification failed",
            _ => "Not eligible"
        };
    }

    private async Task AnnounceBulkUpgradeOutcomeAsync(UpgradeBatchResult? result, int attempted)
    {
        // Gate-denied (singleton upgrade gate held by another caller).
        if (result is null)
        {
            if (AlertSurface is null) { return; }
            try
            {
                _ = await AlertSurface.ShowInlineAlertAsync(
                    new InlineAlertRequest(
                        Title: "Upgrade not started",
                        Message: "Another upgrade is already in progress.",
                        AcceptLabel: null,
                        CancelLabel: "OK",
                        IsPrompt: false,
                        PromptInitialValue: null),
                    CancellationToken.None);
            }
            catch (ObjectDisposedException) { }
            return;
        }

        // Exception-fallback: coordinator's RunOperationAsync<T> returns an empty
        // result on a thrown exception. The error banner already fired upstream;
        // suppress the success-shaped announcement that would otherwise read
        // "Upgraded 0 databases" alongside it.
        if (attempted > 0
            && result.Succeeded.Count == 0
            && result.Failed.Count == 0
            && result.Cancelled.Count == 0)
        {
            return;
        }

        if (result.Cancelled.Count > 0 && result.Failed.Count == 0)
        {
            AnnouncementService.Announce(
                $"Upgraded {result.Succeeded.Count} database{(result.Succeeded.Count == 1 ? "" : "s")}; "
                + $"{result.Cancelled.Count} cancelled.");
            return;
        }

        if (result.Failed.Count > 0)
        {
            var first = result.Failed[0];
            var summary = result.Failed.Count == 1
                ? $"Upgrade of '{first.FileName}' failed: {first.Message}"
                : $"Upgraded {result.Succeeded.Count} of {attempted} databases; "
                    + $"{result.Failed.Count} failed. First failure: '{first.FileName}' \u2014 {first.Message}";
            AnnouncementService.Announce(summary);
            return;
        }

        AnnouncementService.Announce(
            $"Upgraded {result.Succeeded.Count} database{(result.Succeeded.Count == 1 ? "" : "s")}.");
    }

    private string AppendCloseReopenWarningIfNeeded(string baseMessage, IReadOnlyList<string> fileNames)
    {
        if (!LogReloadCoordinator.HasActiveLogs) { return baseMessage; }

        bool anyRemovalAffectsActiveLog = fileNames.Any(f =>
            DatabaseService.Entries.Any(e =>
                string.Equals(e.FileName, f, StringComparison.OrdinalIgnoreCase) &&
                e is { IsEnabled: true, Status: DatabaseStatus.Ready }));

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

        foreach (var fileName in fileNames)
        {
            var cancellable = GetCancellableBatchForFile(fileName);

            if (cancellable is null) { continue; }

            batchesToCancel.TryAdd(cancellable.Value.BatchId, cancellable.Value.Cancel);

            if (!batchToFiles.TryGetValue(cancellable.Value.BatchId, out var files))
            {
                files = [];
                batchToFiles[cancellable.Value.BatchId] = files;
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

        // Coordinator-only — adding IsFileInAnyKnownBatch here would 30s-hang on background batches
        // (no UpgradeStateChanged event ever fires to re-trigger). pendingBatches + stillAlive below
        // already gate Remove via UpgradeBatchCompleted.
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
                // stillAlive includes IsFileInAnyKnownBatch so pendingBatches stays unset for
                // pre-progress / queued files until the real UpgradeBatchCompleted fires.
                bool stillAlive = files.Any(file =>
                {
                    var entry = DatabaseService.Entries.FirstOrDefault(
                        e => string.Equals(e.FileName, file, StringComparison.OrdinalIgnoreCase));
                    return Coordinator.IsUpgradeInFlight(file) ||
                           (entry is not null && GetUpgradeProgressForEntry(entry) is not null) ||
                           IsFileInAnyKnownBatch(file);
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

    private void ExitSelectionMode()
    {
        _isSelectionModeActive = false;
        _selectedForBulk.Clear();
        RecomputeEligibleCount();
        UpdateSelectionAnnouncement();
    }

    private async Task FocusAfterBulkUpgradeAsync()
    {
        // After clean-success auto-exit, the bulk strip is gone; focus the Select
        // button so keyboard users have a stable anchor.
        if (!_isSelectionModeActive)
        {
            try { await _selectButtonRef.FocusAsync(preventScroll: true); }
            catch (JSDisconnectedException) { }
            catch (JSException) { }
            return;
        }

        if (_selectedForBulk.Count > 0)
        {
            try { await _masterCheckboxRef.FocusAsync(preventScroll: true); }
            catch (JSDisconnectedException) { }
            catch (JSException) { }
            return;
        }

        try { await _selectButtonRef.FocusAsync(preventScroll: true); }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
    }

    private async ValueTask FocusEntryRowNameAsync(string fileName)
    {
        if (_rowRefs.TryGetValue(fileName, out var rowRef) && rowRef is not null)
        {
            try { await rowRef.FocusNameAsync(); }
            catch (ObjectDisposedException) { }
            catch (JSException) { }

            return;
        }

        try { await _importButtonRef.FocusAsync(preventScroll: true); }
        catch (ObjectDisposedException) { }
        catch (JSException) { }
    }

    // Lookup by batch membership (active + queued) for the cancel-then-remove flow. Distinct from
    // GetUpgradeProgressForEntry, which matches by CurrentEntryName for per-row display only.
    private CancellableBatch? GetCancellableBatchForFile(string fileName)
    {
        var manage = ProgressBannerService.ManageDatabasesProgress;

        if (manage is not null && manage.BatchFileNames.Contains(fileName))
        {
            return new CancellableBatch(manage.BatchId, manage.Cancel);
        }

        var background = ProgressBannerService.BackgroundProgress;

        if (background is not null && background.BatchFileNames.Contains(fileName))
        {
            return new CancellableBatch(background.BatchId, background.Cancel);
        }

        foreach (var queued in DatabaseService.SnapshotQueuedBatches())
        {
            if (queued.FileNames.Contains(fileName))
            {
                return new CancellableBatch(queued.BatchId, queued.Cancel);
            }
        }

        return null;
    }

    private bool GetEffectiveEnabled(DatabaseEntry entry) =>
        _pendingToggles.TryGetValue(entry.FileName, out var pending) ? pending : entry.IsEnabled;

    private (bool IsEligible, bool IsUpgrading) GetEntryUpgradeState(DatabaseEntry entry)
    {
        bool upgrading = Coordinator.IsUpgradeInFlight(entry.FileName) ||
            GetUpgradeProgressForEntry(entry) is not null ||
            IsFileInAnyKnownBatch(entry.FileName);

        return (DatabaseEntryEligibility.IsUpgradeEligible(entry, upgrading), upgrading);
    }

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

            bool coordinatorSays = Coordinator.IsUpgradeInFlight(fileName);
            bool bannerSays = entry is not null && GetUpgradeProgressForEntry(entry) is not null;
            bool batchSays = IsFileInAnyKnownBatch(fileName);

            if (coordinatorSays || bannerSays || batchSays) { upgrading.Add(fileName); }
        }

        upgradingFiles = upgrading;

        return upgrading.Count > 0;
    }

    private bool IsFileInAnyKnownBatch(string fileName) =>
        GetCancellableBatchForFile(fileName) is not null;

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

        RecomputeEligibleCount();
        _ = InvokeAsyncSafe();
    }

    private async Task OnBulkRemoveClickAsync()
    {
        var snapshot = _selectedForBulk.ToArray();

        await ConfirmAndRemoveAsync(snapshot, FocusTarget.BulkRemoveButton, FocusTarget.SameRowName);
    }

    private async Task OnBulkUpgradeClickAsync()
    {
        if (_eligibleUpgradeCount == 0 || IsUpgradeBlocked) { return; }

        var entriesByFileName = SnapshotEntriesByFileName();
        var eligible = new List<string>();
        var skipped = new List<(string FileName, string Reason)>();

        foreach (var fileName in _selectedForBulk)
        {
            if (!entriesByFileName.TryGetValue(fileName, out var entry)) { continue; }

            var (isEligible, isUpgrading) = GetEntryUpgradeState(entry);

            if (isEligible)
            {
                eligible.Add(fileName);
            }
            else
            {
                skipped.Add((fileName, GetSkipReason(entry, isUpgrading)));
            }
        }

        if (eligible.Count == 0) { return; }

        if (skipped.Count > 0)
        {
            var confirmed = await PromptSubsetConfirmAsync(eligible, skipped, CancellationToken.None);

            if (!confirmed)
            {
                if (_disposed) { return; }

                try { await _bulkUpgradeButtonRef.FocusAsync(preventScroll: true); }
                catch (JSDisconnectedException) { }
                catch (JSException) { }

                return;
            }
        }

        var result = await Coordinator.UpgradeDatabasesAsync(
            eligible,
            UpgradeProgressScope.ManageDatabasesTriggered,
            CancellationToken.None);

        if (_disposed) { return; }

        await AnnounceBulkUpgradeOutcomeAsync(result, eligible.Count);

        // Auto-exit only on a fully-clean batch: any partial-failure/cancellation
        // leaves the relevant rows selected so the user can retry or inspect.
        bool cleanSuccess = result is not null
            && result.Failed.Count == 0
            && result.Cancelled.Count == 0
            && result.Succeeded.Count > 0;

        if (cleanSuccess)
        {
            ExitSelectionMode();
        }

        await FocusAfterBulkUpgradeAsync();
    }

    private void OnCoordinatorStateChanged()
    {
        if (_disposed) { return; }

        RecomputeEligibleCount();
        _ = InvokeAsyncSafe();
    }

    private void OnDatabaseEntriesChanged(object? sender, EventArgs e)
    {
        if (_disposed) { return; }

        var currentNames = new HashSet<string>(
            DatabaseService.Entries.Select(entry => entry.FileName),
            StringComparer.OrdinalIgnoreCase);

        var orphans = _selectedForBulk.Where(name => !currentNames.Contains(name)).ToArray();

        foreach (var orphan in orphans) { _selectedForBulk.Remove(orphan); }

        if (orphans.Length > 0)
        {
            UpdateSelectionAnnouncement();
        }

        // Always recompute when any selection exists: a row's status / backup /
        // upgrade state can change in-place (classification completion, backup
        // restore, upgrade finished) without changing the entry list itself.
        if (_selectedForBulk.Count > 0)
        {
            RecomputeEligibleCount();
        }

        var deadRefs = _rowRefs
            .Where(kvp => !currentNames.Contains(kvp.Key) || kvp.Value is null)
            .Select(kvp => kvp.Key)
            .ToArray();

        foreach (var key in deadRefs) { _rowRefs.Remove(key); }

        // Auto-exit selection mode if the entries list collapses while selecting;
        // the bulk strip would render above an empty-state placeholder otherwise.
        if (_isSelectionModeActive && currentNames.Count == 0)
        {
            ExitSelectionMode();
        }

        _ = InvokeAsyncSafe();
    }

    private async Task OnMasterCheckboxClickAsync()
    {
        if (_selectedForBulk.Count >= DatabaseService.Entries.Count && _selectedForBulk.Count > 0)
        {
            _selectedForBulk.Clear();
            RecomputeEligibleCount();
            UpdateSelectionAnnouncement();
        }
        else
        {
            SelectAll();
        }

        try { await _masterCheckboxRef.FocusAsync(preventScroll: true); }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
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

    private async Task<bool> PromptSubsetConfirmAsync(
        IReadOnlyList<string> eligible,
        IReadOnlyList<(string FileName, string Reason)> skipped,
        CancellationToken cancellationToken)
    {
        if (AlertSurface is null) { return true; }

        var sb = new StringBuilder();
        sb.Append(eligible.Count);
        sb.Append(" of ");
        sb.Append(eligible.Count + skipped.Count);
        sb.AppendLine(" selected databases will be upgraded.");
        sb.AppendLine();
        sb.Append("The following ");
        sb.Append(skipped.Count);
        sb.Append(skipped.Count == 1 ? " database will be skipped:" : " databases will be skipped:");

        foreach (var (fileName, reason) in skipped)
        {
            sb.Append("\n  \u2022 ");
            sb.Append(fileName);
            sb.Append(" (");
            sb.Append(reason);
            sb.Append(')');
        }

        InlineAlertResult response;
        try
        {
            response = await AlertSurface.ShowInlineAlertAsync(
                new InlineAlertRequest(
                    Title: "Upgrade selected databases",
                    Message: sb.ToString(),
                    AcceptLabel: $"Upgrade {eligible.Count}",
                    CancelLabel: "Cancel",
                    IsPrompt: false,
                    PromptInitialValue: null),
                cancellationToken);
        }
        catch (ObjectDisposedException) { return false; }

        return response.Accepted;
    }

    private void RecomputeEligibleCount()
    {
        if (_selectedForBulk.Count == 0)
        {
            _eligibleUpgradeCount = 0;
            return;
        }

        var entriesByFileName = SnapshotEntriesByFileName();
        int count = 0;

        foreach (var fileName in _selectedForBulk)
        {
            if (entriesByFileName.TryGetValue(fileName, out var entry) &&
                GetEntryUpgradeState(entry).IsEligible)
            {
                count++;
            }
        }

        _eligibleUpgradeCount = count;
    }

    private async Task RemoveDatabase(DatabaseEntry entry) =>
        await ConfirmAndRemoveAsync([entry.FileName], FocusTarget.SameRowName, FocusTarget.SameRowName);

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

        foreach (var fileName in succeeded)
        {
            _selectedForBulk.Remove(fileName);
        }

        RecomputeEligibleCount();
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

        // Auto-exit on a fully-clean bulk delete only: any failure leaves the
        // failed rows selected so the user can retry without re-selecting them.
        if (_isSelectionModeActive && succeeded.Count > 0 && failed.Count == 0)
        {
            ExitSelectionMode();
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

    private void SelectAll()
    {
        foreach (var entry in DatabaseService.Entries)
        {
            _selectedForBulk.Add(entry.FileName);
        }

        RecomputeEligibleCount();
        UpdateSelectionAnnouncement();
    }

    private Dictionary<string, DatabaseEntry> SnapshotEntriesByFileName() =>
        DatabaseService.Entries.ToDictionary(
            e => e.FileName,
            StringComparer.OrdinalIgnoreCase);

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
        bool added = _selectedForBulk.Add(fileName);

        if (!added) { _selectedForBulk.Remove(fileName); }

        RecomputeEligibleCount();
        UpdateSelectionAnnouncement();
    }

    private void ToggleSelectionMode()
    {
        if (_isSelectionModeActive)
        {
            ExitSelectionMode();
        }
        else
        {
            _isSelectionModeActive = true;
        }
    }

    private void UpdateSelectionAnnouncement()
    {
        int count = _selectedForBulk.Count;
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

    private readonly record struct CancellableBatch(UpgradeBatchId BatchId, Action Cancel);
}
