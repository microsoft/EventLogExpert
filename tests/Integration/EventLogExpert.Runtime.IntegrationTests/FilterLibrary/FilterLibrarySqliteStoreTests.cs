// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Evaluation;
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
    public void Add_PersistsAllNewColumns_FavoriteLastUsedOrigin()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var lastUsed = new DateTimeOffset(2026, 5, 31, 14, 0, 0, TimeSpan.Zero);
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);

        var entry = new LibraryEntrySavedFilter
        {
            Name = "Fav",
            CreatedUtc = DateTimeOffset.UtcNow,
            IsFavorite = true,
            LastUsedUtc = lastUsed,
            Origin = LibraryEntryOrigin.AutoTracked,
            Filter = filter,
        };

        store.Add(entry);
        var result = store.LoadAll();

        var loaded = Assert.IsType<LibraryEntrySavedFilter>(Assert.Single(result));
        Assert.True(loaded.IsFavorite);
        Assert.Equal(lastUsed, loaded.LastUsedUtc);
        Assert.Equal(LibraryEntryOrigin.AutoTracked, loaded.Origin);
    }

    [Fact]
    public void Add_PersistsAndIsReadable()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var entry = BuildFilterEntry("First");

        store.Add(entry);
        var result = store.LoadAll();

        Assert.Single(result);
        var loaded = Assert.IsType<LibraryEntrySavedFilter>(result[0]);
        Assert.Equal(entry.Id, loaded.Id);
        Assert.Equal("First", loaded.Name);
        Assert.Equal("Level == 4", loaded.Filter.ComparisonText);
        Assert.False(loaded.IsFavorite);
        Assert.Null(loaded.LastUsedUtc);
        Assert.Equal(LibraryEntryOrigin.UserSaved, loaded.Origin);
    }

    [Fact]
    public void Add_PersistsPresetEntryWithMultipleFilters()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());

        var f1 = SavedFilter.TryCreate("Level == 2");
        var f2 = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(f1);
        Assert.NotNull(f2);

        var preset = new LibraryEntryPreset
        {
            Name = "Preset",
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [f1, f2],
        };

        store.Add(preset);
        var result = store.LoadAll();

        var loaded = Assert.IsType<LibraryEntryPreset>(result[0]);
        Assert.Equal(2, loaded.Filters.Count);
    }

    [Fact]
    public void AddOrReturnExistingFilter_DifferentMode_AllowsCoexistence()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());

        var advanced = BuildAutoTrackedFilterEntry("Level == 4", mode: FilterMode.Advanced);
        store.Add(advanced);

        var basic = BuildAutoTrackedFilterEntry("Level == 4", mode: FilterMode.Basic);
        var (entry, wasInserted) = store.AddOrReturnExistingFilter(basic);

        Assert.True(wasInserted);
        Assert.Same(basic, entry);
        Assert.Equal(2, store.LoadAll().Count);
    }

    [Fact]
    public void AddOrReturnExistingFilter_EmptyComparisonText_ThrowsArgumentException()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var emptyText = new LibraryEntrySavedFilter
        {
            Name = "Empty",
            CreatedUtc = DateTimeOffset.UtcNow,
            LastUsedUtc = DateTimeOffset.UtcNow,
            Origin = LibraryEntryOrigin.AutoTracked,
            Filter = SavedFilter.Empty,
        };

        Assert.Throws<ArgumentException>(() => store.AddOrReturnExistingFilter(emptyText));
    }

    [Fact]
    public void AddOrReturnExistingFilter_FreshInsert_ReturnsCandidateAndWasInsertedTrue()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());

        var candidate = BuildAutoTrackedFilterEntry("Level == 4");
        var (entry, wasInserted) = store.AddOrReturnExistingFilter(candidate);

        Assert.True(wasInserted);
        Assert.Same(candidate, entry);
        Assert.Single(store.LoadAll());
    }

    [Fact]
    public void AddOrReturnExistingFilter_NonAutoTrackedOrigin_ThrowsArgumentException()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var userSaved = BuildFilterEntry("Saved");  // Origin = UserSaved by default

        Assert.Throws<ArgumentException>(() => store.AddOrReturnExistingFilter(userSaved));
    }

    [Fact]
    public void AddOrReturnExistingFilter_TupleCollision_IgnoresCase()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        store.Add(BuildAutoTrackedFilterEntry("Level == 4"));

        var differentCase = BuildAutoTrackedFilterEntry("LEVEL == 4");
        var (_, wasInserted) = store.AddOrReturnExistingFilter(differentCase);

        Assert.False(wasInserted);
    }

    [Fact]
    public void AddOrReturnExistingFilter_TupleCollision_ReturnsExistingAndWasInsertedFalse()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());

        var first = BuildAutoTrackedFilterEntry("Level == 4");
        store.Add(first);

        var competing = BuildAutoTrackedFilterEntry("Level == 4");
        var (entry, wasInserted) = store.AddOrReturnExistingFilter(competing);

        Assert.False(wasInserted);
        Assert.Equal(first.Id, entry.Id);
        Assert.Single(store.LoadAll());
    }

    [Fact]
    public void AddOrReturnExistingFilter_UserSavedSameTuple_AllowsAutoTrackedInsert()
    {
        // Partial UNIQUE INDEX is scoped to origin='AutoTracked' so a UserSaved entry with
        // the same tuple must NOT block an AutoTracked insert.
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());

        var userSaved = BuildFilterEntry("Saved");
        store.Add(userSaved);

        var autoTracked = BuildAutoTrackedFilterEntry("Level == 4");
        var (_, wasInserted) = store.AddOrReturnExistingFilter(autoTracked);

        Assert.True(wasInserted);
        Assert.Equal(2, store.LoadAll().Count);
    }

    [Fact]
    public void AddRange_DuplicatePrimaryKey_RollsBackEntireBatch()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var seed = BuildFilterEntry("Seed");
        store.Add(seed);

        var conflict = new LibraryEntrySavedFilter
        {
            Id = seed.Id,
            Name = "Conflict",
            CreatedUtc = DateTimeOffset.UtcNow,
            Filter = seed.Filter,
        };

        var batch = new List<LibraryEntry>
        {
            BuildFilterEntry("New 1"),
            conflict,
            BuildFilterEntry("New 2"),
        };

        Assert.Throws<SqliteException>(() => store.AddRange(batch));

        var result = store.LoadAll();
        Assert.Single(result);
        Assert.Equal(seed.Id, result[0].Id);
    }

    [Fact]
    public void AddRange_EmptyEnumerable_IsNoOp()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());

        store.AddRange([]);

        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void AddRange_InsertsAllEntries_InOneTransaction()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var entryA = BuildFilterEntry("A");
        var entryB = BuildFilterEntry("B");
        var entryC = BuildFilterEntry("C");

        store.AddRange([entryA, entryB, entryC]);

        var result = store.LoadAll();
        Assert.Equal(3, result.Count);
        Assert.Contains(result, e => e.Id == entryA.Id);
        Assert.Contains(result, e => e.Id == entryB.Id);
        Assert.Contains(result, e => e.Id == entryC.Id);
    }

    [Fact]
    public void Delete_RemovesEntry()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var entry = BuildFilterEntry("First");
        store.Add(entry);

        store.Delete(entry.Id);
        var result = store.LoadAll();

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
    public void EnsureSchemaColumns_AppliedIdempotently_AcrossMultipleConnections()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());

        store.Add(BuildFilterEntry("First"));
        store.Add(BuildFilterEntry("Second"));
        var result = store.LoadAll();

        Assert.Equal(2, result.Count);

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        var columns = ReadColumnNames(connection);

        foreach (var required in new[] { "is_favorite", "last_used_utc", "origin", "comparison_text", "mode", "is_excluded" })
        {
            Assert.Contains(required, columns, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void EnsureSchemaColumns_LegacyPr2Schema_AltersToCurrentShape()
    {
        // Simulate a database created by the PR-2 5-column schema and prove PR-3
        // first-open adds the 6 new columns (+ unique index) without data loss.
        var dbPath = CreateTempDatabasePath();
        var legacyId = SeedLegacyPr2Schema(dbPath);

        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());

        var postMigration = BuildFilterEntry("Post");
        store.Add(postMigration);
        var result = store.LoadAll();

        Assert.Equal(2, result.Count);
        Assert.Contains(result, e => e.Id == legacyId);
        Assert.Contains(result, e => e.Id == postMigration.Id);

        var legacy = result.First(e => e.Id == legacyId);
        Assert.Equal(LibraryEntryOrigin.UserSaved, legacy.Origin);
        Assert.False(legacy.IsFavorite);
        Assert.Null(legacy.LastUsedUtc);
    }

    [Fact]
    public void LoadAll_CreatesParentDirectoryIfMissing()
    {
        var nestedDir = Path.Combine(Path.GetTempPath(), $"FilterLibrarySqliteStoreTests_{Guid.NewGuid()}", "nested", "deeper");
        var dbPath = Path.Combine(nestedDir, "filter-library.db");
        _tempDatabases.Add(dbPath);
        Assert.False(Directory.Exists(nestedDir));

        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());

        var result = store.LoadAll();

        Assert.True(Directory.Exists(nestedDir));
        Assert.Empty(result);
    }

    [Fact]
    public void LoadAll_FreshDatabase_ReturnsEmpty()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());

        var result = store.LoadAll();

        Assert.Empty(result);
    }

    [Fact]
    public void LoadAll_MalformedPayload_SkipsRowAndLogs()
    {
        var dbPath = CreateTempDatabasePath();
        var logger = Substitute.For<ITraceLogger>();
        var store = new FilterLibrarySqliteStore(dbPath, logger);

        var seedEntry = BuildFilterEntry("Seed");
        store.Add(seedEntry);
        store.Delete(seedEntry.Id);

        var badId = new LibraryEntryId(Guid.Parse("00000000-0000-0000-0000-00000000bad1"));

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"""
                INSERT INTO library_entries (id, name, created_utc, kind, payload, is_favorite, last_used_utc, origin, comparison_text, mode, is_excluded)
                VALUES ('{badId.Value:D}', 'Bad', '2026-05-31T00:00:00.0000000+00:00', 'Filter', 'not valid json', 0, NULL, 'UserSaved', NULL, NULL, NULL);
                """;
            cmd.ExecuteNonQuery();
        }

        var good = BuildFilterEntry("Good");
        store.Add(good);

        var result = store.LoadAll();

        Assert.Single(result);
        Assert.Equal(good.Id, result[0].Id);
        logger.ReceivedWithAnyArgs(1).Warning(default);
    }

    [Fact]
    public void LoadAll_UnknownKind_SkipsRowAndLogs()
    {
        var dbPath = CreateTempDatabasePath();
        var logger = Substitute.For<ITraceLogger>();
        var store = new FilterLibrarySqliteStore(dbPath, logger);
        var seed = BuildFilterEntry("Seed");
        store.Add(seed);
        store.Delete(seed.Id);

        var unknownId = new LibraryEntryId(Guid.Parse("00000000-0000-0000-0000-000000000099"));

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"INSERT INTO library_entries (id, name, created_utc, kind, payload, is_favorite, last_used_utc, origin, comparison_text, mode, is_excluded) VALUES ('{unknownId.Value:D}', 'Unknown', '2026-05-31T00:00:00.0000000+00:00', 'SomeFutureKind', '{{}}', 0, NULL, 'UserSaved', NULL, NULL, NULL);";
            cmd.ExecuteNonQuery();
        }

        var result = store.LoadAll();

        Assert.Empty(result);
        logger.ReceivedWithAnyArgs(1).Warning(default);
    }

    [Fact]
    public void TryBumpLastUsedIfNotFavorite_FavoritedEntry_DoesNotBumpAndReturnsFalse()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);
        var fav = new LibraryEntrySavedFilter
        {
            Name = "Fav",
            CreatedUtc = DateTimeOffset.UtcNow,
            IsFavorite = true,
            Filter = filter,
        };
        store.Add(fav);

        var bumped = store.TryBumpLastUsedIfNotFavorite(fav.Id, DateTimeOffset.UtcNow);

        Assert.False(bumped);
        var loaded = Assert.IsType<LibraryEntrySavedFilter>(Assert.Single(store.LoadAll()));
        Assert.Null(loaded.LastUsedUtc);
    }

    [Fact]
    public void TryBumpLastUsedIfNotFavorite_MissingEntry_ReturnsFalse()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());

        Assert.False(store.TryBumpLastUsedIfNotFavorite(LibraryEntryId.Create(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void TryBumpLastUsedIfNotFavorite_NotFavorite_BumpsAndReturnsTrue()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var seeded = BuildAutoTrackedFilterEntry("Level == 4");
        store.Add(seeded);
        var bumpTo = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        var bumped = store.TryBumpLastUsedIfNotFavorite(seeded.Id, bumpTo);

        Assert.True(bumped);
        var loaded = Assert.IsType<LibraryEntrySavedFilter>(Assert.Single(store.LoadAll()));
        Assert.Equal(bumpTo, loaded.LastUsedUtc);
    }

    [Fact]
    public void TryDeleteAutoTrackedIfNotFavorite_AutoTrackedNotFavorite_DeletesAndReturnsTrue()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var seeded = BuildAutoTrackedFilterEntry("Level == 4");
        store.Add(seeded);

        var deleted = store.TryDeleteAutoTrackedIfNotFavorite(seeded.Id);

        Assert.True(deleted);
        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void TryDeleteAutoTrackedIfNotFavorite_FavoritedAutoTracked_DoesNotDeleteAndReturnsFalse()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);
        var fav = new LibraryEntrySavedFilter
        {
            Name = "Fav AutoTracked",
            CreatedUtc = DateTimeOffset.UtcNow,
            Origin = LibraryEntryOrigin.AutoTracked,
            IsFavorite = true,
            Filter = filter,
        };
        store.Add(fav);

        var deleted = store.TryDeleteAutoTrackedIfNotFavorite(fav.Id);

        Assert.False(deleted);
        Assert.Single(store.LoadAll());
    }

    [Fact]
    public void TryDeleteAutoTrackedIfNotFavorite_MissingEntry_ReturnsFalse()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());

        Assert.False(store.TryDeleteAutoTrackedIfNotFavorite(LibraryEntryId.Create()));
    }

    [Fact]
    public void TryDeleteAutoTrackedIfNotFavorite_PresetRow_DoesNotDeleteAndReturnsFalse()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);
        var preset = new LibraryEntryPreset
        {
            Name = "Preset",
            CreatedUtc = DateTimeOffset.UtcNow,
            Origin = LibraryEntryOrigin.AutoTracked,
            Filters = [filter],
        };
        store.Add(preset);

        var deleted = store.TryDeleteAutoTrackedIfNotFavorite(preset.Id);

        Assert.False(deleted);
        Assert.Single(store.LoadAll());
    }

    [Fact]
    public void TryDeleteAutoTrackedIfNotFavorite_UserSavedRow_DoesNotDeleteAndReturnsFalse()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var userSaved = BuildFilterEntry("Saved");
        store.Add(userSaved);

        var deleted = store.TryDeleteAutoTrackedIfNotFavorite(userSaved.Id);

        Assert.False(deleted);
        Assert.Single(store.LoadAll());
    }

    [Fact]
    public void UniqueIndex_DirectInsertOfDuplicateAutoTracked_RaisesConstraintViolation()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        store.Add(BuildAutoTrackedFilterEntry("Level == 4"));

        Assert.Throws<SqliteException>(() => store.Add(BuildAutoTrackedFilterEntry("Level == 4")));
    }

    [Fact]
    public void Update_ReplacesExistingEntry()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var initial = BuildFilterEntry("First");
        store.Add(initial);

        var updated = new LibraryEntrySavedFilter
        {
            Id = initial.Id,
            Name = "First (renamed)",
            CreatedUtc = initial.CreatedUtc,
            Filter = initial.Filter,
        };

        store.Update(updated);
        var result = store.LoadAll();

        Assert.Single(result);
        Assert.Equal("First (renamed)", result[0].Name);
    }

    private static LibraryEntrySavedFilter BuildAutoTrackedFilterEntry(string comparisonText, FilterMode mode = FilterMode.Advanced)
    {
        var filter = SavedFilter.TryCreate(comparisonText, mode: mode);
        Assert.NotNull(filter);

        // AutoTracked rows are born with LastUsedUtc = nowUtc in production
        // (Effects.HandleRecordFilterApplied candidate construction); mirror that here so
        // SQL guards conditioning on `last_used_utc IS NOT NULL` see the production shape.
        return new LibraryEntrySavedFilter
        {
            Name = comparisonText,
            CreatedUtc = DateTimeOffset.UtcNow,
            LastUsedUtc = DateTimeOffset.UtcNow,
            Origin = LibraryEntryOrigin.AutoTracked,
            Filter = filter,
        };
    }

    private static LibraryEntrySavedFilter BuildFilterEntry(string name)
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);

        return new LibraryEntrySavedFilter
        {
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filter = filter,
        };
    }

    private static List<string> ReadColumnNames(SqliteConnection connection)
    {
        var columns = new List<string>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(library_entries);";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static LibraryEntryId SeedLegacyPr2Schema(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); }

        var legacyId = LibraryEntryId.Create();

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        using (var create = connection.CreateCommand())
        {
            create.CommandText = """
                CREATE TABLE library_entries (
                    id           TEXT PRIMARY KEY,
                    name         TEXT NOT NULL,
                    created_utc  TEXT NOT NULL,
                    kind         TEXT NOT NULL,
                    payload      TEXT NOT NULL
                );
                """;
            create.ExecuteNonQuery();
        }

        using var insert = connection.CreateCommand();
        insert.CommandText = $"INSERT INTO library_entries (id, name, created_utc, kind, payload) VALUES ('{legacyId.Value:D}', 'Legacy', '2026-05-31T00:00:00.0000000+00:00', 'Filter', '{{\"Color\":0,\"ComparisonText\":\"Level == 4\",\"IsExcluded\":false,\"Mode\":\"Advanced\"}}');";
        insert.ExecuteNonQuery();

        return legacyId;
    }

    private string CreateTempDatabasePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"FilterLibrarySqliteStoreTests_{Guid.NewGuid()}.db");
        _tempDatabases.Add(path);
        return path;
    }
}
