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

    public IReadOnlyList<ProviderDatabaseOsStamp> ReadDistinctSourceOsStamps(string databasePath, int limit)
    {
        var stamps = new List<ProviderDatabaseOsStamp>();

        // Read-only + pooling disabled so the file handle is released on dispose (no lingering pooled connection that
        // could block a later delete/move). A raw DISTINCT query avoids EF Core evaluating Distinct() client-side.
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText =
            "SELECT DISTINCT SourceOsBuild, SourceOsRevision, SourceOsEdition, SourceOsDisplayVersion " +
            "FROM ProviderDetails LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            stamps.Add(new ProviderDatabaseOsStamp(
                reader.IsDBNull(0) ? null : reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return stamps;
    }

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
