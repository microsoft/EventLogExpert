// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.ProviderDatabase;

/// <summary>
///     Abstracts provider-database maintenance operations (schema inspection, upgrade, connection lifecycle) so that
///     consumers do not depend on the EF Core / SQLite implementation directly.
/// </summary>
public interface IProviderDatabaseMaintenance
{
    /// <summary>Inspects the schema version of the database at <paramref name="databasePath" />.</summary>
    /// <param name="databasePath">Full path to the .db file.</param>
    /// <param name="readOnly">
    ///     <c>true</c> for read-only verification (no EnsureCreated); <c>false</c> for classification
    ///     probes that may need write access.
    /// </param>
    ProviderDatabaseSchemaState CheckSchemaState(string databasePath, bool readOnly = false);

    /// <summary>
    ///     Runs the schema migration on the database at <paramref name="databasePath" />. On return (normal or
    ///     exceptional), the context is disposed and all SQLite connection pools are flushed — callers may safely perform file
    ///     operations on the database.
    /// </summary>
    void PerformUpgrade(string databasePath);

    /// <summary>
    ///     Executes <c>PRAGMA wal_checkpoint(TRUNCATE)</c> on the database and flushes all SQLite connection pools. The
    ///     checkpoint + pool flush are a single atomic operation.
    /// </summary>
    void WalCheckpoint(string databasePath);

    /// <summary>
    ///     Flushes all SQLite connection pools process-wide so that pooled file handles are released and subsequent file
    ///     operations (delete, copy, move) do not race on Windows. This is a global operation — it affects every pooled
    ///     connection in the process, not just the connection for a specific database.
    /// </summary>
    void PrepareForFileDeletion();
}
