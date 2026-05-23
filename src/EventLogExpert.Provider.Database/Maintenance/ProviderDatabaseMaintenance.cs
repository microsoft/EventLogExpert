// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Maintenance;
using EventLogExpert.Provider.Schema;
using EventLogExpert.ProviderDatabase.Context;
using Microsoft.Data.Sqlite;

namespace EventLogExpert.ProviderDatabase.Maintenance;

internal sealed class ProviderDatabaseMaintenance(ITraceLogger? logger = null) : IProviderDatabaseMaintenance
{
    public DatabaseSchemaState CheckSchemaState(string databasePath, bool readOnly = false)
    {
        using var context = new ProviderDbContext(
            databasePath,
            readOnly,
            false,
            logger);

        return context.IsUpgradeNeeded();
    }

    public void PerformUpgrade(string databasePath)
    {
        try
        {
            using var context = new ProviderDbContext(
                databasePath,
                false,
                false,
                logger);

            context.PerformUpgradeIfNeeded();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }

    public void PrepareForFileDeletion() => SqliteConnection.ClearAllPools();

    public void WalCheckpoint(string databasePath)
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={databasePath}");

            connection.Open();

            using var cmd = connection.CreateCommand();

            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteNonQuery();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }
}
