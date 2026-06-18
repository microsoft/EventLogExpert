// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.Scenarios.Favorites;
using Microsoft.Data.Sqlite;
using NSubstitute;

namespace EventLogExpert.Runtime.IntegrationTests.Scenarios;

public sealed class ScenarioFavoriteSqliteStoreTests : IDisposable
{
    private readonly List<string> _tempDatabases = [];

    [Fact]
    public async Task Add_PersistsAndIsLoadable()
    {
        var dbPath = CreateTempDatabasePath();
        ScenarioFavoriteSqliteStore store = new(dbPath);

        await store.AddAsync("application-crashes", TestContext.Current.CancellationToken);
        var result = await store.LoadAllAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["application-crashes"], result);
    }

    [Fact]
    public async Task Add_SameScenarioTwice_IsIdempotent()
    {
        var dbPath = CreateTempDatabasePath();
        ScenarioFavoriteSqliteStore store = new(dbPath);

        await store.AddAsync("application-crashes", TestContext.Current.CancellationToken);
        await store.AddAsync("application-crashes", TestContext.Current.CancellationToken);
        var result = await store.LoadAllAsync(TestContext.Current.CancellationToken);

        Assert.Single(result);
    }

    [Fact]
    public async Task Delete_RemovesOnlyTheNamedScenario()
    {
        var dbPath = CreateTempDatabasePath();
        ScenarioFavoriteSqliteStore store = new(dbPath);
        await store.AddAsync("application-crashes", TestContext.Current.CancellationToken);
        await store.AddAsync("failed-services-at-boot", TestContext.Current.CancellationToken);

        await store.DeleteAsync("application-crashes", TestContext.Current.CancellationToken);
        var result = await store.LoadAllAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["failed-services-at-boot"], result);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        foreach (var path in _tempDatabases)
        {
            try
            {
                if (File.Exists(path)) { File.Delete(path); }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task LoadAll_OnFreshDatabase_ReturnsEmpty()
    {
        var dbPath = CreateTempDatabasePath();
        ScenarioFavoriteSqliteStore store = new(dbPath);

        var result = await store.LoadAllAsync(TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task SharesDatabaseFileWithFilterLibrary_WithoutInterference()
    {
        var dbPath = CreateTempDatabasePath();
        ScenarioFavoriteSqliteStore favorites = new(dbPath);
        FilterLibrarySqliteStore library = new(dbPath, Substitute.For<ITraceLogger>());
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);
        LibraryEntrySavedFilter entry = new() { Name = "Shared", CreatedUtc = DateTimeOffset.UtcNow, Filter = filter };

        await library.AddAsync(entry, TestContext.Current.CancellationToken);
        await favorites.AddAsync("application-crashes", TestContext.Current.CancellationToken);

        var favoriteIds = await favorites.LoadAllAsync(TestContext.Current.CancellationToken);
        var entries = await library.LoadAllAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["application-crashes"], favoriteIds);
        Assert.Equal("Shared", Assert.Single(entries).Name);
    }

    private string CreateTempDatabasePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ScenarioFavoriteSqliteStoreTests_{Guid.NewGuid()}.db");
        _tempDatabases.Add(path);
        return path;
    }
}
