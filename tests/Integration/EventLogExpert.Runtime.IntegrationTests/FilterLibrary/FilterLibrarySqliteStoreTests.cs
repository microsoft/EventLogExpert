// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.FilterLibrary;
using Microsoft.Data.Sqlite;
using NSubstitute;

namespace EventLogExpert.Runtime.IntegrationTests.FilterLibrary;

public sealed class FilterLibrarySqliteStoreTests : IDisposable
{
    private readonly List<string> _tempDatabases = [];

    [Fact]
    public void Add_PersistsAndIsReadable()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var entry = BuildFilterEntry("id-1", "First");

        // Act
        store.Add(entry);
        var result = store.LoadAll();

        // Assert
        Assert.Single(result);
        var loaded = Assert.IsType<LibraryEntrySavedFilter>(result[0]);
        Assert.Equal("id-1", loaded.Id);
        Assert.Equal("First", loaded.Name);
        Assert.Equal("Level == 4", loaded.Filter.ComparisonText);
    }

    [Fact]
    public void Add_PersistsPresetEntryWithMultipleFilters()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());

        var f1 = SavedFilter.TryCreate("Level == 2");
        var f2 = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(f1);
        Assert.NotNull(f2);

        var preset = new LibraryEntryPreset("id-1", "Preset", DateTimeOffset.UtcNow, [f1, f2]);

        // Act
        store.Add(preset);
        var result = store.LoadAll();

        // Assert
        var loaded = Assert.IsType<LibraryEntryPreset>(result[0]);
        Assert.Equal(2, loaded.Filters.Count);
    }

    [Fact]
    public void Delete_RemovesEntry()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var entry = BuildFilterEntry("id-1", "First");
        store.Add(entry);

        // Act
        store.Delete("id-1");
        var result = store.LoadAll();

        // Assert
        Assert.Empty(result);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        foreach (var path in _tempDatabases)
        {
            try
            {
                if (File.Exists(path)) { File.Delete(path); }
                var dir = Path.GetDirectoryName(path);
                while (dir is not null && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch
            {
                // Best-effort cleanup; CI temp dir is wiped between runs.
            }
        }
    }

    [Fact]
    public void LoadAll_CreatesParentDirectoryIfMissing()
    {
        // Arrange — point at a nested path that doesn't exist yet.
        var nestedDir = Path.Combine(Path.GetTempPath(), $"FilterLibrarySqliteStoreTests_{Guid.NewGuid()}", "nested", "deeper");
        var dbPath = Path.Combine(nestedDir, "filter-library.db");
        _tempDatabases.Add(dbPath);
        Assert.False(Directory.Exists(nestedDir));

        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());

        // Act
        var result = store.LoadAll();

        // Assert — directory was created on first connection open
        Assert.True(Directory.Exists(nestedDir));
        Assert.Empty(result);
    }

    [Fact]
    public void LoadAll_FreshDatabase_ReturnsEmpty()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());

        // Act
        var result = store.LoadAll();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void LoadAll_MalformedPayload_SkipsRowAndLogs()
    {
        // Arrange — insert a row with bogus JSON payload directly via SqliteConnection.
        var dbPath = CreateTempDatabasePath();
        var logger = Substitute.For<ITraceLogger>();
        var store = new FilterLibrarySqliteStore(dbPath, logger);

        // Force schema creation by performing a no-op Add then Delete (idempotent).
        var seedEntry = BuildFilterEntry("seed", "Seed");
        store.Add(seedEntry);
        store.Delete("seed");

        // Now insert a malformed row directly.
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO library_entries (id, name, created_utc, kind, payload) VALUES ('bad', 'Bad', '2026-05-31T00:00:00.0000000+00:00', 'Filter', 'not valid json');";
            cmd.ExecuteNonQuery();
        }

        // Insert a valid row alongside it.
        store.Add(BuildFilterEntry("good", "Good"));

        // Act
        var result = store.LoadAll();

        // Assert — bad row skipped, good row loaded
        Assert.Single(result);
        Assert.Equal("good", result[0].Id);
        logger.ReceivedWithAnyArgs(1).Warning(default);
    }

    [Fact]
    public void LoadAll_UnknownKind_SkipsRowAndLogs()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();
        var logger = Substitute.For<ITraceLogger>();
        var store = new FilterLibrarySqliteStore(dbPath, logger);
        store.Add(BuildFilterEntry("seed", "Seed"));
        store.Delete("seed");

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO library_entries (id, name, created_utc, kind, payload) VALUES ('unknown', 'Unknown', '2026-05-31T00:00:00.0000000+00:00', 'SomeFutureKind', '{}');";
            cmd.ExecuteNonQuery();
        }

        // Act
        var result = store.LoadAll();

        // Assert
        Assert.Empty(result);
        logger.ReceivedWithAnyArgs(1).Warning(default);
    }

    [Fact]
    public void Update_ReplacesExistingEntry()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var initial = BuildFilterEntry("id-1", "First");
        store.Add(initial);

        var updated = BuildFilterEntry("id-1", "First (renamed)");

        // Act
        store.Update(updated);
        var result = store.LoadAll();

        // Assert
        Assert.Single(result);
        Assert.Equal("First (renamed)", result[0].Name);
    }

    private static LibraryEntrySavedFilter BuildFilterEntry(string id, string name)
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);

        return new LibraryEntrySavedFilter(id, name, DateTimeOffset.UtcNow, filter);
    }

    private string CreateTempDatabasePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"FilterLibrarySqliteStoreTests_{Guid.NewGuid()}.db");
        _tempDatabases.Add(path);
        return path;
    }
}
