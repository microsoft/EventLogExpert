// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Maintenance;
using EventLogExpert.Provider.Schema;

namespace EventLogExpert.Runtime.Database;

internal static class DatabaseFileOperations
{
    public const string UpgradeBackupSuffix = ".upgrade.bak";

    public static void DeleteDatabaseFiles(string databasePath, string fileName)
    {
        var basePath = Path.Combine(databasePath, fileName);

        File.Delete(basePath + "-journal");
        File.Delete(basePath + "-wal");
        File.Delete(basePath + "-shm");
        File.Delete(basePath + UpgradeBackupSuffix);
        File.Delete(basePath);
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

    public static bool VerifyEntryReady(
        string fullPath,
        IProviderDatabaseMaintenance maintenance,
        ITraceLogger traceLogger)
    {
        try
        {
            return !maintenance.CheckSchemaState(fullPath, readOnly: true).NeedsUpgrade;
        }
        catch (SchemaLockTimeoutException ex)
        {
            // Inconclusive, not a failure: a concurrent process holds the schema lock, so the state can't be re-probed
            // right now. The sole caller is the post-upgrade verify, where treating this as not-ready would roll back a
            // just-completed upgrade; the entry is re-classified on the next pass instead.
            SafeLog(() => traceLogger.Warning(
                $"{nameof(DatabaseFileOperations)}.{nameof(VerifyEntryReady)}: '{fullPath}' schema lock busy during verify: {ex.Message}"));

            return true;
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
        catch { /* Logger faults must not propagate from defensive logging sites. */ }
    }
}
