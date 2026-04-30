// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventProviderDatabase;
using EventLogExpert.Eventing.Helpers;
using Microsoft.Data.Sqlite;

namespace EventLogExpert.UI.Tests.TestUtils;

internal static class DatabaseSeedUtils
{
    /// <summary>Seeds the V1 ProviderDetails schema (TEXT payload columns, no Parameters column).
    /// V1 is the "non-upgradable" leg — it should classify as ObsoleteSchema, NOT UpgradeRequired.</summary>
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

    /// <summary>Seeds the V2 ProviderDetails schema (TEXT payload columns, TEXT Parameters column).
    /// V2 added Parameters as TEXT JSON before the V3 BLOB rewrite. Should also classify as
    /// ObsoleteSchema per Phase B policy. Layout matches EventProviderDbContext.IsUpgradeNeeded
    /// detection logic: `payloadColumnsAllText &amp;&amp; IsType(parametersType, "TEXT")` → version 2.</summary>
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

    /// <summary>Seeds an empty file with the V3 ProviderDetails schema (BLOB payload columns,
    /// no ResolvedFromOwningPublisher column, default BINARY collation on the PK). Built by raw
    /// SQL so a subsequent EnsureCreated() sees the table as already-existing and skips its V4
    /// schema generation, exposing the legacy V3 shape to detection / upgrade paths. Mirrors
    /// EventProviderDbContextTests.SeedV3Schema.</summary>
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

        // Seed methods are used by tests that subsequently lock the file, copy/restore it, or
        // delete it; SqliteConnection pooling keeps the OS handle alive after the using-block
        // disposes the managed wrapper. Drop the pool here so callers can manipulate the file
        // immediately.
        SqliteConnection.ClearAllPools();
    }

    /// <summary>Creates a fully-formed V4 schema by letting EnsureCreated build it on a brand-new
    /// file. Returns immediately after the context is disposed — the resulting .db reflects the
    /// current production schema.</summary>
    internal static void SeedV4Schema(string dbPath, ITraceLogger? logger = null)
    {
        using (var context = new EventProviderDbContext(dbPath, readOnly: false, ensureCreated: true, logger))
        {
            context.Database.EnsureCreated();
        }

        SqliteConnection.ClearAllPools();
    }
}
