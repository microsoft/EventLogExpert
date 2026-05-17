// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Database.Upgrade;

namespace EventLogExpert.Runtime.Database;

public interface IDatabaseService : IAsyncDisposable
{
    event EventHandler? EntriesChanged;

    event EventHandler<UpgradeBatchCompletedEventArgs>? UpgradeBatchCompleted;

    event EventHandler<UpgradeBatchProgressEventArgs>? UpgradeBatchProgress;

    event EventHandler<UpgradeBatchStartedEventArgs>? UpgradeBatchStarted;

    IReadOnlyList<DatabaseEntry> Entries { get; }

    Task InitialClassificationTask { get; }

    int QueuedBatchCount { get; }

    Task ClassifyEntriesAsync(CancellationToken cancellationToken = default);

    Task<bool> DeleteEntryWithBackupAsync(string fileName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> EnumerateZipDbEntryNamesAsync(
        string sourceZipPath,
        CancellationToken cancellationToken = default);

    Task<ImportResult> ImportAsync(IEnumerable<string> sourceFilePaths, CancellationToken cancellationToken = default);

    Task<ImportResult> ImportAsync(
        IEnumerable<string> sourceFilePaths,
        IReadOnlySet<string> skipFileNames,
        CancellationToken cancellationToken = default);

    void MarkStatus(string fileName, DatabaseStatus status);

    void Refresh();

    Task RemoveAsync(
        string fileName,
        Func<CancellationToken, Task>? prepareForDeletionAsync = null,
        CancellationToken cancellationToken = default);

    Task<bool> RestoreFromBackupAsync(string fileName, CancellationToken cancellationToken = default);

    void Toggle(string fileName);

    Task<UpgradeBatchResult> UpgradeBatchAsync(
        IReadOnlyList<string> fileNames,
        UpgradeProgressScope scope,
        CancellationToken cancellationToken = default);
}
