// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Interfaces;

public interface IDatabaseService : IAsyncDisposable
{
    event EventHandler? EntriesChanged;

    /// <summary>
    ///     Raised once per batch after every entry has either succeeded, been cancelled, or failed. The companion
    ///     <see cref="UpgradeBatchResult" /> in the args carries the per-entry outcome plus any entries that were rejected
    ///     up-front (e.g., recovery-required) and never enqueued for actual upgrade work.
    /// </summary>
    event EventHandler<UpgradeBatchCompletedEventArgs>? UpgradeBatchCompleted;

    /// <summary>
    ///     Raised at each phase transition for the entry currently being upgraded (<see cref="UpgradePhase.BackingUp" />
    ///     → <see cref="UpgradePhase.MigratingSchema" /> → <see cref="UpgradePhase.Verifying" />). Every invocation carries
    ///     the batch identifier from the matching <see cref="UpgradeBatchStarted" />.
    /// </summary>
    event EventHandler<UpgradeBatchProgressEventArgs>? UpgradeBatchProgress;

    /// <summary>
    ///     Raised when the consumer task starts processing a queued upgrade batch (after any short-circuited /
    ///     fully-rejected batches have already returned). Subscribers receive the batch identifier (used to correlate with
    ///     later progress and completion events) and a <see cref="UpgradeBatchStartedEventArgs.Cancel" /> hook to request
    ///     cancellation. Always followed by either <see cref="UpgradeBatchProgress" /> + <see cref="UpgradeBatchCompleted" />
    ///     or just <see cref="UpgradeBatchCompleted" /> if every entry fails immediately.
    /// </summary>
    event EventHandler<UpgradeBatchStartedEventArgs>? UpgradeBatchStarted;

    IReadOnlyList<DatabaseEntry> Entries { get; }

    /// <summary>
    ///     Completes once the ctor-initiated <see cref="ClassifyEntriesAsync" /> pass finishes. Guaranteed to never fault
    ///     or cancel — failures (per-entry or infrastructure) are absorbed and surface as
    ///     <see cref="DatabaseStatus.ClassificationFailed" /> on the affected entry. Consumers (e.g.,
    ///     <c>EventLogEffects.HandleOpenLog</c>) await this on every log open; a faulted task here would poison every
    ///     subsequent open for the rest of app lifetime.
    /// </summary>
    Task InitialClassificationTask { get; }

    /// <summary>
    ///     Snapshot count of upgrade batches accepted by <see cref="UpgradeBatchAsync" /> that are waiting in the queue
    ///     and have not yet started processing. Excludes the batch (if any) currently being processed by the consumer task.
    ///     Updated atomically on enqueue/dequeue.
    /// </summary>
    int QueuedBatchCount { get; }

