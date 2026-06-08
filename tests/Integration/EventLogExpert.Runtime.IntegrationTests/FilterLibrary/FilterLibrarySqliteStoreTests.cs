// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.FilterLibrary;
using Microsoft.Data.Sqlite;
using NSubstitute;

namespace EventLogExpert.Runtime.IntegrationTests.FilterLibrary;

public sealed class FilterLibrarySqliteStoreTests : IDisposable
{
    private readonly List<string> _tempDatabases = [];

    [Fact]
    public async Task Add_NoTags_PersistsAsNullColumn_DefaultsToEmptyOnLoad()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var entry = BuildFilterEntry("Untagged");

        await store.AddAsync(entry, TestContext.Current.CancellationToken);
        var result = await store.LoadAllAsync(TestContext.Current.CancellationToken);

        var loaded = Assert.IsType<LibraryEntrySavedFilter>(Assert.Single(result));
        Assert.NotNull(loaded.Tags);
        Assert.Empty(loaded.Tags);
    }

    [Fact]
    public async Task Add_PersistsAllNewColumns_FavoriteLastUsedOrigin()
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

        await store.AddAsync(entry, TestContext.Current.CancellationToken);
        var result = await store.LoadAllAsync(TestContext.Current.CancellationToken);

        var loaded = Assert.IsType<LibraryEntrySavedFilter>(Assert.Single(result));
        Assert.True(loaded.IsFavorite);
        Assert.Equal(lastUsed, loaded.LastUsedUtc);
        Assert.Equal(LibraryEntryOrigin.AutoTracked, loaded.Origin);
    }

    [Fact]
    public async Task Add_PersistsAndIsReadable()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var entry = BuildFilterEntry("First");

        await store.AddAsync(entry, TestContext.Current.CancellationToken);
        var result = await store.LoadAllAsync(TestContext.Current.CancellationToken);

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
    public async Task Add_PersistsFilterSetEntryWithMultipleFilters()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());

        var f1 = SavedFilter.TryCreate("Level == 2");
        var f2 = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(f1);
        Assert.NotNull(f2);

        var filterSet = new LibraryEntryFilterSet
        {
            Name = "Filter Set",
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [f1, f2],
        };

        await store.AddAsync(filterSet, TestContext.Current.CancellationToken);
        var result = await store.LoadAllAsync(TestContext.Current.CancellationToken);

        var loaded = Assert.IsType<LibraryEntryFilterSet>(result[0]);
        Assert.Equal(2, loaded.Filters.Count);
    }

    [Fact]
    public async Task Add_PersistsTagsRoundTrip_FilterSetEntry()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);

        var entry = new LibraryEntryFilterSet
        {
            Name = "Tagged Set",
            CreatedUtc = DateTimeOffset.UtcNow,
            Tags = ["network", "dns"],
            Filters = [filter],
        };

        await store.AddAsync(entry, TestContext.Current.CancellationToken);
        var result = await store.LoadAllAsync(TestContext.Current.CancellationToken);

        var loaded = Assert.IsType<LibraryEntryFilterSet>(Assert.Single(result));
        Assert.Equal(["network", "dns"], loaded.Tags);
    }

    [Fact]
    public async Task Add_PersistsTagsRoundTrip_SavedFilterEntry()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);

        var entry = new LibraryEntrySavedFilter
        {
            Name = "Tagged",
            CreatedUtc = DateTimeOffset.UtcNow,
            Tags = ["exchange", "hub"],
            Filter = filter,
        };

        await store.AddAsync(entry, TestContext.Current.CancellationToken);
        var result = await store.LoadAllAsync(TestContext.Current.CancellationToken);

        var loaded = Assert.IsType<LibraryEntrySavedFilter>(Assert.Single(result));
        Assert.Equal(["exchange", "hub"], loaded.Tags);
    }

    [Fact]
    public async Task AddOrReturnExistingFilter_DifferentMode_AllowsCoexistence()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());

        var advanced = BuildAutoTrackedFilterEntry("Level == 4", mode: FilterMode.Advanced);
        await store.AddAsync(advanced, TestContext.Current.CancellationToken);

        var basic = BuildAutoTrackedFilterEntry("Level == 4", mode: FilterMode.Basic);
        var (entry, wasInserted) = await store.AddOrReturnExistingFilterAsync(basic, TestContext.Current.CancellationToken);

        Assert.True(wasInserted);
        Assert.Same(basic, entry);
        Assert.Equal(2, (await store.LoadAllAsync(TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task AddOrReturnExistingFilter_EmptyComparisonText_ThrowsArgumentException()
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

        await Assert.ThrowsAsync<ArgumentException>(() => store.AddOrReturnExistingFilterAsync(emptyText, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddOrReturnExistingFilter_FreshInsert_ReturnsCandidateAndWasInsertedTrue()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());

        var candidate = BuildAutoTrackedFilterEntry("Level == 4");
        var (entry, wasInserted) = await store.AddOrReturnExistingFilterAsync(candidate, TestContext.Current.CancellationToken);

        Assert.True(wasInserted);
        Assert.Same(candidate, entry);
        Assert.Single(await store.LoadAllAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddOrReturnExistingFilter_NonAutoTrackedOrigin_ThrowsArgumentException()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var userSaved = BuildFilterEntry("Saved");  // Origin = UserSaved by default

        await Assert.ThrowsAsync<ArgumentException>(() => store.AddOrReturnExistingFilterAsync(userSaved, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddOrReturnExistingFilter_TupleCollision_IgnoresCase()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        await store.AddAsync(BuildAutoTrackedFilterEntry("Level == 4"), TestContext.Current.CancellationToken);

        var differentCase = BuildAutoTrackedFilterEntry("LEVEL == 4");
        var (_, wasInserted) = await store.AddOrReturnExistingFilterAsync(differentCase, TestContext.Current.CancellationToken);

        Assert.False(wasInserted);
    }

    [Fact]
    public async Task AddOrReturnExistingFilter_TupleCollision_ReturnsExistingAndWasInsertedFalse()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());

        var first = BuildAutoTrackedFilterEntry("Level == 4");
        await store.AddAsync(first, TestContext.Current.CancellationToken);

        var competing = BuildAutoTrackedFilterEntry("Level == 4");
        var (entry, wasInserted) = await store.AddOrReturnExistingFilterAsync(competing, TestContext.Current.CancellationToken);

        Assert.False(wasInserted);
        Assert.Equal(first.Id, entry.Id);
        Assert.Single(await store.LoadAllAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddOrReturnExistingFilter_UserSavedSameTuple_AllowsAutoTrackedInsert()
    {
        // Partial UNIQUE INDEX is scoped to origin='AutoTracked' so a UserSaved entry with
        // the same tuple must NOT block an AutoTracked insert.
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());

        var userSaved = BuildFilterEntry("Saved");
        await store.AddAsync(userSaved, TestContext.Current.CancellationToken);

        var autoTracked = BuildAutoTrackedFilterEntry("Level == 4");
        var (_, wasInserted) = await store.AddOrReturnExistingFilterAsync(autoTracked, TestContext.Current.CancellationToken);

        Assert.True(wasInserted);
        Assert.Equal(2, (await store.LoadAllAsync(TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task AddRange_DuplicatePrimaryKey_RollsBackEntireBatch()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var seed = BuildFilterEntry("Seed");
        await store.AddAsync(seed, TestContext.Current.CancellationToken);

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

        await Assert.ThrowsAsync<SqliteException>(() => store.AddRangeAsync(batch, TestContext.Current.CancellationToken));

        var result = await store.LoadAllAsync(TestContext.Current.CancellationToken);
        Assert.Single(result);
        Assert.Equal(seed.Id, result[0].Id);
    }

    [Fact]
    public async Task AddRange_EmptyEnumerable_IsNoOp()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());

        await store.AddRangeAsync([], TestContext.Current.CancellationToken);

        Assert.Empty(await store.LoadAllAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddRange_InsertsAllEntries_InOneTransaction()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var entryA = BuildFilterEntry("A");
        var entryB = BuildFilterEntry("B");
        var entryC = BuildFilterEntry("C");

        await store.AddRangeAsync([entryA, entryB, entryC], TestContext.Current.CancellationToken);

        var result = await store.LoadAllAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, result.Count);
        Assert.Contains(result, e => e.Id == entryA.Id);
        Assert.Contains(result, e => e.Id == entryB.Id);
        Assert.Contains(result, e => e.Id == entryC.Id);
    }

    [Fact]
    public async Task Delete_RemovesEntry()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var entry = BuildFilterEntry("First");
        await store.AddAsync(entry, TestContext.Current.CancellationToken);

        await store.DeleteAsync(entry.Id, TestContext.Current.CancellationToken);
        var result = await store.LoadAllAsync(TestContext.Current.CancellationToken);

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
    public async Task EnsureSchemaColumns_AppliedIdempotently_AcrossMultipleConnections()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());

        await store.AddAsync(BuildFilterEntry("First"), TestContext.Current.CancellationToken);
        await store.AddAsync(BuildFilterEntry("Second"), TestContext.Current.CancellationToken);
        var result = await store.LoadAllAsync(TestContext.Current.CancellationToken);

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
    public async Task EnsureSchemaColumns_LegacyPr2Schema_AltersToCurrentShape()
    {
        // Simulate a database created by the PR-2 5-column schema and prove PR-3
        // first-open adds the 6 new columns (+ unique index) without data loss.
        var dbPath = CreateTempDatabasePath();
        var legacyId = SeedLegacyPr2Schema(dbPath);

        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());

        var postMigration = BuildFilterEntry("Post");
        await store.AddAsync(postMigration, TestContext.Current.CancellationToken);
        var result = await store.LoadAllAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, e => e.Id == legacyId);
        Assert.Contains(result, e => e.Id == postMigration.Id);

        var legacy = result.First(e => e.Id == legacyId);
        Assert.Equal(LibraryEntryOrigin.UserSaved, legacy.Origin);
        Assert.False(legacy.IsFavorite);
        Assert.Null(legacy.LastUsedUtc);
    }

    [Fact]
    public async Task Load_FromPreTagsSchema_AddsTagsColumnViaAlterTable_ExistingRowsGetEmptyTags()
    {
        var dbPath = CreateTempDatabasePath();
        var preTagsId = SeedPreTagsRow(dbPath);

        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var result = await store.LoadAllAsync(TestContext.Current.CancellationToken);

        var loaded = Assert.IsType<LibraryEntrySavedFilter>(Assert.Single(result));
        Assert.Equal(preTagsId, loaded.Id);
        Assert.NotNull(loaded.Tags);
        Assert.Empty(loaded.Tags);
    }

    [Fact]
    public async Task LoadAll_CorruptTagsKnownKind_KeepsEntryWithEmptyTags()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var seed = BuildFilterEntry("Seed");
        await store.AddAsync(seed, TestContext.Current.CancellationToken);
        await store.DeleteAsync(seed.Id, TestContext.Current.CancellationToken);

        var entry = BuildFilterEntry("HasCorruptTags");
        await store.AddAsync(entry, TestContext.Current.CancellationToken);

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"UPDATE library_entries SET tags = 'not valid json' WHERE id = '{entry.Id.Value:D}';";
            cmd.ExecuteNonQuery();
        }

        var result = await store.LoadAllAsync(TestContext.Current.CancellationToken);

        var loaded = Assert.Single(result);
        Assert.Equal(entry.Id, loaded.Id);
        Assert.Empty(loaded.Tags);
    }

    [Fact]
    public async Task LoadAll_CreatesParentDirectoryIfMissing()
    {
        var nestedDir = Path.Combine(Path.GetTempPath(), $"FilterLibrarySqliteStoreTests_{Guid.NewGuid()}", "nested", "deeper");
        var dbPath = Path.Combine(nestedDir, "filter-library.db");
        _tempDatabases.Add(dbPath);
        Assert.False(Directory.Exists(nestedDir));

        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());

        var result = await store.LoadAllAsync(TestContext.Current.CancellationToken);

        Assert.True(Directory.Exists(nestedDir));
        Assert.Empty(result);
    }

    [Fact]
    public async Task LoadAll_ForwardVersionEvidenceWithMinorityUnloadable_WithholdsBelowSystemicRatio()
    {
        var dbPath = CreateTempDatabasePath();
        var banner = Substitute.For<IErrorBannerService>();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>(), banner);

        // 5 good known-kind rows make the unloadable rows a MINORITY of known kinds: with 3 unloadable of
        // 8 known-kind, the systemic ratio (3*2 = 6 >= 8) is false. The single unknown-kind row is the
        // only thing that trips the breaker (forward-version evidence), so this isolates that path - drop
        // the forward-version-evidence check and the 3 reformatted rows would be wrongly deleted.
        for (var i = 0; i < 5; i++) { await store.AddAsync(BuildFilterEntry($"Good{i}"), TestContext.Current.CancellationToken); }

        var unloadableIds = new[]
        {
            "00000000-0000-0000-0000-0000000000a1", "00000000-0000-0000-0000-0000000000a2",
            "00000000-0000-0000-0000-0000000000a3",
        };
        const string unknownId = "00000000-0000-0000-0000-0000000000b9";

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            foreach (var id in unloadableIds)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = $"INSERT INTO library_entries (id, name, created_utc, kind, payload, is_favorite, last_used_utc, origin, comparison_text, mode, is_excluded) VALUES ('{id}', 'Bad', '2026-05-31T00:00:00.0000000+00:00', 'Filter', 'not valid json', 0, NULL, 'UserSaved', NULL, NULL, NULL);";
                cmd.ExecuteNonQuery();
            }

            using var unknownCmd = connection.CreateCommand();
            unknownCmd.CommandText = $"INSERT INTO library_entries (id, name, created_utc, kind, payload, is_favorite, last_used_utc, origin, comparison_text, mode, is_excluded) VALUES ('{unknownId}', 'Future', '2026-05-31T00:00:00.0000000+00:00', 'SomeFutureKind', '{{}}', 0, NULL, 'UserSaved', NULL, NULL, NULL);";
            unknownCmd.ExecuteNonQuery();
        }

        var result = await store.LoadAllAsync(TestContext.Current.CancellationToken);

        Assert.Equal(5, result.Count);
        foreach (var id in unloadableIds)
        {
            Assert.Equal(1L, CountRows(dbPath, new LibraryEntryId(Guid.Parse(id))));
        }

        banner.ReceivedWithAnyArgs(1).ReportError(default!, default!);
    }

    [Fact]
    public async Task LoadAll_ForwardVersionUnknownKindsPresent_WithholdsUnloadableKnownKindDeletion()
    {
        var dbPath = CreateTempDatabasePath();
        var banner = Substitute.For<IErrorBannerService>();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>(), banner);
        var seed = BuildFilterEntry("Seed");
        await store.AddAsync(seed, TestContext.Current.CancellationToken);
        await store.DeleteAsync(seed.Id, TestContext.Current.CancellationToken);

        // Forward-version DB: a newer app wrote 6 unknown-kind rows AND reformatted Filter payloads so
        // 4 known-kind rows are now unparseable. Over all 10 rows, 4*2 = 8 < 10 would (wrongly) delete
        // the 4 known-kind rows; the breaker must count only known-kind rows / treat the unknown kinds
        // as forward-version evidence and withhold.
        var unknownIds = new[]
        {
            "00000000-0000-0000-0000-0000000000e1", "00000000-0000-0000-0000-0000000000e2",
            "00000000-0000-0000-0000-0000000000e3", "00000000-0000-0000-0000-0000000000e4",
            "00000000-0000-0000-0000-0000000000e5", "00000000-0000-0000-0000-0000000000e6",
        };
        var unloadableIds = new[]
        {
            "00000000-0000-0000-0000-0000000000f1", "00000000-0000-0000-0000-0000000000f2",
            "00000000-0000-0000-0000-0000000000f3", "00000000-0000-0000-0000-0000000000f4",
        };

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            foreach (var id in unknownIds)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = $"INSERT INTO library_entries (id, name, created_utc, kind, payload, is_favorite, last_used_utc, origin, comparison_text, mode, is_excluded) VALUES ('{id}', 'Future', '2026-05-31T00:00:00.0000000+00:00', 'SomeFutureKind', '{{}}', 0, NULL, 'UserSaved', NULL, NULL, NULL);";
                cmd.ExecuteNonQuery();
            }

            foreach (var id in unloadableIds)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = $"INSERT INTO library_entries (id, name, created_utc, kind, payload, is_favorite, last_used_utc, origin, comparison_text, mode, is_excluded) VALUES ('{id}', 'Bad', '2026-05-31T00:00:00.0000000+00:00', 'Filter', 'not valid json', 0, NULL, 'UserSaved', NULL, NULL, NULL);";
                cmd.ExecuteNonQuery();
            }
        }

        var result = await store.LoadAllAsync(TestContext.Current.CancellationToken);

        Assert.Empty(result);
        foreach (var id in unloadableIds)
        {
            Assert.Equal(1L, CountRows(dbPath, new LibraryEntryId(Guid.Parse(id))));
        }

        banner.ReceivedWithAnyArgs(1).ReportError(default!, default!);
    }

    [Fact]
    public async Task LoadAll_FreshDatabase_ReturnsEmpty()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());

        var result = await store.LoadAllAsync(TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task LoadAll_HalfUnloadable_WithholdsDeletionAndLoadsGoodRows()
    {
        var dbPath = CreateTempDatabasePath();
        var banner = Substitute.For<IErrorBannerService>();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>(), banner);
        await store.AddAsync(BuildFilterEntry("GoodA"), TestContext.Current.CancellationToken);
        await store.AddAsync(BuildFilterEntry("GoodB"), TestContext.Current.CancellationToken);

        var badIds = new[]
        {
            new LibraryEntryId(Guid.Parse("00000000-0000-0000-0000-0000000000d1")),
            new LibraryEntryId(Guid.Parse("00000000-0000-0000-0000-0000000000d2")),
        };

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            foreach (var id in badIds)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = $"INSERT INTO library_entries (id, name, created_utc, kind, payload, is_favorite, last_used_utc, origin, comparison_text, mode, is_excluded) VALUES ('{id.Value:D}', 'Bad', '2026-05-31T00:00:00.0000000+00:00', 'Filter', 'not valid json', 0, NULL, 'UserSaved', NULL, NULL, NULL);";
                cmd.ExecuteNonQuery();
            }
        }

        var result = await store.LoadAllAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        foreach (var id in badIds) { Assert.Equal(1L, CountRows(dbPath, id)); }
        banner.ReceivedWithAnyArgs(1).ReportError(default!, default!);
    }

    [Fact]
    public async Task LoadAll_MalformedPayloadKnownKind_DeletesRowAndLogs()
    {
        var dbPath = CreateTempDatabasePath();
        var logger = Substitute.For<ITraceLogger>();
        var store = new FilterLibrarySqliteStore(dbPath, logger);

        var seedEntry = BuildFilterEntry("Seed");
        await store.AddAsync(seedEntry, TestContext.Current.CancellationToken);
        await store.DeleteAsync(seedEntry.Id, TestContext.Current.CancellationToken);

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
        await store.AddAsync(good, TestContext.Current.CancellationToken);

        var result = await store.LoadAllAsync(TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal(good.Id, result[0].Id);
        logger.ReceivedWithAnyArgs(1).Warning(default);
        Assert.Equal(0L, CountRows(dbPath, badId));
    }

    [Fact]
    public async Task LoadAll_SystemicUnloadableRows_WithholdsDeletionAndReportsBanner()
    {
        var dbPath = CreateTempDatabasePath();
        var banner = Substitute.For<IErrorBannerService>();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>(), banner);
        var seed = BuildFilterEntry("Seed");
        await store.AddAsync(seed, TestContext.Current.CancellationToken);
        await store.DeleteAsync(seed.Id, TestContext.Current.CancellationToken);

        var ids = new[]
        {
            new LibraryEntryId(Guid.Parse("00000000-0000-0000-0000-0000000000c1")),
            new LibraryEntryId(Guid.Parse("00000000-0000-0000-0000-0000000000c2")),
            new LibraryEntryId(Guid.Parse("00000000-0000-0000-0000-0000000000c3")),
        };

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            foreach (var id in ids)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = $"INSERT INTO library_entries (id, name, created_utc, kind, payload, is_favorite, last_used_utc, origin, comparison_text, mode, is_excluded) VALUES ('{id.Value:D}', 'Bad', '2026-05-31T00:00:00.0000000+00:00', 'Filter', 'not valid json', 0, NULL, 'UserSaved', NULL, NULL, NULL);";
                cmd.ExecuteNonQuery();
            }
        }

        var result = await store.LoadAllAsync(TestContext.Current.CancellationToken);

        Assert.Empty(result);
        foreach (var id in ids) { Assert.Equal(1L, CountRows(dbPath, id)); }
        banner.ReceivedWithAnyArgs(1).ReportError(default!, default!);
    }

    [Fact]
    public async Task LoadAll_UnknownKind_SkipsRowAndKeepsIt()
    {
        var dbPath = CreateTempDatabasePath();
        var logger = Substitute.For<ITraceLogger>();
        var store = new FilterLibrarySqliteStore(dbPath, logger);
        var seed = BuildFilterEntry("Seed");
        await store.AddAsync(seed, TestContext.Current.CancellationToken);
        await store.DeleteAsync(seed.Id, TestContext.Current.CancellationToken);

        var unknownId = new LibraryEntryId(Guid.Parse("00000000-0000-0000-0000-000000000099"));

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"INSERT INTO library_entries (id, name, created_utc, kind, payload, is_favorite, last_used_utc, origin, comparison_text, mode, is_excluded) VALUES ('{unknownId.Value:D}', 'Unknown', '2026-05-31T00:00:00.0000000+00:00', 'SomeFutureKind', '{{}}', 0, NULL, 'UserSaved', NULL, NULL, NULL);";
            cmd.ExecuteNonQuery();
        }

        var result = await store.LoadAllAsync(TestContext.Current.CancellationToken);

        Assert.Empty(result);
        logger.ReceivedWithAnyArgs(1).Warning(default);
        Assert.Equal(1L, CountRows(dbPath, unknownId));
    }

    [Fact]
    public async Task LoadAll_UnknownKindWithUnparseableColumns_KeepsRow()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var seed = BuildFilterEntry("Seed");
        await store.AddAsync(seed, TestContext.Current.CancellationToken);
        await store.DeleteAsync(seed.Id, TestContext.Current.CancellationToken);

        var unknownId = new LibraryEntryId(Guid.Parse("00000000-0000-0000-0000-0000000000aa"));

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"INSERT INTO library_entries (id, name, created_utc, kind, payload, is_favorite, last_used_utc, origin, comparison_text, mode, is_excluded) VALUES ('{unknownId.Value:D}', 'FutureRow', 'not-a-date', 'SomeFutureKind', '{{}}', 0, NULL, 'UserSaved', NULL, NULL, NULL);";
            cmd.ExecuteNonQuery();
        }

        var result = await store.LoadAllAsync(TestContext.Current.CancellationToken);

        Assert.Empty(result);
        Assert.Equal(1L, CountRows(dbPath, unknownId));
    }

    [Fact]
    public async Task TryBumpLastUsedIfNotFavorite_FavoritedEntry_DoesNotBumpAndReturnsFalse()
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
        await store.AddAsync(fav, TestContext.Current.CancellationToken);

        var bumped = await store.TryBumpLastUsedIfNotFavoriteAsync(fav.Id, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        Assert.False(bumped);
        var loaded = Assert.IsType<LibraryEntrySavedFilter>(Assert.Single(await store.LoadAllAsync(TestContext.Current.CancellationToken)));
        Assert.Null(loaded.LastUsedUtc);
    }

    [Fact]
    public async Task TryBumpLastUsedIfNotFavorite_MissingEntry_ReturnsFalse()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());

        Assert.False(await store.TryBumpLastUsedIfNotFavoriteAsync(LibraryEntryId.Create(), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TryBumpLastUsedIfNotFavorite_NotFavorite_BumpsAndReturnsTrue()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var seeded = BuildAutoTrackedFilterEntry("Level == 4");
        await store.AddAsync(seeded, TestContext.Current.CancellationToken);
        var bumpTo = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        var bumped = await store.TryBumpLastUsedIfNotFavoriteAsync(seeded.Id, bumpTo, TestContext.Current.CancellationToken);

        Assert.True(bumped);
        var loaded = Assert.IsType<LibraryEntrySavedFilter>(Assert.Single(await store.LoadAllAsync(TestContext.Current.CancellationToken)));
        Assert.Equal(bumpTo, loaded.LastUsedUtc);
    }

    [Fact]
    public async Task TryDeleteAutoTrackedIfNotFavorite_AutoTrackedNotFavorite_DeletesAndReturnsTrue()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var seeded = BuildAutoTrackedFilterEntry("Level == 4");
        await store.AddAsync(seeded, TestContext.Current.CancellationToken);

        var deleted = await store.TryDeleteAutoTrackedIfNotFavoriteAsync(seeded.Id, TestContext.Current.CancellationToken);

        Assert.True(deleted);
        Assert.Empty(await store.LoadAllAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TryDeleteAutoTrackedIfNotFavorite_FavoritedAutoTracked_DoesNotDeleteAndReturnsFalse()
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
        await store.AddAsync(fav, TestContext.Current.CancellationToken);

        var deleted = await store.TryDeleteAutoTrackedIfNotFavoriteAsync(fav.Id, TestContext.Current.CancellationToken);

        Assert.False(deleted);
        Assert.Single(await store.LoadAllAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TryDeleteAutoTrackedIfNotFavorite_FilterSetRow_DoesNotDeleteAndReturnsFalse()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);
        var filterSet = new LibraryEntryFilterSet
        {
            Name = "Filter Set",
            CreatedUtc = DateTimeOffset.UtcNow,
            Origin = LibraryEntryOrigin.AutoTracked,
            Filters = [filter],
        };
        await store.AddAsync(filterSet, TestContext.Current.CancellationToken);

        var deleted = await store.TryDeleteAutoTrackedIfNotFavoriteAsync(filterSet.Id, TestContext.Current.CancellationToken);

        Assert.False(deleted);
        Assert.Single(await store.LoadAllAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TryDeleteAutoTrackedIfNotFavorite_MissingEntry_ReturnsFalse()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());

        Assert.False(await store.TryDeleteAutoTrackedIfNotFavoriteAsync(LibraryEntryId.Create(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TryDeleteAutoTrackedIfNotFavorite_UserSavedRow_DoesNotDeleteAndReturnsFalse()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var userSaved = BuildFilterEntry("Saved");
        await store.AddAsync(userSaved, TestContext.Current.CancellationToken);

        var deleted = await store.TryDeleteAutoTrackedIfNotFavoriteAsync(userSaved.Id, TestContext.Current.CancellationToken);

        Assert.False(deleted);
        Assert.Single(await store.LoadAllAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UniqueIndex_DirectInsertOfDuplicateAutoTracked_RaisesConstraintViolation()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        await store.AddAsync(BuildAutoTrackedFilterEntry("Level == 4"), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<SqliteException>(() => store.AddAsync(BuildAutoTrackedFilterEntry("Level == 4"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Update_PersistsTagChanges()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var entry = BuildFilterEntry("Will-be-tagged");

        await store.AddAsync(entry, TestContext.Current.CancellationToken);
        var withTags = entry with { Tags = ["new-tag"] };
        await store.UpdateAsync(withTags, TestContext.Current.CancellationToken);

        var loaded = Assert.IsType<LibraryEntrySavedFilter>(Assert.Single(await store.LoadAllAsync(TestContext.Current.CancellationToken)));
        Assert.Equal(["new-tag"], loaded.Tags);
    }

    [Fact]
    public async Task Update_ReplacesExistingEntry()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var initial = BuildFilterEntry("First");
        await store.AddAsync(initial, TestContext.Current.CancellationToken);

        var updated = new LibraryEntrySavedFilter
        {
            Id = initial.Id,
            Name = "First (renamed)",
            CreatedUtc = initial.CreatedUtc,
            Filter = initial.Filter,
        };

        await store.UpdateAsync(updated, TestContext.Current.CancellationToken);
        var result = await store.LoadAllAsync(TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal("First (renamed)", result[0].Name);
    }

    [Fact]
    public async Task UpdateRange_SkipsAbsentRow_ReturnsOnlyPresentIds()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var present = BuildFilterEntry("Present");
        var absent = BuildFilterEntry("Absent");
        await store.AddAsync(present, TestContext.Current.CancellationToken);

        var ids = await store.UpdateRangeAsync([present with { Tags = ["x"] }, absent with { Tags = ["y"] }], TestContext.Current.CancellationToken);

        Assert.Single(ids);
        Assert.Contains(present.Id, ids);
        Assert.DoesNotContain(absent.Id, ids);
    }

    [Fact]
    public async Task UpdateRange_UpdatesAllPresentRows_ReturnsTheirIds()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var a = BuildFilterEntry("A");
        var b = BuildFilterEntry("B");
        await store.AddAsync(a, TestContext.Current.CancellationToken);
        await store.AddAsync(b, TestContext.Current.CancellationToken);

        var ids = await store.UpdateRangeAsync([a with { Tags = ["x"] }, b with { Tags = ["y"] }], TestContext.Current.CancellationToken);

        Assert.Equal(2, ids.Count);
        Assert.Contains(a.Id, ids);
        Assert.Contains(b.Id, ids);

        var afterLoad = await store.LoadAllAsync(TestContext.Current.CancellationToken);
        Assert.Contains(afterLoad, e => e.Id == a.Id && e.Tags.SequenceEqual(new[] { "x" }));
        Assert.Contains(afterLoad, e => e.Id == b.Id && e.Tags.SequenceEqual(new[] { "y" }));
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

    private static long CountRows(string dbPath, LibraryEntryId id)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM library_entries WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.Value.ToString("D"));
        return (long)cmd.ExecuteScalar()!;
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

    private static LibraryEntryId SeedPreTagsRow(string dbPath)
    {
        var preTagsId = LibraryEntryId.Create();
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        using (var create = connection.CreateCommand())
        {
            create.CommandText = """
                CREATE TABLE library_entries (
                    id              TEXT PRIMARY KEY,
                    name            TEXT NOT NULL,
                    created_utc     TEXT NOT NULL,
                    kind            TEXT NOT NULL,
                    payload         TEXT NOT NULL,
                    is_favorite     INTEGER NOT NULL DEFAULT 0,
                    last_used_utc   TEXT NULL,
                    origin          TEXT NOT NULL DEFAULT 'UserSaved',
                    comparison_text TEXT NULL,
                    mode            TEXT NULL,
                    is_excluded     INTEGER NULL
                );
                """;
            create.ExecuteNonQuery();
        }

        using var insert = connection.CreateCommand();
        insert.CommandText = $"INSERT INTO library_entries (id, name, created_utc, kind, payload, is_favorite, origin, comparison_text, mode, is_excluded) VALUES ('{preTagsId.Value:D}', 'Legacy', '2026-05-31T00:00:00.0000000+00:00', 'Filter', '{{\"Color\":0,\"ComparisonText\":\"Level == 4\",\"IsExcluded\":false,\"Mode\":\"Advanced\"}}', 0, 'UserSaved', 'Level == 4', 'Advanced', 0);";
        insert.ExecuteNonQuery();

        return preTagsId;
    }

    private string CreateTempDatabasePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"FilterLibrarySqliteStoreTests_{Guid.NewGuid()}.db");
        _tempDatabases.Add(path);
        return path;
    }
}
