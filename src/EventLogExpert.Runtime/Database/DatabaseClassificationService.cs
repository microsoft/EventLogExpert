// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Maintenance;
using EventLogExpert.Provider.Schema;
using EventLogExpert.Runtime.Common.Files;

namespace EventLogExpert.Runtime.Database;

internal sealed class DatabaseClassificationService
{
    // Distinct OS stamps surfaced per database. The read fetches ONE MORE than this visible cap (cap + 1) so the row UI
    // can distinguish "exactly cap" from "more than cap" and render a "cap+" indicator. Mirrors OsStampDisplayCap.
    private const int OsStampVisibleCap = 9;

    private readonly DatabaseRegistry _entryStore;
    private readonly FileLocationOptions _fileLocationOptions;
    private readonly IProviderDatabaseMaintenance _maintenance;
    private readonly ITraceLogger _traceLogger;

    public DatabaseClassificationService(
        DatabaseRegistry entryStore,
        FileLocationOptions fileLocationOptions,
        IProviderDatabaseMaintenance maintenance,
        ITraceLogger traceLogger)
    {
        _entryStore = entryStore;
        _fileLocationOptions = fileLocationOptions;
        _maintenance = maintenance;
        _traceLogger = traceLogger;
        InitialClassificationTask = StartInitialClassificationAsync();
    }

    public Task InitialClassificationTask { get; }

    public async Task ClassifyEntriesAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = _entryStore.SnapshotEntries();

        if (snapshot.Count == 0) { return; }

        var statuses = await Task.Run(
                () =>
                {
                    var perFile = new Dictionary<string, DatabaseClassificationResult>(StringComparer.OrdinalIgnoreCase);

                    foreach (var entry in snapshot)
                    {
                        perFile[entry.FileName] = ClassifyEntry(entry, cancellationToken);
                    }

                    return perFile;
                },
                cancellationToken)
            .ConfigureAwait(false);

        _entryStore.ApplyClassificationResults(statuses);
    }

    public async Task RetryClassificationAsync(string fileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entry = _entryStore.SnapshotEntries()
            .FirstOrDefault(e => string.Equals(e.FileName, fileName, StringComparison.OrdinalIgnoreCase));
        if (entry is null) { return; }

        try
        {
            _entryStore.MarkStatus(fileName, DatabaseStatus.NotClassified);
        }
        catch (InvalidOperationException)
        {
            // Entry was removed between SnapshotEntries() and MarkStatus(). Stale UI; nothing to retry.
            return;
        }

        var result = await Task.Run(
                () => ClassifyEntry(entry, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);

        _entryStore.ApplyClassificationResults(
            new Dictionary<string, DatabaseClassificationResult>(StringComparer.OrdinalIgnoreCase)
            {
                [fileName] = result
            });
    }

    private static DatabaseStatus MapSchemaStateToStatus(DatabaseSchemaState state)
    {
        if (!state.NeedsUpgrade) { return DatabaseStatus.Ready; }

        return state.CurrentVersion switch
        {
            1 or 2 => DatabaseStatus.ObsoleteSchema,
            DatabaseSchemaVersion.Unknown => DatabaseStatus.UnrecognizedSchema,
            _ => DatabaseStatus.UpgradeRequired,
        };
    }

    private DatabaseClassificationResult ClassifyEntry(
        DatabaseEntry entry,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        DatabaseStatus status;
        bool backupExists;

        try
        {
            if (!File.Exists(entry.FullPath) || new FileInfo(entry.FullPath).Length == 0)
            {
                return new DatabaseClassificationResult(DatabaseStatus.UnrecognizedSchema, false, []);
            }

            var state = _maintenance.CheckSchemaState(entry.FullPath);
            status = MapSchemaStateToStatus(state);
            backupExists = ProbeOrCleanupBackup(entry, status);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DatabaseRegistry.SafeLog(() => _traceLogger.Warning(
                $"{nameof(DatabaseClassificationService)}.{nameof(ClassifyEntry)} failed to classify '{entry.FileName}': {ex}"));

            return new DatabaseClassificationResult(DatabaseStatus.ClassificationFailed, false, []);
        }

        // Read OS stamps ONLY for a current-schema (Ready) database and in its OWN try: an obsolete schema may lack the
        // stamp columns, and a stamp-read failure must never demote an otherwise-classified database to
        // ClassificationFailed.
        var osStamps = status == DatabaseStatus.Ready ? ReadOsStamps(entry) : [];

        return new DatabaseClassificationResult(status, backupExists, osStamps);
    }

    private bool ProbeOrCleanupBackup(DatabaseEntry entry, DatabaseStatus status)
    {
        var backupPath = entry.FullPath + DatabaseFileOperations.UpgradeBackupSuffix;

        if (status == DatabaseStatus.UpgradeRequired)
        {
            try
            {
                return File.Exists(backupPath);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                DatabaseRegistry.SafeLog(() => _traceLogger.Warning(
                    $"{nameof(DatabaseClassificationService)}.{nameof(ProbeOrCleanupBackup)} probe failed for '{entry.FileName}': {ex}"));

                return true;
            }
        }

        if (status == DatabaseStatus.Ready)
        {
            try
            {
                File.Delete(backupPath);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                DatabaseRegistry.SafeLog(() => _traceLogger.Warning(
                    $"{nameof(DatabaseClassificationService)}.{nameof(ProbeOrCleanupBackup)} stale .upgrade.bak cleanup failed for '{entry.FileName}': {ex}"));
            }
        }

        return false;
    }

    private IReadOnlyList<ProviderDatabaseOsStamp> ReadOsStamps(DatabaseEntry entry)
    {
        try
        {
            return _maintenance.ReadDistinctSourceOsStamps(entry.FullPath, OsStampVisibleCap + 1);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DatabaseRegistry.SafeLog(() => _traceLogger.Warning(
                $"{nameof(DatabaseClassificationService)}.{nameof(ReadOsStamps)} failed for '{entry.FileName}': {ex.Message}"));

            return [];
        }
    }

    private async Task StartInitialClassificationAsync()
    {
        try
        {
            await ClassifyEntriesAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DatabaseRegistry.SafeLog(() => _traceLogger.Warning(
                $"{nameof(DatabaseClassificationService)}.{nameof(StartInitialClassificationAsync)}: initial classification failed: {ex}"));
        }
    }
}