    /// <summary>
    ///     Inspects each <see cref="DatabaseEntry" /> on disk (in read-only mode without auto-creating a schema) and
    ///     updates its <see cref="DatabaseEntry.Status" /> from the on-disk schema version. Per-entry classification failures
    ///     (e.g., file locked, file missing) leave that entry at its current status and continue with the rest. Raises
    ///     <see cref="EntriesChanged" /> exactly once when the pass completes.
    /// </summary>
    Task ClassifyEntriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deletes the <c>-journal</c>/<c>-wal</c>/<c>-shm</c> sidecars and any <c>.upgrade.bak</c> backup, then deletes
    ///     the main database file last (so a partial failure leaves the entry visible to <see cref="Refresh" /> and
    ///     retry-able), then removes the entry from <see cref="Entries" />. Returns <c>false</c> on any IO failure (logged);
    ///     the entry is left in place so the caller can surface the error and retry. User-created <c>.bak</c> files (not the
    ///     <c>.upgrade.bak</c> marker) are never touched.
    /// </summary>
    Task<bool> DeleteEntryWithBackupAsync(string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reads a <c>.zip</c> archive without extracting and returns the names of contained <c>.db</c> entries (the same
    ///     entries that <see cref="ImportAsync(IEnumerable{string}, CancellationToken)" /> would extract). Used by callers
    ///     (e.g., the settings dialog) to pre-scan zip contents for filename conflicts before invoking the import. A malformed
    ///     or unreadable archive is logged and returns an empty list — the actual import call will surface the failure as an
    ///     <see cref="ImportFailure" /> in <see cref="ImportResult.Failures" />.
    /// </summary>
    Task<IReadOnlyList<string>> EnumerateZipDbEntryNamesAsync(
        string sourceZipPath,
        CancellationToken cancellationToken = default);

    Task<ImportResult> ImportAsync(IEnumerable<string> sourceFilePaths, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Imports the supplied database files, skipping any whose file name appears in <paramref name="skipFileNames" />
    ///     (case-insensitive). Skipped files are not counted as imports, do not appear as failures, and do not affect existing
    ///     on-disk databases — the caller is expected to have already shown the user a conflict-resolution prompt and to have
    ///     populated <paramref name="skipFileNames" /> with the user's "skip" decisions. Files NOT in the skip set are
    ///     copied/extracted with overwrite semantics (per-name reservations gate against concurrent upgrade/restore/remove on
    ///     the same name). Newly-added entries (file names not previously present in <see cref="Entries" />) are persisted as
    ///     disabled before classification so the published snapshot is correct on the first <see cref="EntriesChanged" />
    ///     notification. Re-imports of existing names preserve the user's prior <see cref="DatabaseEntry.IsEnabled" /> choice.
    ///     After classification any imported entries (new or overwritten) whose status is
    ///     <see cref="DatabaseStatus.UpgradeRequired" /> are auto-upgraded via a single <see cref="UpgradeBatchAsync" /> call
    ///     with <see cref="UpgradeProgressScope.Background" />. Per-entry upgrade failures are mapped to
    ///     <see cref="ImportResult.UpgradeFailures" />; cancelled upgrades roll back to
    ///     <see cref="DatabaseStatus.UpgradeRequired" /> and are not surfaced as failures.
    /// </summary>
    Task<ImportResult> ImportAsync(
        IEnumerable<string> sourceFilePaths,
        IReadOnlySet<string> skipFileNames,
        CancellationToken cancellationToken = default);

    void MarkStatus(string fileName, DatabaseStatus status);

    void Refresh();

    void Remove(string fileName);

    /// <summary>
    ///     Deletes any stale <c>-journal</c>/<c>-wal</c>/<c>-shm</c> sidecars first (their presence paired with a
    ///     restored older main would let SQLite roll forward stale transactions into the restored database on next open), then
    ///     restores the main database file from its <c>.upgrade.bak</c> backup, then deletes the backup. The backup is
    ///     preserved if any sidecar cleanup fails so the caller can retry. Re-classifies the entry so consumers observe the
    ///     post-restore state via <see cref="EntriesChanged" />. Returns <c>false</c> on any IO failure (logged).
    /// </summary>
    Task<bool> RestoreFromBackupAsync(string fileName, CancellationToken cancellationToken = default);

    void Toggle(string fileName);

    /// <summary>
    ///     Enqueues an upgrade batch for the named entries and returns a task that completes when the batch has been
    ///     processed. Batches are processed sequentially in FIFO order by a single consumer task; callers may safely invoke
    ///     this concurrently from multiple threads. Entries are filtered up-front: those whose status is not
    ///     <see cref="DatabaseStatus.UpgradeRequired" /> or <see cref="DatabaseStatus.UpgradeFailed" />, or whose
    ///     <see cref="DatabaseEntry.BackupExists" /> is true (recovery required), or that are unknown to the service, are
    ///     returned in the result's <see cref="UpgradeBatchResult.Failed" /> list with a descriptive reason and never reach
    ///     the consumer. If every entry is filtered out, the call returns immediately without raising any events. Cancelling
    ///     <paramref name="cancellationToken" /> after the batch has been enqueued causes the in-flight entry to roll back
    ///     from its <c>.upgrade.bak</c> backup; previously completed entries in the same batch keep their upgraded state.
    /// </summary>
    Task<UpgradeBatchResult> UpgradeBatchAsync(
        IReadOnlyList<string> fileNames,
        UpgradeProgressScope scope,
        CancellationToken cancellationToken = default);
}
