// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Logging;
using EventLogExpert.Eventing.ProviderDatabase;
using EventLogExpert.ProviderDatabase;
using Microsoft.Data.Sqlite;

namespace EventLogExpert.Runtime.Database;

internal static class DatabaseFileOperations
{
    public const string UpgradeBackupSuffix = ".upgrade.bak";

    public static bool RestoreFilesCore(DatabaseEntry entry, ITraceLogger traceLogger, string callerName)
    {
        var mainPath = entry.FullPath;
        var backupPath = mainPath + UpgradeBackupSuffix;

        if (!File.Exists(backupPath))
        {
            SafeLog(() => traceLogger.Warning(
                $"{nameof(DatabaseFileOperations)}.{nameof(RestoreFilesCore)}: '{backupPath}' missing; nothing to restore."));

            return false;
        }

        if (!TryDeleteFile(mainPath + "-journal", traceLogger, callerName)) { return false; }

        if (!TryDeleteFile(mainPath + "-wal", traceLogger, callerName)) { return false; }

        if (!TryDeleteFile(mainPath + "-shm", traceLogger, callerName)) { return false; }

        try
        {
            File.Copy(backupPath, mainPath, true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SafeLog(() => traceLogger.Warning(
                $"{nameof(DatabaseFileOperations)}.{nameof(RestoreFilesCore)}: copy from '{backupPath}' to '{mainPath}' failed: {ex}"));

            return false;
        }

        return TryDeleteFile(backupPath, traceLogger, callerName);
    }

    public static bool DeleteFilesCore(DatabaseEntry entry, ITraceLogger traceLogger, string callerName)
    {
        var mainPath = entry.FullPath;

        if (!TryDeleteFile(mainPath + "-journal", traceLogger, callerName)) { return false; }

        if (!TryDeleteFile(mainPath + "-wal", traceLogger, callerName)) { return false; }

        if (!TryDeleteFile(mainPath + "-shm", traceLogger, callerName)) { return false; }

        return TryDeleteFile(mainPath + UpgradeBackupSuffix, traceLogger, callerName) &&
            TryDeleteFile(mainPath, traceLogger, callerName);
    }

    public static void DeleteDatabaseFiles(string databasePath, string fileName)
    {
        var basePath = Path.Combine(databasePath, fileName);

        File.Delete(basePath + "-journal");
        File.Delete(basePath + "-wal");
        File.Delete(basePath + "-shm");
        File.Delete(basePath + UpgradeBackupSuffix);
        File.Delete(basePath);
    }

    public static bool TryDeleteFile(string path, ITraceLogger traceLogger, string callerName)
    {
        try
        {
            File.Delete(path);

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SafeLog(() => traceLogger.Warning(
                $"{nameof(DatabaseFileOperations)}.{callerName}: delete failed for '{path}': {ex}"));

            return false;
        }
    }

    public static void WalCheckpoint(string dbPath)
    {
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();

            using var cmd = connection.CreateCommand();

            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
    }

    public static bool VerifyEntryReady(string fullPath, ITraceLogger traceLogger)
    {
        try
        {
            using var context = new ProviderDbContext(
                fullPath,
                readOnly: true,
                ensureCreated: false,
                logger: traceLogger);

            return context.IsUpgradeNeeded().CurrentVersion == ProviderDatabaseSchemaVersion.Current;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SafeLog(() => traceLogger.Warning(
                $"{nameof(DatabaseFileOperations)}.{nameof(VerifyEntryReady)}: '{fullPath}' verification threw: {ex}"));

            return false;
        }
    }

    private static void SafeLog(Action log)
    {
        try { log(); }
        catch
        { /* Logger faults must not propagate from defensive logging sites. */
        }
    }
}
