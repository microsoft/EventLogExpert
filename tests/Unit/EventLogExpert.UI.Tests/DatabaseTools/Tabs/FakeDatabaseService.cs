// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.Database.Upgrade;

namespace EventLogExpert.UI.Tests.DatabaseTools.Tabs;

/// <summary>
///     Test-only fake of <see cref="IDatabaseService" /> that exposes real CLR field-like events and their
///     invocation-list counts via <see cref="EntriesChangedHandlerCount" /> /
///     <see cref="UpgradeBatchCompletedHandlerCount" />. NSubstitute proxies do not surface <c>GetInvocationList()</c> on
///     intercepted events, so a hand-rolled fake is the cleanest way to assert unsubscribe coverage from the
///     disposal-regression test.
/// </summary>
internal sealed class FakeDatabaseService : IDatabaseService
{
    public event EventHandler? EntriesChanged;

    public event EventHandler<UpgradeBatchCompletedEventArgs>? UpgradeBatchCompleted;

    public IReadOnlyList<DatabaseEntry> Entries { get; set; } = [];

    public int EntriesChangedHandlerCount => EntriesChanged?.GetInvocationList().Length ?? 0;

    public Task InitialClassificationTask { get; set; } = Task.CompletedTask;

    public int QueuedBatchCount => 0;

    public int RestoreFromBackupCalls { get; private set; }

    public bool RestoreFromBackupReturnValue { get; set; } = true;

    public int RetryClassificationCalls { get; private set; }

    public int UpgradeBatchCompletedHandlerCount => UpgradeBatchCompleted?.GetInvocationList().Length ?? 0;

    public Task ClassifyEntriesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<bool> DeleteEntryWithBackupAsync(string fileName, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public Task<IReadOnlyList<string>> EnumerateZipDbEntryNamesAsync(
        string sourceZipPath,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public Task<ImportResult> ImportAsync(
        IEnumerable<string> sourceFilePaths,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ImportResult(0, [], []));

    public Task<ImportResult> ImportAsync(
        IEnumerable<string> sourceFilePaths,
        IReadOnlySet<string> skipFileNames,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ImportResult(0, [], []));

    public void MarkStatus(string fileName, DatabaseStatus status) { }

    public void RaiseEntriesChanged() => EntriesChanged?.Invoke(this, EventArgs.Empty);

    public void RaiseUpgradeBatchCompleted(UpgradeBatchCompletedEventArgs args) =>
        UpgradeBatchCompleted?.Invoke(this, args);

    public void Refresh() { }

    public Task RemoveAsync(
        string fileName,
        Func<CancellationToken, Task>? prepareForDeletionAsync = null,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<bool> RestoreFromBackupAsync(string fileName, CancellationToken cancellationToken = default)
    {
        RestoreFromBackupCalls++;
        return Task.FromResult(RestoreFromBackupReturnValue);
    }

    public Task RetryClassificationAsync(string fileName, CancellationToken cancellationToken = default)
    {
        RetryClassificationCalls++;
        return Task.CompletedTask;
    }

    public void Toggle(string fileName) { }

    public Task<UpgradeBatchResult> UpgradeBatchAsync(
        IReadOnlyList<string> fileNames,
        UpgradeProgressScope scope,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new UpgradeBatchResult([], [], []));

#pragma warning disable CS0067 // Events not raised in test fakes; declared only to satisfy the interface.
    public event EventHandler<UpgradeBatchProgressEventArgs>? UpgradeBatchProgress;

    public event EventHandler<UpgradeBatchStartedEventArgs>? UpgradeBatchStarted;
#pragma warning restore CS0067
}
