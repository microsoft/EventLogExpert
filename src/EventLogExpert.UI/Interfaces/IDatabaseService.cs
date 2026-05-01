// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Interfaces;

public interface IDatabaseService
{
    event EventHandler? EntriesChanged;

    IReadOnlyList<DatabaseEntry> Entries { get; }

    /// <summary>
    ///     Completes once the ctor-initiated <see cref="ClassifyEntriesAsync" /> pass finishes.
    ///     Guaranteed to never fault or cancel — failures (per-entry or infrastructure) are absorbed
    ///     and surface as <see cref="DatabaseStatus.ClassificationFailed" /> on the affected entry.
    ///     Consumers (e.g., <c>EventLogEffects.HandleOpenLog</c>) await this on every log open;
    ///     a faulted task here would poison every subsequent open for the rest of app lifetime.
    /// </summary>
    Task InitialClassificationTask { get; }

    /// <summary>
    ///     Inspects each <see cref="DatabaseEntry" /> on disk (in read-only mode without auto-creating a schema) and
    ///     updates its <see cref="DatabaseEntry.Status" /> from the on-disk schema version. Per-entry classification
    ///     failures (e.g., file locked, file missing) leave that entry at its current status and continue with the rest.
    ///     Raises <see cref="EntriesChanged" /> exactly once when the pass completes.
    /// </summary>
    Task ClassifyEntriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deletes the <c>-journal</c>/<c>-wal</c>/<c>-shm</c> sidecars and any <c>.upgrade.bak</c> backup,
    ///     then deletes the main database file last (so a partial failure leaves the entry visible to
    ///     <see cref="Refresh" /> and retry-able), then removes the entry from <see cref="Entries" />.
    ///     Returns <c>false</c> on any IO failure (logged); the entry is left in place so the caller can
    ///     surface the error and retry. User-created <c>.bak</c> files (not the <c>.upgrade.bak</c>
    ///     marker) are never touched.
    /// </summary>
    Task<bool> DeleteEntryWithBackupAsync(string fileName, CancellationToken cancellationToken = default);

    Task<ImportResult> ImportAsync(IEnumerable<string> sourceFilePaths, CancellationToken cancellationToken = default);

    void MarkStatus(string fileName, DatabaseStatus status);

    void Refresh();

    void Remove(string fileName);

    /// <summary>
    ///     Deletes any stale <c>-journal</c>/<c>-wal</c>/<c>-shm</c> sidecars first (their presence
    ///     paired with a restored older main would let SQLite roll forward stale transactions into
    ///     the restored database on next open), then restores the main database file from its
    ///     <c>.upgrade.bak</c> backup, then deletes the backup. The backup is preserved if any sidecar
    ///     cleanup fails so the caller can retry. Re-classifies the entry so consumers observe the
    ///     post-restore state via <see cref="EntriesChanged" />. Returns <c>false</c> on any IO
    ///     failure (logged).
    /// </summary>
    Task<bool> RestoreFromBackupAsync(string fileName, CancellationToken cancellationToken = default);

    void Toggle(string fileName);
}
