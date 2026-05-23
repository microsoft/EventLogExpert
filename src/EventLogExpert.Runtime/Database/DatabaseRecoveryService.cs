// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Maintenance;
using EventLogExpert.Runtime.Common.Files;

namespace EventLogExpert.Runtime.Database;

internal sealed class DatabaseRecoveryService(
    DatabaseRegistry registry,
    DatabaseClassificationService classificationService,
    FileLocationOptions fileLocationOptions,
    IProviderDatabaseMaintenance maintenance,
    ITraceLogger traceLogger)
{
    private readonly DatabaseClassificationService _classificationService = classificationService;
    private readonly FileLocationOptions _fileLocationOptions = fileLocationOptions;
    private readonly IProviderDatabaseMaintenance _maintenance = maintenance;
    private readonly DatabaseRegistry _registry = registry;
    private readonly ITraceLogger _traceLogger = traceLogger;

    public async Task<bool> DeleteEntryWithBackupAsync(string fileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var reservation = _registry.ReserveFileOperation(fileName, nameof(DeleteEntryWithBackupAsync));

        var entry = _registry.LookupEntryOrThrow(fileName, nameof(DeleteEntryWithBackupAsync));

        var success = await Task.Run(() =>
                DatabaseFileOperations.DeleteFilesCore(entry, _traceLogger, nameof(DeleteEntryWithBackupAsync)))
            .ConfigureAwait(false);

        if (!success) { return false; }

        _registry.TryRemoveAndPersist(fileName);

        return true;
    }

    public async Task RemoveAsync(
        string fileName,
        Func<CancellationToken, Task>? prepareForDeletionAsync = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var reservation = _registry.ReserveFileOperation(fileName, nameof(RemoveAsync));

        // Wait for initial classification so callers don't race with the background scan
        // (which can mutate IsEnabled / Status / BackupExists for this entry mid-flight).
        try
        {
            await _classificationService.InitialClassificationTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DatabaseRegistry.SafeLog(() => _traceLogger.Trace(
                $"{nameof(DatabaseRecoveryService)}.{nameof(RemoveAsync)}: InitialClassificationTask faulted unexpectedly: {ex}"));
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Step 1: disable the entry so EventResolver can no longer pick this database up
        // on its next construction. Capture wasEnabled so we can restore on rollback if a
        // later phase fails.
        var (found, wasEnabled) = _registry.SetEnabled(fileName, false, true);

        if (!found)
        {
            throw new InvalidOperationException(
                $"{nameof(DatabaseRecoveryService)}.{nameof(RemoveAsync)}: no entry found with file name '{fileName}'.");
        }

        // Step 2: let the caller close any open log views before we touch the file.
        // Always invoked when a callback is supplied — a previously-disabled entry can still
        // be referenced by IEventResolver instances constructed before the disable (e.g., the
        // user disabled this DB and declined the modal-close reload prompt). Those resolvers
        // hold DbContexts whose connections are NOT in the pool until the per-event service
        // scope disposes, so ClearAllPools below cannot release them and File.Delete races.
        if (prepareForDeletionAsync is not null)
        {
            try
            {
                await prepareForDeletionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                DatabaseRegistry.SafeLog(() => _traceLogger.Warning(
                    $"{nameof(DatabaseRecoveryService)}.{nameof(RemoveAsync)}: prepare-for-deletion callback failed: {ex}"));

                _registry.RestoreEnabledIfChanged(fileName, wasEnabled);

                throw;
            }
        }

        // Step 3: clear the SQLite connection pool and delete the files. Pool clear is
        // required so the OS file handles backing the pooled connections are closed; the
        // closed-but-pooled connections still hold the file handle without
        // FILE_SHARE_DELETE on Windows.
        try
        {
            _maintenance.PrepareForFileDeletion();

            DatabaseFileOperations.DeleteDatabaseFiles(_fileLocationOptions.DatabasePath, fileName);
        }
        catch (Exception ex)
        {
            DatabaseRegistry.SafeLog(() => _traceLogger.Warning(
                $"{nameof(DatabaseRecoveryService)}.{nameof(RemoveAsync)}: deleting files for '{fileName}' failed: {ex}"));

            _registry.RestoreEnabledIfChanged(fileName, wasEnabled);

            throw;
        }

        // Step 4: drop the entry from the snapshot.
        _registry.TryRemoveAndPersist(fileName);
    }

    public async Task<bool> RestoreFromBackupAsync(string fileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var reservation = _registry.ReserveFileOperation(fileName, nameof(RestoreFromBackupAsync));

        var entry = _registry.LookupEntryOrThrow(fileName, nameof(RestoreFromBackupAsync));

        var success = await Task
            .Run(() => DatabaseFileOperations.RestoreFilesCore(entry, _traceLogger, nameof(RestoreFromBackupAsync)))
            .ConfigureAwait(false);

        if (!success) { return false; }

        try
        {
            await _classificationService.ClassifyEntriesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DatabaseRegistry.SafeLog(() => _traceLogger.Warning(
                $"{nameof(DatabaseRecoveryService)}.{nameof(RestoreFromBackupAsync)} post-restore classification failed: {ex}"));
        }

        return true;
    }
}
