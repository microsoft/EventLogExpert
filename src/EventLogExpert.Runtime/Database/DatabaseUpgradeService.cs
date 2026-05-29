// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Maintenance;
using EventLogExpert.Provider.Schema;
using EventLogExpert.Runtime.Database.Upgrade;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Threading.Channels;

namespace EventLogExpert.Runtime.Database;

internal sealed class DatabaseUpgradeService : IAsyncDisposable
{
    private readonly Task _consumerTask;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly Task _initialClassificationTask;
    private readonly IProviderDatabaseMaintenance _maintenance;
    private readonly ConcurrentDictionary<UpgradeBatchId, UpgradeBatch> _queuedBatches = new();
    private readonly DatabaseRegistry _registry;
    private readonly ITraceLogger _traceLogger;
    private readonly Channel<UpgradeBatch> _upgradeQueue = Channel.CreateUnbounded<UpgradeBatch>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private int _disposed;
    private int _queuedBatchCount;

    public DatabaseUpgradeService(
        DatabaseRegistry registry,
        Task initialClassificationTask,
        IProviderDatabaseMaintenance maintenance,
        ITraceLogger traceLogger)
    {
        _registry = registry;
        _initialClassificationTask = initialClassificationTask;
        _maintenance = maintenance;
        _traceLogger = traceLogger;

        _consumerTask = Task.Run(ConsumeUpgradeQueueAsync);
    }

    public event EventHandler<UpgradeBatchCompletedEventArgs>? UpgradeBatchCompleted;

    public event EventHandler<UpgradeBatchProgressEventArgs>? UpgradeBatchProgress;

    public event EventHandler<UpgradeBatchStartedEventArgs>? UpgradeBatchStarted;

