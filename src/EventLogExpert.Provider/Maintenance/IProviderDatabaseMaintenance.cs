// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Provider.Schema;

namespace EventLogExpert.Provider.Maintenance;

public interface IProviderDatabaseMaintenance
{
    DatabaseSchemaState CheckSchemaState(string databasePath, bool readOnly = false);

    // Always flushes SQLite pools after migration so callers can move or delete the file.
    void PerformUpgrade(string databasePath);

    // Process-wide: clears every SQLite pool, not just this database.
    void PrepareForFileDeletion();

    // Caller must restrict this to Ready schema; stamp-read failures are classification-neutral.
    IReadOnlyList<ProviderDatabaseOsStamp> ReadDistinctSourceOsStamps(string databasePath, int limit);

    // Also flushes SQLite pools so the checkpoint connection cannot keep the file pinned.
    void WalCheckpoint(string databasePath);
}
