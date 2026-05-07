// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventProviderDatabase;
using EventLogExpert.Eventing.Logging;
using Microsoft.Data.Sqlite;

namespace EventLogExpert.UI.Tests.TestUtils;

internal static class DatabaseSeedUtils
{
    internal static void SeedV1Schema(string dbPath)
    {
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "CREATE TABLE \"ProviderDetails\" (" +
                "\"ProviderName\" TEXT NOT NULL CONSTRAINT \"PK_ProviderDetails\" PRIMARY KEY, " +
                "\"Messages\" TEXT NOT NULL, " +
                "\"Events\" TEXT NOT NULL, " +
                "\"Keywords\" TEXT NOT NULL, " +
                "\"Opcodes\" TEXT NOT NULL, " +
                "\"Tasks\" TEXT NOT NULL)";
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
    }

    internal static void SeedV2Schema(string dbPath)
    {
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "CREATE TABLE \"ProviderDetails\" (" +
                "\"ProviderName\" TEXT NOT NULL CONSTRAINT \"PK_ProviderDetails\" PRIMARY KEY, " +
                "\"Messages\" TEXT NOT NULL, " +
                "\"Parameters\" TEXT NOT NULL, " +
                "\"Events\" TEXT NOT NULL, " +
                "\"Keywords\" TEXT NOT NULL, " +
                "\"Opcodes\" TEXT NOT NULL, " +
                "\"Tasks\" TEXT NOT NULL)";
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
    }

    internal static void SeedV3Schema(string dbPath)
    {
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "CREATE TABLE \"ProviderDetails\" (" +
                "\"ProviderName\" TEXT NOT NULL CONSTRAINT \"PK_ProviderDetails\" PRIMARY KEY, " +
                "\"Messages\" BLOB NOT NULL, " +
                "\"Parameters\" BLOB NOT NULL, " +
                "\"Events\" BLOB NOT NULL, " +
                "\"Keywords\" BLOB NOT NULL, " +
                "\"Opcodes\" BLOB NOT NULL, " +
                "\"Tasks\" BLOB NOT NULL)";
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
    }

    internal static void SeedV4Schema(string dbPath, ITraceLogger? logger = null)
    {
        using (var context = new EventProviderDbContext(dbPath, readOnly: false, ensureCreated: true, logger))
        {
            context.Database.EnsureCreated();
        }

        SqliteConnection.ClearAllPools();
    }
}
