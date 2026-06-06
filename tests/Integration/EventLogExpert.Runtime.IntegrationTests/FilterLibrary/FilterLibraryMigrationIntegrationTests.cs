// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.FilterPane;
using Fluxor;
using Microsoft.Data.Sqlite;
using NSubstitute;
using System.Text.Json;
using Effects = EventLogExpert.Runtime.FilterLibrary.Effects;

namespace EventLogExpert.Runtime.IntegrationTests.FilterLibrary;

public sealed class FilterLibraryMigrationIntegrationTests : IDisposable
{
    private const string FavoriteFiltersKey = "favorite-filters";
    private const string MigrationSectionsKey = "filter-library-migration-sections";
    private const string SavedGroupsKey = "saved-filters";

    private readonly List<string> _tempDatabases = [];

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
    public async Task Migration_AddRangeThrows_LegacyDataPreserved_DispatchesLoadSuccessEmpty_DoesNotMarkCompleted()
    {
        var dbPath = CreateTempDatabasePath();
        var realStore = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());
        var throwingStore = new ThrowingAddRangeStoreWrapper(realStore);

        var favoritesJson = JsonSerializer.Serialize(new List<string> { "Level == 4" });
        var prefs = new InMemoryLegacyPreferences { [FavoriteFiltersKey] = favoritesJson };
        var migrator = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());
        var effects = BuildEffects(throwingStore, migrator);
        var dispatcher = Substitute.For<IDispatcher>();

        await effects.HandleLoadLibrary(dispatcher);

        Assert.Empty(realStore.LoadAll());
        Assert.True(prefs.ContainsKey(FavoriteFiltersKey));
        Assert.False(prefs.ContainsKey(MigrationSectionsKey));
        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a.Entries.IsEmpty));
        dispatcher.DidNotReceive().Dispatch(Arg.Any<LoadLibraryFailureAction>());
    }

    [Fact]
    public async Task Migration_BothCorruptLegacyData_FirstLoadMigratesNothing_StoreStillEmpty_SecondLoadRetriesAllSections()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());

        var prefs = new InMemoryLegacyPreferences
        {
            [FavoriteFiltersKey] = "{not-json",
            [SavedGroupsKey] = "[\"unterminated",
        };
        var migrator = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());
        var spyMigrator = Substitute.For<ILegacyFilterMigrator>();
        spyMigrator.ShouldRunMigration().Returns(_ => migrator.ShouldRunMigration());
        spyMigrator.BuildEntriesFromLegacy().Returns(_ => migrator.BuildEntriesFromLegacy());
        spyMigrator.When(m => m.MarkMigrationCompleted(Arg.Any<LegacyMigrationSections>()))
            .Do(call => migrator.MarkMigrationCompleted((LegacyMigrationSections)call[0]!));
        var effects = BuildEffects(store, spyMigrator);
        var dispatcher = Substitute.For<IDispatcher>();

        await effects.HandleLoadLibrary(dispatcher);
        await effects.HandleLoadLibrary(dispatcher);

        spyMigrator.Received(2).BuildEntriesFromLegacy();
        Assert.Empty(store.LoadAll());
        Assert.Equal("4", prefs.GetString(MigrationSectionsKey));
    }

    [Fact]
    public async Task Migration_Concurrent_TwoSimultaneousLoadLibraryCalls_MigratorBuildCalledExactlyOnce()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());

        var favoritesJson = JsonSerializer.Serialize(new List<string> { "Level == 4" });
        var prefs = new InMemoryLegacyPreferences { [FavoriteFiltersKey] = favoritesJson };
        var innerMigrator = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());
        var spyMigrator = Substitute.For<ILegacyFilterMigrator>();
        spyMigrator.ShouldRunMigration().Returns(_ => innerMigrator.ShouldRunMigration());
        spyMigrator.BuildEntriesFromLegacy().Returns(_ => innerMigrator.BuildEntriesFromLegacy());
        spyMigrator.When(m => m.MarkMigrationCompleted(Arg.Any<LegacyMigrationSections>()))
            .Do(call => innerMigrator.MarkMigrationCompleted((LegacyMigrationSections)call[0]!));
        var effects = BuildEffects(store, spyMigrator);
        var dispatcher = Substitute.For<IDispatcher>();

        var ct = TestContext.Current.CancellationToken;
        var t1 = Task.Run(() => effects.HandleLoadLibrary(dispatcher), ct);
        var t2 = Task.Run(() => effects.HandleLoadLibrary(dispatcher), ct);
        await Task.WhenAll(t1, t2);

        spyMigrator.Received(1).BuildEntriesFromLegacy();
        var loaded = store.LoadAll();
        Assert.Single(loaded);
        dispatcher.Received(2).Dispatch(Arg.Any<LoadLibrarySuccessAction>());
    }

    [Fact]
    public async Task Migration_CorruptGroupsLegacyData_FirstLoadMigratesFavorites_SecondLoadRetriesGroupsButStillFails_NoDuplicateFavoriteInsertion()
    {
        // Behavior verified: with per-section flag check + DedupMigrationEntriesAgainstExisting, partial-success
        // migration retries on every launch (Groups bit unset → ShouldRunMigration true), but the already-completed
        // Favorites section is skipped in BuildEntriesFromLegacy AND dedup defends against re-insertion if the bit
        // somehow regressed. The corrupt Groups JSON is preserved indefinitely for a future fix.
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());

        var favoritesJson = JsonSerializer.Serialize(new List<string> { "Level == 4" });
        var prefs = new InMemoryLegacyPreferences
        {
            [FavoriteFiltersKey] = favoritesJson,
            [SavedGroupsKey] = "{not-json",
        };
        var migrator = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());
        var spyMigrator = Substitute.For<ILegacyFilterMigrator>();
        spyMigrator.ShouldRunMigration().Returns(_ => migrator.ShouldRunMigration());
        spyMigrator.BuildEntriesFromLegacy().Returns(_ => migrator.BuildEntriesFromLegacy());
        spyMigrator.When(m => m.MarkMigrationCompleted(Arg.Any<LegacyMigrationSections>()))
            .Do(call => migrator.MarkMigrationCompleted((LegacyMigrationSections)call[0]!));
        var effects = BuildEffects(store, spyMigrator);
        var dispatcher = Substitute.For<IDispatcher>();

        await effects.HandleLoadLibrary(dispatcher);
        await effects.HandleLoadLibrary(dispatcher);

        // Both loads invoke the migrator because ShouldRunMigration stays true while Groups remains unmigrated.
        spyMigrator.Received(2).BuildEntriesFromLegacy();
        // Favorite migrated exactly once (dedup prevents duplicate on retry).
        Assert.Single(store.LoadAll());
        // Bitmask: Favorites (1) | Recents (4) = 5; Groups (2) stays unset because the JSON is still corrupt.
        Assert.Equal("5", prefs.GetString(MigrationSectionsKey));
        // Corrupt Groups JSON preserved for future fix attempt.
        Assert.True(prefs.ContainsKey(SavedGroupsKey));
    }

    [Fact]
    public async Task Migration_EndToEnd_EmptyDb_LegacyFavoritesAndGroups_PopulatesStoreCorrectly()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());

        var favoritesJson = JsonSerializer.Serialize(new List<string> { "Level == 2", "Source == \"x\"" });
        var groupsJson = JsonSerializer.Serialize(new List<SavedFilterGroup>
        {
            new()
            {
                Name = "PresetA",
                Filters = [SavedFilter.TryCreate("Level == 4")!, SavedFilter.TryCreate("Level == 5")!],
            },
        });
        var prefs = new InMemoryLegacyPreferences
        {
            [FavoriteFiltersKey] = favoritesJson,
            [SavedGroupsKey] = groupsJson,
        };
        var migrator = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());
        var effects = BuildEffects(store, migrator);
        var dispatcher = Substitute.For<IDispatcher>();

        await effects.HandleLoadLibrary(dispatcher);

        var loaded = store.LoadAll();
        Assert.Equal(3, loaded.Count);
        var favorites = loaded.OfType<LibraryEntrySavedFilter>().ToList();
        Assert.Equal(2, favorites.Count);
        Assert.All(favorites, f => Assert.True(f.IsFavorite));
        Assert.All(favorites, f => Assert.Equal(LibraryEntryOrigin.UserSaved, f.Origin));
        var filterSet = Assert.Single(loaded.OfType<LibraryEntryFilterSet>());
        Assert.Equal("PresetA", filterSet.Name);
        Assert.Equal(2, filterSet.Filters.Count);
        Assert.Equal(LibraryEntryOrigin.UserSaved, filterSet.Origin);

        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a.Entries.Count == 3));
        dispatcher.DidNotReceive().Dispatch(Arg.Any<LoadLibraryFailureAction>());

        Assert.True(prefs.ContainsKey(FavoriteFiltersKey));
        Assert.True(prefs.ContainsKey(SavedGroupsKey));
        Assert.Equal("7", prefs.GetString(MigrationSectionsKey));
    }

    [Fact]
    public async Task Migration_FreshUser_NoLegacyKeys_MarksCompletedWithAllSections_SecondLoadSkipsMigrator()
    {
        var dbPath = CreateTempDatabasePath();
        var store = new FilterLibrarySqliteStore(dbPath, Substitute.For<ITraceLogger>());

        var prefs = new InMemoryLegacyPreferences();
        var migrator = new LegacyFilterMigrator(prefs, Substitute.For<ITraceLogger>());
        var spyMigrator = Substitute.For<ILegacyFilterMigrator>();
        spyMigrator.ShouldRunMigration().Returns(_ => migrator.ShouldRunMigration());
        spyMigrator.BuildEntriesFromLegacy().Returns(_ => migrator.BuildEntriesFromLegacy());
        spyMigrator.When(m => m.MarkMigrationCompleted(Arg.Any<LegacyMigrationSections>()))
            .Do(call => migrator.MarkMigrationCompleted((LegacyMigrationSections)call[0]!));
        var effects = BuildEffects(store, spyMigrator);
        var dispatcher = Substitute.For<IDispatcher>();

        await effects.HandleLoadLibrary(dispatcher);
        await effects.HandleLoadLibrary(dispatcher);

        spyMigrator.Received(1).BuildEntriesFromLegacy();
        spyMigrator.Received(2).ShouldRunMigration();
        spyMigrator.Received(1).MarkMigrationCompleted(
            LegacyMigrationSections.Favorites | LegacyMigrationSections.Groups | LegacyMigrationSections.Recents);
        Assert.Equal("7", prefs.GetString(MigrationSectionsKey));
    }

    private static Effects BuildEffects(IFilterLibraryStore store, ILegacyFilterMigrator migrator, IBackslashNameMigrator? backslashMigrator = null)
    {
        var libraryState = Substitute.For<IState<FilterLibraryState>>();
        libraryState.Value.Returns(new FilterLibraryState());
        var paneState = Substitute.For<IState<FilterPaneState>>();
        paneState.Value.Returns(new FilterPaneState());

        backslashMigrator ??= Substitute.For<IBackslashNameMigrator>();

        return new Effects(store, libraryState, paneState, migrator, backslashMigrator, Substitute.For<IAnnouncementService>(), Substitute.For<ITraceLogger>());
    }

    private string CreateTempDatabasePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"FilterLibraryMigrationIntegrationTests_{Guid.NewGuid()}.db");
        _tempDatabases.Add(path);
        return path;
    }

    private sealed class InMemoryLegacyPreferences : ILegacyPreferences
    {
        private readonly Dictionary<string, string> _values = [];

        public string? this[string key]
        {
            set => _values[key] = value ?? throw new ArgumentNullException(nameof(value));
        }

        public bool ContainsKey(string key) => _values.ContainsKey(key);

        public string? GetString(string key) => _values.TryGetValue(key, out var value) ? value : null;

        public void Remove(string key) => _values.Remove(key);

        public void SetString(string key, string value) => _values[key] = value;
    }

    private sealed class ThrowingAddRangeStoreWrapper(IFilterLibraryStore inner) : IFilterLibraryStore
    {
        public void Add(LibraryEntry entry) => inner.Add(entry);

        public (LibraryEntry Entry, bool WasInserted) AddOrReturnExistingFilter(LibraryEntrySavedFilter candidate) =>
            inner.AddOrReturnExistingFilter(candidate);

        public void AddRange(IEnumerable<LibraryEntry> entries) => throw new SqliteException("simulated AddRange failure", 1);

        public void Delete(LibraryEntryId entryId) => inner.Delete(entryId);

        public IReadOnlyList<LibraryEntry> LoadAll() => inner.LoadAll();

        public bool TryBumpLastUsedIfNotFavorite(LibraryEntryId entryId, DateTimeOffset lastUsedUtc) =>
            inner.TryBumpLastUsedIfNotFavorite(entryId, lastUsedUtc);

        public bool TryDeleteAutoTrackedIfNotFavorite(LibraryEntryId entryId) =>
            inner.TryDeleteAutoTrackedIfNotFavorite(entryId);

        public void Update(LibraryEntry entry) => inner.Update(entry);

        public IReadOnlyList<LibraryEntryId> UpdateRange(IReadOnlyList<LibraryEntry> entries) => inner.UpdateRange(entries);
    }
}
