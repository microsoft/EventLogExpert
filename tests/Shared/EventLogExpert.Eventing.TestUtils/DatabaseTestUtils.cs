// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Provider.Resolution;
using EventLogExpert.ProviderDatabase.Context;
using Microsoft.Data.Sqlite;

namespace EventLogExpert.Eventing.TestUtils;

public static class DatabaseTestUtils
{
    public static ProviderDetails BuildProviderDetails(string name, string? resolvedFromOwningPublisher = null) => new()
    {
        ProviderName = name,
        Messages = [],
        Parameters = [],
        Events = [],
        Keywords = new Dictionary<long, string>(),
        Opcodes = new Dictionary<int, string>(),
        Tasks = new Dictionary<int, string>(),
        ResolvedFromOwningPublisher = resolvedFromOwningPublisher
    };

    public static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"eventdbtool_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(path);
        return path;
    }

    public static string CreateTempPath(string extension = ".db") =>
        Path.Combine(Path.GetTempPath(), $"eventdbtool_test_{Guid.NewGuid()}{extension}");

    public static void CreateUnknownShapeDatabase(string dbPath)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "CREATE TABLE \"ProviderDetails\" (" +
            "\"ProviderName\" TEXT NOT NULL CONSTRAINT \"PK_ProviderDetails\" PRIMARY KEY, " +
            "\"Messages\" BLOB NOT NULL, " +
            "\"Parameters\" TEXT NOT NULL, " +
            "\"Events\" BLOB NOT NULL, " +
            "\"Keywords\" BLOB NOT NULL, " +
            "\"Opcodes\" BLOB NOT NULL, " +
            "\"Tasks\" BLOB NOT NULL)";
        cmd.ExecuteNonQuery();
    }

    public static void CreateV3Database(string dbPath)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
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

    public static void CreateV4Database(string dbPath, params ProviderDetails[] providers)
    {
        using var context = new ProviderDbContext(dbPath, false);

        foreach (var provider in providers)
        {
            context.ProviderDetails.Add(provider);
        }

        context.SaveChanges();
    }

    public static void DeleteDatabaseFile(string path) => SqliteTestDb.Delete(path);

    public static void DeleteDirectoryRecursive(string path) => SqliteTestDb.DeleteDirectory(path);
}
