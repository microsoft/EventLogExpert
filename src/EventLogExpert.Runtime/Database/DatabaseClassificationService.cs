// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Logging;
using EventLogExpert.Eventing.ProviderDatabase;
using EventLogExpert.ProviderDatabase;
using EventLogExpert.Runtime.Common.Files;

namespace EventLogExpert.Runtime.Database;

internal sealed class DatabaseClassificationService
{
    private readonly DatabaseEntryStore _entryStore;
    private readonly FileLocationOptions _fileLocationOptions;
    private readonly ITraceLogger _traceLogger;

    public DatabaseClassificationService(
        DatabaseEntryStore entryStore,
        FileLocationOptions fileLocationOptions,
        ITraceLogger traceLogger)
    {
        _entryStore = entryStore;
        _fileLocationOptions = fileLocationOptions;
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
                    var perFile = new Dictionary<string, (DatabaseStatus Status, bool BackupExists)>(StringComparer.OrdinalIgnoreCase);

                    foreach (var entry in snapshot)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            if (!File.Exists(entry.FullPath) || new FileInfo(entry.FullPath).Length == 0)
                            {
                                perFile[entry.FileName] = (DatabaseStatus.UnrecognizedSchema, false);

                                continue;
                            }

                            using var context = new ProviderDbContext(
                                entry.FullPath,
                                readOnly: false,
                                ensureCreated: false,
                                logger: _traceLogger);

                            var state = context.IsUpgradeNeeded();
                            var status = MapSchemaVersionToStatus(state.CurrentVersion);
                            var backupExists = ProbeOrCleanupBackup(entry, status);

                            perFile[entry.FileName] = (status, backupExists);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            perFile[entry.FileName] = (DatabaseStatus.ClassificationFailed, false);

                            DatabaseEntryStore.SafeLog(() => _traceLogger.Warning(
                                $"{nameof(DatabaseClassificationService)}.{nameof(ClassifyEntriesAsync)} failed to classify '{entry.FileName}': {ex}"));
                        }
                    }

                    return perFile;
                },
                cancellationToken)
            .ConfigureAwait(false);

        _entryStore.ApplyClassificationResults(statuses);
    }

    private async Task StartInitialClassificationAsync()
    {
        try
        {
            await ClassifyEntriesAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DatabaseEntryStore.SafeLog(() => _traceLogger.Warning(
                $"{nameof(DatabaseClassificationService)}.{nameof(StartInitialClassificationAsync)}: initial classification failed: {ex}"));
        }
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
                DatabaseEntryStore.SafeLog(() => _traceLogger.Warning(
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
                DatabaseEntryStore.SafeLog(() => _traceLogger.Warning(
                    $"{nameof(DatabaseClassificationService)}.{nameof(ProbeOrCleanupBackup)} stale .upgrade.bak cleanup failed for '{entry.FileName}': {ex}"));
            }
        }

        return false;
    }

    private static DatabaseStatus MapSchemaVersionToStatus(int currentVersion) =>
        currentVersion switch
        {
            ProviderDatabaseSchemaVersion.Current => DatabaseStatus.Ready,
            3 => DatabaseStatus.UpgradeRequired,
            1 or 2 => DatabaseStatus.ObsoleteSchema,
            _ => DatabaseStatus.UnrecognizedSchema,
        };
}
