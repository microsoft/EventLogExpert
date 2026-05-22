// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Logging;
using EventLogExpert.Eventing.ProviderDatabase;
using Microsoft.Data.Sqlite;

namespace EventLogExpert.ProviderDatabase;

internal sealed class ProviderDatabaseMaintenance(ITraceLogger? logger = null) : IProviderDatabaseMaintenance
{
    public ProviderDatabaseSchemaState CheckSchemaState(string databasePath, bool readOnly = false)
    {
        using var context = new ProviderDbContext(
            databasePath,
            readOnly,
            ensureCreated: false,
            logger: logger);

        return context.IsUpgradeNeeded();
    }

    public void PerformUpgrade(string databasePath)
    {
        try
        {
            using var context = new ProviderDbContext(
                databasePath,
                readOnly: false,
                ensureCreated: false,
                logger: logger);

            context.PerformUpgradeIfNeeded();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }

    public void WalCheckpoint(string databasePath)
    {
        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            connection.Open();

            using var cmd = connection.CreateCommand();

            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
    }

    public void PrepareForFileDeletion() => SqliteConnection.ClearAllPools();
}
