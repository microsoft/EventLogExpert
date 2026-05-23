// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Maintenance;
using EventLogExpert.Provider.Schema;
using EventLogExpert.Runtime.Common.Files;

namespace EventLogExpert.Runtime.Database;

internal sealed class DatabaseClassificationService
{
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
                    var perFile =
                        new Dictionary<string, (DatabaseStatus Status, bool BackupExists)>(StringComparer
                            .OrdinalIgnoreCase);

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

                            var state = _maintenance.CheckSchemaState(entry.FullPath);
                            var status = MapSchemaVersionToStatus(state.CurrentVersion);
                            var backupExists = ProbeOrCleanupBackup(entry, status);

                            perFile[entry.FileName] = (status, backupExists);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            perFile[entry.FileName] = (DatabaseStatus.ClassificationFailed, false);

                            DatabaseRegistry.SafeLog(() => _traceLogger.Warning(
                                $"{nameof(DatabaseClassificationService)}.{nameof(ClassifyEntriesAsync)} failed to classify '{entry.FileName}': {ex}"));
                        }
                    }

                    return perFile;
                },
                cancellationToken)
            .ConfigureAwait(false);

        _entryStore.ApplyClassificationResults(statuses);
    }

    private static DatabaseStatus MapSchemaVersionToStatus(int currentVersion) =>
        currentVersion switch
        {
            DatabaseSchemaVersion.Current => DatabaseStatus.Ready,
            3 => DatabaseStatus.UpgradeRequired,
            1 or 2 => DatabaseStatus.ObsoleteSchema,
            _ => DatabaseStatus.UnrecognizedSchema,
        };

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