    public int QueuedBatchCount => Volatile.Read(ref _queuedBatchCount);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }

        _upgradeQueue.Writer.TryComplete();

        try
        {
            await _disposeCts.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DatabaseRegistry.SafeLog(() => _traceLogger.Warning(
                $"{nameof(DatabaseUpgradeService)}.{nameof(DisposeAsync)}: cancellation source faulted: {ex}"));
        }

        try
        {
            await _consumerTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on disposal.
        }
        catch (Exception ex)
        {
            DatabaseRegistry.SafeLog(() => _traceLogger.Warning(
                $"{nameof(DatabaseUpgradeService)}.{nameof(DisposeAsync)}: consumer task faulted: {ex}"));
        }

        _disposeCts.Dispose();
    }

    /// <summary>Snapshot of enqueued batches not yet picked up by the consumer.</summary>
    public IReadOnlyList<QueuedBatchInfo> SnapshotQueuedBatches()
    {
        var snapshot = _queuedBatches.Values;
        var list = new List<QueuedBatchInfo>(snapshot.Count);

        foreach (var batch in snapshot)
        {
            var fileNames = batch.Entries
                .Select(entry => entry.FileName)
                .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

            Action cancel = () =>
            {
                try { batch.BatchCts.Cancel(); }
                catch (ObjectDisposedException) { /* Already disposed; cancellation is moot. */ }
            };

            list.Add(new QueuedBatchInfo(batch.Id, batch.Scope, fileNames, cancel));
        }

        return list;
    }

    public async Task<UpgradeBatchResult> UpgradeBatchAsync(
        IReadOnlyList<string> fileNames,
        UpgradeProgressScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileNames);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var dedupedNames = DedupePreservingOrder(fileNames);

        if (cancellationToken.IsCancellationRequested)
        {
            return new UpgradeBatchResult([], dedupedNames, []);
        }

        try
        {
            await _initialClassificationTask.ConfigureAwait(false);
        }
        catch
        {
            // InitialClassificationTask is contractually never-faulting; defensive try/catch only.
        }

        var acceptable = new List<DatabaseEntry>(dedupedNames.Count);
        var rejected = new List<UpgradeFailure>();

        var snapshot = _registry.SnapshotEntries();
        var byName = new Dictionary<string, DatabaseEntry>(snapshot.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in snapshot)
        {
            byName[entry.FileName] = entry;
        }

        foreach (var fileName in dedupedNames)
        {
            if (!byName.TryGetValue(fileName, out var entry))
            {
                rejected.Add(new UpgradeFailure(fileName, "Entry not found"));
                continue;
            }

            if (entry.BackupExists)
            {
                rejected.Add(new UpgradeFailure(
                    fileName,
                    "Recovery required — resolve via Settings or recovery prompt first"));

                continue;
            }

            if (entry.Status is not (DatabaseStatus.UpgradeRequired or DatabaseStatus.UpgradeFailed))
            {
                rejected.Add(new UpgradeFailure(
                    fileName,
                    $"Cannot upgrade entry in status '{entry.Status}'"));

                continue;
            }

            acceptable.Add(entry);
        }

        if (acceptable.Count == 0)
        {
            return new UpgradeBatchResult([], [], rejected);
        }

        var batchCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);

        var tcs = new TaskCompletionSource<UpgradeBatchResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        var batch = new UpgradeBatch(
            UpgradeBatchId.Create(),
            scope,
            acceptable,
            tcs,
            batchCts,
            rejected);

        Interlocked.Increment(ref _queuedBatchCount);
        _queuedBatches[batch.Id] = batch;

        if (_upgradeQueue.Writer.TryWrite(batch))
        {
            return await tcs.Task.ConfigureAwait(false);
        }

        _queuedBatches.TryRemove(batch.Id, out _);
        Interlocked.Decrement(ref _queuedBatchCount);
        batchCts.Dispose();

        return new UpgradeBatchResult([], acceptable.Select(entry => entry.FileName).ToArray(), rejected);
    }

    private static IReadOnlyList<string> DedupePreservingOrder(IReadOnlyList<string> source)
    {
        var seen = new HashSet<string>(source.Count, StringComparer.OrdinalIgnoreCase);
        var deduped = new List<string>(source.Count);

        foreach (var item in source)
        {
            if (seen.Add(item))
            {
                deduped.Add(item);
            }
        }

        return deduped;
    }

    private async Task ConsumeUpgradeQueueAsync()
    {
        try
        {
            await foreach (var batch in _upgradeQueue.Reader.ReadAllAsync(_disposeCts.Token).ConfigureAwait(false))
            {
                await ProcessBatchAsync(batch).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on dispose.
        }
        catch (Exception ex)
        {
            DatabaseRegistry.SafeLog(() => _traceLogger.Warning(
                $"{nameof(DatabaseUpgradeService)}.{nameof(ConsumeUpgradeQueueAsync)}: consumer faulted: {ex}"));
        }
        finally
        {
            DrainPendingBatches();
        }
    }

    private void DrainPendingBatches()
    {
        while (_upgradeQueue.Reader.TryRead(out var batch))
        {
            _queuedBatches.TryRemove(batch.Id, out _);
            Interlocked.Decrement(ref _queuedBatchCount);

            try { batch.BatchCts.Cancel(); }
            catch (Exception)
            { /* CTS may already be disposed; cancellation moot. */
            }

            try { batch.BatchCts.Dispose(); }
            catch (Exception)
            { /* Already disposed; ignore. */
            }

            batch.Tcs.TrySetException(
                new OperationCanceledException("DatabaseService disposed before batch processed."));
        }
    }

    private async Task ProcessBatchAsync(UpgradeBatch batch)
    {
        // _queuedBatches.TryRemove deferred until after SafeRaise UpgradeBatchStarted: gives an
        // explicit superset overlap (file findable in queued snapshot OR active progress at all
        // times) instead of a transition gap the remove flow's lookup could miss.
        Interlocked.Decrement(ref _queuedBatchCount);

        var succeeded = new List<string>(batch.Entries.Count);
        var cancelled = new List<string>(batch.Entries.Count);
        var failed = new List<UpgradeFailure>(batch.AlreadyRejected.Count + batch.Entries.Count);
        failed.AddRange(batch.AlreadyRejected);
        var wasCancelled = false;

        try
        {
            _registry.SafeRaise(
                UpgradeBatchStarted,
                this,
                new UpgradeBatchStartedEventArgs(batch.Id, batch.Scope, batch.Entries.Count, batch.BatchCts)
                {
                    FileNames = batch.Entries.Select(entry => entry.FileName).ToArray()
                },
                nameof(UpgradeBatchStarted));

            _queuedBatches.TryRemove(batch.Id, out _);

            for (var i = 0; i < batch.Entries.Count; i++)
            {
                var entry = batch.Entries[i];

                if (batch.BatchCts.Token.IsCancellationRequested)
                {
                    wasCancelled = true;

                    for (var j = i; j < batch.Entries.Count; j++)
                    {
                        cancelled.Add(batch.Entries[j].FileName);
                    }

                    break;
                }

                try
                {
                    await UpgradeAsync(entry.FileName, i + 1, batch.Id, batch.BatchCts.Token).ConfigureAwait(false);

                    succeeded.Add(entry.FileName);
                }
                catch (OperationCanceledException) when (batch.BatchCts.Token.IsCancellationRequested)
                {
                    wasCancelled = true;
                    cancelled.Add(entry.FileName);

                    for (var j = i + 1; j < batch.Entries.Count; j++)
                    {
                        cancelled.Add(batch.Entries[j].FileName);
                    }

                    break;
                }
                catch (UpgradeRollbackFailedException ex)
                {
                    failed.Add(new UpgradeFailure(entry.FileName, ex.Message));

                    if (!batch.BatchCts.Token.IsCancellationRequested)
                    {
                        continue;
                    }

                    wasCancelled = true;

                    for (var j = i + 1; j < batch.Entries.Count; j++)
                    {
                        cancelled.Add(batch.Entries[j].FileName);
                    }

                    break;
                }
                catch (Exception ex)
                {
                    var message = ex is DatabaseUpgradeException dbEx ? dbEx.Reason : ex.Message;
                    failed.Add(new UpgradeFailure(entry.FileName, message));

                    DatabaseRegistry.SafeLog(() => _traceLogger.Warning(
                        $"{nameof(DatabaseUpgradeService)}.{nameof(ProcessBatchAsync)}: '{entry.FileName}' upgrade threw: {ex}"));
                }
            }

            var result = new UpgradeBatchResult(succeeded, cancelled, failed);

            _registry.SafeRaise(
                UpgradeBatchCompleted,
                this,
                new UpgradeBatchCompletedEventArgs(batch.Id, result, wasCancelled),
                nameof(UpgradeBatchCompleted));

            batch.Tcs.TrySetResult(result);
        }
        catch (Exception ex)
        {
            DatabaseRegistry.SafeLog(() => _traceLogger.Warning(
                $"{nameof(DatabaseUpgradeService)}.{nameof(ProcessBatchAsync)}: unhandled batch error: {ex}"));

            batch.Tcs.TrySetException(ex);
        }
        finally
        {
            try { batch.BatchCts.Dispose(); }
            catch (Exception)
            { /* Already disposed; ignore. */
            }
        }
    }

    private async Task UpgradeAsync(
        string fileName,
        int position,
        UpgradeBatchId batchId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var reservation = _registry.ReserveFileOperation(fileName, nameof(UpgradeAsync));

        var entry = _registry.LookupEntryOrThrow(fileName, nameof(UpgradeAsync));

        if (entry.Status is not (DatabaseStatus.UpgradeRequired or DatabaseStatus.UpgradeFailed))
        {
            throw new InvalidOperationException($"Cannot upgrade entry in status '{entry.Status}'");
        }

        if (entry.BackupExists)
        {
            throw new InvalidOperationException("Recovery required — backup exists");
        }

        var backupPath = entry.FullPath + DatabaseFileOperations.UpgradeBackupSuffix;

        if (File.Exists(backupPath))
        {
            _registry.UpdateEntryStatusAndBackup(fileName, entry.Status, true);

            throw new InvalidOperationException("Recovery required — .upgrade.bak already present");
        }

        await Task.Run(
                () =>
                {
                    var backupCreated = false;
                    var migrationStarted = false;
                    var migrationCompleted = false;

                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        _maintenance.WalCheckpoint(entry.FullPath);

                        cancellationToken.ThrowIfCancellationRequested();

                        _registry.SafeRaise(
                            UpgradeBatchProgress,
                            this,
                            new UpgradeBatchProgressEventArgs(batchId, position, fileName, UpgradePhase.BackingUp),
                            nameof(UpgradeBatchProgress));

                        try
                        {
                            File.Copy(entry.FullPath, backupPath, false);
                        }
                        catch (IOException ex) when (File.Exists(backupPath))
                        {
                            _registry.UpdateEntryStatusAndBackup(fileName, entry.Status, true);

                            throw new InvalidOperationException(
                                "Recovery required — .upgrade.bak appeared during backup",
                                ex);
                        }

                        backupCreated = true;

                        cancellationToken.ThrowIfCancellationRequested();

                        _registry.SafeRaise(
                            UpgradeBatchProgress,
                            this,
                            new UpgradeBatchProgressEventArgs(batchId,
                                position,
                                fileName,
                                UpgradePhase.MigratingSchema),
                            nameof(UpgradeBatchProgress));

                        migrationStarted = true;

                        _maintenance.PerformUpgrade(entry.FullPath);

                        migrationCompleted = true;

                        _registry.SafeRaise(
                            UpgradeBatchProgress,
                            this,
                            new UpgradeBatchProgressEventArgs(batchId, position, fileName, UpgradePhase.Verifying),
                            nameof(UpgradeBatchProgress));

                        if (!DatabaseFileOperations.VerifyEntryReady(entry.FullPath, _maintenance, _traceLogger))
                        {
                            throw new InvalidOperationException("Upgrade verification failed");
                        }

                        if (!DatabaseFileOperations.TryDeleteFile(backupPath, _traceLogger, nameof(UpgradeAsync)))
                        {
                            _registry.UpdateEntryStatusAndBackup(fileName, DatabaseStatus.Ready, true);

                            throw new UpgradeCleanupFailedException(
                                "Upgrade succeeded but backup cleanup failed; .upgrade.bak remains on disk");
                        }

                        _registry.UpdateEntryStatusAndBackup(fileName, DatabaseStatus.Ready, false);
                    }
                    catch (UpgradeCleanupFailedException)
                    {
                        throw;
                    }
                    catch (OperationCanceledException)
                    {
                        if (!backupCreated || migrationCompleted) { throw; }

                        if (DatabaseFileOperations.RestoreFilesCore(entry, _traceLogger, nameof(UpgradeAsync)))
                        {
                            _registry.UpdateEntryStatusAndBackup(fileName, DatabaseStatus.UpgradeRequired, false);
                        }
                        else
                        {
                            _registry.UpdateEntryStatusAndBackup(fileName, DatabaseStatus.UpgradeFailed, true);

                            throw new UpgradeRollbackFailedException(
                                $"Cancellation rollback failed for '{fileName}'; .upgrade.bak remains on disk");
                        }

                        throw;
                    }
                    catch (Exception ex)
                    {
                        if (migrationStarted && !migrationCompleted)
                        {
                            if (!DatabaseFileOperations.RestoreFilesCore(entry, _traceLogger, nameof(UpgradeAsync)))
                            {
                                _registry.UpdateEntryStatusAndBackup(fileName, DatabaseStatus.UpgradeFailed, true);

                                throw new UpgradeRollbackFailedException(
                                    $"Migration failed and rollback also failed for '{fileName}': {(ex is DatabaseUpgradeException dbEx ? dbEx.Reason : ex.Message)}");
                            }

                            _registry.UpdateEntryStatusAndBackup(fileName, DatabaseStatus.UpgradeFailed, false);
                        }
                        else if (migrationCompleted)
                        {
                            if (!DatabaseFileOperations.RestoreFilesCore(entry, _traceLogger, nameof(UpgradeAsync)))
                            {
                                _registry.UpdateEntryStatusAndBackup(fileName, DatabaseStatus.UpgradeFailed, true);

                                throw new UpgradeRollbackFailedException(
                                    $"Verification or cleanup failed and rollback also failed for '{fileName}'");
                            }

                            _registry.UpdateEntryStatusAndBackup(fileName, DatabaseStatus.UpgradeFailed, false);
                        }
                        else if (backupCreated)
                        {
                            DatabaseFileOperations.TryDeleteFile(backupPath, _traceLogger, nameof(UpgradeAsync));
                        }

                        throw;
                    }
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed class UpgradeCleanupFailedException(string message) : Exception(message);

    private sealed class UpgradeRollbackFailedException(string message) : Exception(message);

    private sealed record UpgradeBatch(
        UpgradeBatchId Id,
        UpgradeProgressScope Scope,
        IReadOnlyList<DatabaseEntry> Entries,
        TaskCompletionSource<UpgradeBatchResult> Tcs,
        CancellationTokenSource BatchCts,
        IReadOnlyList<UpgradeFailure> AlreadyRejected);
}
