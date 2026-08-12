// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using EventLogExpert.Provider.Maintenance;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.Database.Upgrade;
using EventLogExpert.Runtime.IntegrationTests.TestUtils;
using EventLogExpert.Runtime.IntegrationTests.TestUtils.Constants;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.IO.Compression;

namespace EventLogExpert.Runtime.IntegrationTests.Database;

public sealed class DatabaseServiceTests : IDisposable
{
    private const int LinkedCtsPropagationDelayMs = 250;
    private const int SecondBatchStartDelayMs = 100;

    private static readonly TimeSpan s_disposeTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan s_testTimeout = TimeSpan.FromSeconds(10);

    private readonly IProviderDatabaseMaintenance _maintenance;
    private readonly ServiceProvider _maintenanceProvider;
    private readonly List<DatabaseService> _services = [];
    private readonly string _testDirectory;

    public DatabaseServiceTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"DatabaseServiceTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);

        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ITraceLogger>());
        services.AddEventLogProviderDatabase();
        _maintenanceProvider = services.BuildServiceProvider();
        _maintenance = _maintenanceProvider.GetRequiredService<IProviderDatabaseMaintenance>();
    }

    [Fact]
    public void ActiveDatabases_ShouldReturnFullPathsOfEnabledReadyEntriesOnly()
    {
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);
        CreateDatabaseFile(databasePath, Constants.TestDb2);
        CreateDatabaseFile(databasePath, Constants.TestDb3);

        var preferences = Substitute.For<IDatabasePreferencesProvider>();
        preferences.DisabledDatabasesPreference.Returns([Constants.TestDb2]);

        var service = CreateDatabaseService(preferences);
        service.MarkStatus(Constants.TestDb1, DatabaseStatus.Ready);
        service.MarkStatus(Constants.TestDb2, DatabaseStatus.Ready);
        service.MarkStatus(Constants.TestDb3, DatabaseStatus.UpgradeRequired);

        var activeDatabases = service.Paths;

        Assert.Single(activeDatabases);
        Assert.Equal(Path.Join(databasePath, Constants.TestDb1), activeDatabases[0]);
    }

    [Fact]
    public async Task ClassifyEntriesAsync_WhenAnyStatusChanges_ShouldRaiseEntriesChangedExactlyOnce()
    {
        var databasePath = CreateDatabaseDirectory();
        DatabaseSeedUtils.SeedV3Schema(Path.Combine(databasePath, Constants.TestDb1));
        DatabaseSeedUtils.SeedV3Schema(Path.Combine(databasePath, Constants.TestDb2));

        var service = CreateDatabaseService();

        service.MarkStatus(Constants.TestDb1, DatabaseStatus.NotClassified);
        service.MarkStatus(Constants.TestDb2, DatabaseStatus.NotClassified);

        var raisedCount = 0;
        service.EntriesChanged += (_, _) => raisedCount++;

        await service.ClassifyEntriesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, raisedCount);
    }

    [Fact]
    public async Task ClassifyEntriesAsync_WhenEmptyFile_ShouldClassifyAsUnrecognizedSchemaWithoutMutation()
    {
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var dbPath = Path.Combine(databasePath, Constants.TestDb1);
        var sizeBefore = new FileInfo(dbPath).Length;

        var service = CreateDatabaseService();

        await service.ClassifyEntriesAsync(TestContext.Current.CancellationToken);

        SqliteConnection.ClearAllPools();
        var sizeAfter = new FileInfo(dbPath).Length;

        Assert.Equal(sizeBefore, sizeAfter);
        Assert.False(File.Exists(dbPath + "-wal"), "WAL sidecar should not be created during classification.");
        Assert.False(File.Exists(dbPath + "-shm"), "SHM sidecar should not be created during classification.");

        var entry = Assert.Single(service.Entries);
        Assert.Equal(DatabaseStatus.UnrecognizedSchema, entry.Status);
    }

    [Theory]
    [InlineData("v1.db")]
    [InlineData("v2.db")]
    public async Task ClassifyEntriesAsync_WhenLegacySchema_ShouldDetectAsObsoleteSchema(string fileName)
    {
        var databasePath = CreateDatabaseDirectory();
        var dbPath = Path.Combine(databasePath, fileName);

        if (fileName == "v1.db")
        {
            DatabaseSeedUtils.SeedV1Schema(dbPath);
        }
        else
        {
            DatabaseSeedUtils.SeedV2Schema(dbPath);
        }

        var service = CreateDatabaseService();

        await service.ClassifyEntriesAsync(TestContext.Current.CancellationToken);

        var entry = Assert.Single(service.Entries);
        Assert.Equal(DatabaseStatus.ObsoleteSchema, entry.Status);
    }

    [Fact]
    public async Task ClassifyEntriesAsync_WhenNoStatusesChange_ShouldNotRaiseEntriesChanged()
    {
        var databasePath = CreateDatabaseDirectory();
        DatabaseSeedUtils.SeedV4Schema(Path.Combine(databasePath, Constants.TestDb1));

        var service = CreateDatabaseService();
        service.MarkStatus(Constants.TestDb1, DatabaseStatus.Ready);

        var raisedCount = 0;
        service.EntriesChanged += (_, _) => raisedCount++;

        await service.ClassifyEntriesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, raisedCount);
    }

    [Fact]
    public async Task ClassifyEntriesAsync_WhenObsoleteSchemaWithUpgradeBak_ShouldNotDeleteBakAndBackupExistsFalse()
    {
        var databasePath = CreateDatabaseDirectory();
        var dbPath = Path.Combine(databasePath, "v1.db");
        DatabaseSeedUtils.SeedV1Schema(dbPath);

        var bakPath = dbPath + DatabaseService.UpgradeBackupSuffix;
        File.WriteAllText(bakPath, "stale-backup-contents");

        var service = CreateDatabaseService();

        await service.ClassifyEntriesAsync(TestContext.Current.CancellationToken);

        var entry = Assert.Single(service.Entries);
        Assert.Equal(DatabaseStatus.ObsoleteSchema, entry.Status);
        Assert.False(entry.BackupExists);
        Assert.True(File.Exists(bakPath));
    }

    [Fact]
    public async Task ClassifyEntriesAsync_WhenOneEntryFails_ShouldQuarantineAsClassificationFailed()
    {
        var databasePath = CreateDatabaseDirectory();

        var v3Path = Path.Combine(databasePath, "v3.db");
        DatabaseSeedUtils.SeedV3Schema(v3Path);

        var lockedPath = Path.Combine(databasePath, "locked.db");
        DatabaseSeedUtils.SeedV3Schema(lockedPath);

        using var blockingHandle = new FileStream(
            lockedPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        var service = CreateDatabaseService();

        await service.ClassifyEntriesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, service.Entries.Count);
        var v3Entry = service.Entries.Single(entry => entry.FileName == "v3.db");
        var lockedEntry = service.Entries.Single(entry => entry.FileName == "locked.db");

        Assert.Equal(DatabaseStatus.UpgradeRequired, v3Entry.Status);
        Assert.Equal(DatabaseStatus.ClassificationFailed, lockedEntry.Status);

        Assert.DoesNotContain(lockedPath, service.Paths);
    }

    [Fact]
    public async Task
        ClassifyEntriesAsync_WhenSqliteFileWithoutProviderDetailsTable_ShouldClassifyAsUnrecognizedSchema()
    {
        var databasePath = CreateDatabaseDirectory();
        var dbPath = Path.Combine(databasePath, Constants.TestDb1);

        await using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE \"SomeOtherTable\" (\"Id\" INTEGER PRIMARY KEY, \"Value\" TEXT);";
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        SqliteConnection.ClearAllPools();

        var service = CreateDatabaseService();

        await service.ClassifyEntriesAsync(TestContext.Current.CancellationToken);

        var entry = Assert.Single(service.Entries);
        Assert.Equal(DatabaseStatus.UnrecognizedSchema, entry.Status);
        Assert.DoesNotContain(dbPath, service.Paths);
    }

    [Fact]
    public async Task
        ClassifyEntriesAsync_WhenV3BakAppearsBetweenClassifications_ShouldUpdateBackupExistsAndRaiseEntriesChanged()
    {
        var databasePath = CreateDatabaseDirectory();
        var dbPath = Path.Combine(databasePath, Constants.TestDb1);
        DatabaseSeedUtils.SeedV3Schema(dbPath);

        var service = CreateDatabaseService();

        var firstEntry = Assert.Single(service.Entries);
        Assert.Equal(DatabaseStatus.UpgradeRequired, firstEntry.Status);
        Assert.False(firstEntry.BackupExists);

        var bakPath = dbPath + DatabaseService.UpgradeBackupSuffix;
        File.WriteAllText(bakPath, "interrupted-upgrade-backup");

        var raisedCount = 0;
        service.EntriesChanged += (_, _) => raisedCount++;

        await service.ClassifyEntriesAsync(TestContext.Current.CancellationToken);

        var entry = Assert.Single(service.Entries);
        Assert.Equal(DatabaseStatus.UpgradeRequired, entry.Status);
        Assert.True(entry.BackupExists);
        Assert.Equal(1, raisedCount);
    }

    [Fact]
    public async Task ClassifyEntriesAsync_WhenV3SchemaWithUpgradeBak_ShouldDetectAsUpgradeRequiredAndBackupExistsTrue()
    {
        var databasePath = CreateDatabaseDirectory();
        var dbPath = Path.Combine(databasePath, Constants.TestDb1);
        DatabaseSeedUtils.SeedV3Schema(dbPath);

        var bakPath = dbPath + DatabaseService.UpgradeBackupSuffix;
        File.WriteAllText(bakPath, "interrupted-upgrade-backup");

        var service = CreateDatabaseService();

        await service.ClassifyEntriesAsync(TestContext.Current.CancellationToken);

        var entry = Assert.Single(service.Entries);
        Assert.Equal(DatabaseStatus.UpgradeRequired, entry.Status);
        Assert.True(entry.BackupExists);
        Assert.True(File.Exists(bakPath), ".upgrade.bak must be preserved for V3 entries so recovery can restore it.");
    }

    [Fact]
    public async Task ClassifyEntriesAsync_WhenV3Schema_ShouldDetectAsUpgradeRequired()
    {
        var databasePath = CreateDatabaseDirectory();
        var dbPath = Path.Combine(databasePath, Constants.TestDb1);
        DatabaseSeedUtils.SeedV3Schema(dbPath);

        var service = CreateDatabaseService();

        await service.ClassifyEntriesAsync(TestContext.Current.CancellationToken);

        var entry = Assert.Single(service.Entries);
        Assert.Equal(DatabaseStatus.UpgradeRequired, entry.Status);
        Assert.False(entry.BackupExists);
    }

    [Fact]
    public async Task ClassifyEntriesAsync_WhenV4SchemaWithUpgradeBak_ShouldDeleteBakAndDetectAsReady()
    {
        var databasePath = CreateDatabaseDirectory();
        var dbPath = Path.Combine(databasePath, Constants.TestDb1);
        DatabaseSeedUtils.SeedV4Schema(dbPath);

        var bakPath = dbPath + DatabaseService.UpgradeBackupSuffix;
        File.WriteAllText(bakPath, "stale-backup-from-successful-upgrade");

        var service = CreateDatabaseService();

        await service.ClassifyEntriesAsync(TestContext.Current.CancellationToken);

        var entry = Assert.Single(service.Entries);
        Assert.Equal(DatabaseStatus.Ready, entry.Status);
        Assert.False(entry.BackupExists);
        Assert.False(File.Exists(bakPath), "Stale .upgrade.bak must be cleaned up once the main file reaches V4.");
    }

    [Fact]
    public async Task ClassifyEntriesAsync_WhenV4Schema_ShouldDetectAsReady()
    {
        var databasePath = CreateDatabaseDirectory();
        var dbPath = Path.Combine(databasePath, Constants.TestDb1);
        DatabaseSeedUtils.SeedV4Schema(dbPath);

        var service = CreateDatabaseService();

        await service.ClassifyEntriesAsync(TestContext.Current.CancellationToken);

        var entry = Assert.Single(service.Entries);
        Assert.Equal(DatabaseStatus.Ready, entry.Status);
        Assert.False(entry.BackupExists);
    }

    [Fact]
    public void Constructor_WhenCalled_ShouldSeedEntriesFromDisk()
    {
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);
        CreateDatabaseFile(databasePath, Constants.TestDb2);

        var service = CreateDatabaseService();

        Assert.Equal(2, service.Entries.Count);
        Assert.Contains(service.Entries, entry => entry.FileName == Constants.TestDb1);
        Assert.Contains(service.Entries, entry => entry.FileName == Constants.TestDb2);
        Assert.All(service.Entries, entry => Assert.True(entry.IsEnabled));
        Assert.All(service.Entries, entry => Assert.False(entry.BackupExists));
    }

    [Fact]
    public void Constructor_WhenDatabaseDirectoryDoesNotExist_ShouldHaveEmptyEntries()
    {
        var service = CreateDatabaseService();

        Assert.Empty(service.Entries);
    }

    [Fact]
    public void Constructor_WhenDisabledFilenameIsCaseDifferent_ShouldStillMarkDisabled()
    {
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var preferences = Substitute.For<IDatabasePreferencesProvider>();
        preferences.DisabledDatabasesPreference.Returns([Constants.TestDb1.ToUpper()]);

        var service = CreateDatabaseService(preferences);

        Assert.Single(service.Entries);
        Assert.False(service.Entries[0].IsEnabled);
    }

    [Fact]
    public void Constructor_WhenNonDbFilesPresent_ShouldOnlyIncludeDbFiles()
    {
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);
        File.WriteAllText(Path.Combine(databasePath, "ignored.txt"), "");
        File.WriteAllText(Path.Combine(databasePath, "ignored.json"), "");

        var service = CreateDatabaseService();

        Assert.Single(service.Entries);
        Assert.Equal(Constants.TestDb1, service.Entries[0].FileName);
    }

    [Fact]
    public async Task DeleteEntryWithBackupAsync_BackupMissing_StillSucceedsAndRemovesEntry()
    {
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var service = CreateDatabaseService();

        var result = await service.DeleteEntryWithBackupAsync(Constants.TestDb1, TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.False(File.Exists(Path.Combine(databasePath, Constants.TestDb1)));
        Assert.Empty(service.Entries);
    }

    [Fact]
    public async Task DeleteEntryWithBackupAsync_DeletesMainAllSidecarsAndBackup_RemovesFromEntries()
    {
        var databasePath = CreateDatabaseDirectory();
        var dbPath = Path.Combine(databasePath, Constants.TestDb1);
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var journalPath = dbPath + "-journal";
        var walPath = dbPath + "-wal";
        var shmPath = dbPath + "-shm";
        var bakPath = dbPath + DatabaseService.UpgradeBackupSuffix;
        File.WriteAllText(journalPath, "rollback-journal");
        File.WriteAllText(walPath, "wal-content");
        File.WriteAllText(shmPath, "shm-content");
        File.WriteAllText(bakPath, "upgrade-backup");

        var service = CreateDatabaseService();

        var result = await service.DeleteEntryWithBackupAsync(Constants.TestDb1, TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.False(File.Exists(dbPath));
        Assert.False(File.Exists(journalPath));
        Assert.False(File.Exists(walPath));
        Assert.False(File.Exists(shmPath));
        Assert.False(File.Exists(bakPath));
        Assert.Empty(service.Entries);
    }

    [Fact]
    public async Task DeleteEntryWithBackupAsync_DoesNotTouchUserCreatedDotBakFiles()
    {
        var databasePath = CreateDatabaseDirectory();
        var dbPath = Path.Combine(databasePath, Constants.TestDb1);
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        const string userBackupContent = "user-created-content";
        var userBakPath = dbPath + ".bak";
        File.WriteAllText(userBakPath, userBackupContent);

        var service = CreateDatabaseService();

        var result = await service.DeleteEntryWithBackupAsync(Constants.TestDb1, TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.False(File.Exists(dbPath));
        Assert.True(File.Exists(userBakPath));
        Assert.Equal(userBackupContent, File.ReadAllText(userBakPath));
    }

    [Fact]
    public async Task DeleteEntryWithBackupAsync_DuringInFlightUpgrade_ShouldThrowInvalidOperationException()
    {
        var databasePath = CreateDatabaseDirectory();
        DatabaseSeedUtils.SeedV3Schema(Path.Combine(databasePath, Constants.TestDb1));

        var service = CreateDatabaseService();

        using var inFlight = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        service.UpgradeBatchProgress += (_, args) =>
        {
            if (args.Phase == UpgradePhase.BackingUp)
            {
                inFlight.Set();
                release.Wait(s_testTimeout);
            }
        };

        var batchTask = service.UpgradeBatchAsync(
            [Constants.TestDb1],
            UpgradeProgressScope.Background,
            TestContext.Current.CancellationToken);

        Assert.True(inFlight.Wait(s_testTimeout, TestContext.Current.CancellationToken));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteEntryWithBackupAsync(Constants.TestDb1, TestContext.Current.CancellationToken));

        Assert.Contains("another operation is in progress", ex.Message, StringComparison.OrdinalIgnoreCase);

        release.Set();
        await batchTask;
    }

    [Fact]
    public async Task DeleteEntryWithBackupAsync_RaisesEntriesChangedExactlyOnce()
    {
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var service = CreateDatabaseService();
        var raisedCount = 0;
        service.EntriesChanged += (_, _) => Interlocked.Increment(ref raisedCount);

        await service.DeleteEntryWithBackupAsync(Constants.TestDb1, TestContext.Current.CancellationToken);

        Assert.Equal(1, raisedCount);
    }

    [Fact]
    public async Task DeleteEntryWithBackupAsync_TokenAlreadyCanceled_Throws()
    {
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var service = CreateDatabaseService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await service.DeleteEntryWithBackupAsync(Constants.TestDb1, cts.Token));

        Assert.True(File.Exists(Path.Combine(databasePath, Constants.TestDb1)));
    }

    [Fact]
    public async Task DeleteEntryWithBackupAsync_UnknownFileName_Throws()
    {
        CreateDatabaseDirectory();
        var service = CreateDatabaseService();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.DeleteEntryWithBackupAsync("does-not-exist.db", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteEntryWithBackupAsync_WhenSidecarDeleteFails_PreservesMainAndEntryAndReturnsFalse()
    {
        var databasePath = CreateDatabaseDirectory();
        var dbPath = Path.Combine(databasePath, Constants.TestDb1);
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var walPath = dbPath + "-wal";
        File.WriteAllText(walPath, "wal-content");

        using var lockHandle = new FileStream(walPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var service = CreateDatabaseService();

        var result = await service.DeleteEntryWithBackupAsync(Constants.TestDb1, TestContext.Current.CancellationToken);

        Assert.False(result);
        Assert.True(File.Exists(dbPath));
        Assert.Single(service.Entries);
    }

    public void Dispose()
    {
        foreach (var service in _services)
        {
            try
            {
                service.DisposeAsync().AsTask().Wait(s_disposeTimeout);
            }
            catch (Exception)
            {
                // Best-effort cleanup; a hung disposal should not fail an otherwise-passing test.
            }
        }

        _maintenanceProvider.Dispose();

        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; a residual SQLite handle should not fail an otherwise-passing test.
            }
        }
    }

    [Fact]
    public async Task DisposeAsync_WithPendingBatches_ShouldCancelInFlightAndPendingBatches()
    {
        var databasePath = CreateDatabaseDirectory();
        DatabaseSeedUtils.SeedV3Schema(Path.Combine(databasePath, Constants.TestDb1));
        DatabaseSeedUtils.SeedV3Schema(Path.Combine(databasePath, Constants.TestDb2));

        var service = CreateDatabaseService();

        using var inFlight = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        service.UpgradeBatchProgress += (_, args) =>
        {
            if (args.Phase == UpgradePhase.BackingUp &&
                string.Equals(args.FileName, Constants.TestDb1, StringComparison.OrdinalIgnoreCase))
            {
                inFlight.Set();
                release.Wait(s_testTimeout);
            }
        };

        var firstBatch = service.UpgradeBatchAsync(
            [Constants.TestDb1],
            UpgradeProgressScope.Background,
            TestContext.Current.CancellationToken);

        Assert.True(inFlight.Wait(s_testTimeout, TestContext.Current.CancellationToken));

        var pendingBatch = service.UpgradeBatchAsync(
            [Constants.TestDb2],
            UpgradeProgressScope.Background,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, service.QueuedBatchCount);

        var disposeTask = service.DisposeAsync().AsTask();

        await Task.Delay(LinkedCtsPropagationDelayMs, TestContext.Current.CancellationToken);

        release.Set();
        await disposeTask;

        var firstResult = await firstBatch;
        Assert.Empty(firstResult.Succeeded);
        Assert.Single(firstResult.Cancelled);
        Assert.Equal(Constants.TestDb1, firstResult.Cancelled[0]);

        var pendingResult = await pendingBatch;
        Assert.Empty(pendingResult.Succeeded);
        Assert.Single(pendingResult.Cancelled);
        Assert.Equal(Constants.TestDb2, pendingResult.Cancelled[0]);
    }

    [Fact]
    public void EntriesChanged_MultipleSubscribers_FirstThrows_ShouldStillInvokeRest()
    {
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var service = CreateDatabaseService();

        var secondSubscriberInvocations = 0;

        service.EntriesChanged += (_, _) => throw new InvalidOperationException("first subscriber throws");
        service.EntriesChanged += (_, _) => Interlocked.Increment(ref secondSubscriberInvocations);

        service.Toggle(Constants.TestDb1);

        Assert.Equal(1, secondSubscriberInvocations);
    }

    [Fact]
    public void Entries_WhenMixedVersionedAndNonVersioned_ShouldSortCorrectly()
    {
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.Windows10 + ".db");
        CreateDatabaseFile(databasePath, Constants.Windows11 + ".db");
        CreateDatabaseFile(databasePath, Constants.SimpleDatabase + ".db");
        CreateDatabaseFile(databasePath, Constants.AnotherDb + ".db");

        var service = CreateDatabaseService();

        Assert.Equal(4, service.Entries.Count);
        Assert.Equal(Constants.AnotherDb + ".db", service.Entries[0].FileName);
        Assert.Equal(Constants.SimpleDatabase + ".db", service.Entries[1].FileName);
        Assert.Equal(Constants.Windows11 + ".db", service.Entries[2].FileName);
        Assert.Equal(Constants.Windows10 + ".db", service.Entries[3].FileName);
    }

    [Fact]
    public void Entries_WhenNumericVersions_ShouldSortNumericallyNotLexicographically()
    {
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.Server1 + ".db");
        CreateDatabaseFile(databasePath, Constants.Server2 + ".db");
        CreateDatabaseFile(databasePath, Constants.Server10 + ".db");
        CreateDatabaseFile(databasePath, Constants.Server20 + ".db");

        var service = CreateDatabaseService();

        Assert.Equal(Constants.Server20 + ".db", service.Entries[0].FileName);
        Assert.Equal(Constants.Server10 + ".db", service.Entries[1].FileName);
        Assert.Equal(Constants.Server2 + ".db", service.Entries[2].FileName);
        Assert.Equal(Constants.Server1 + ".db", service.Entries[3].FileName);
    }

    [Fact]
    public void Entries_WhenSimpleNames_ShouldSortByNameAscThenVersionDesc()
    {
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.DatabaseA + ".db");
        CreateDatabaseFile(databasePath, Constants.DatabaseB + ".db");
        CreateDatabaseFile(databasePath, Constants.DatabaseC + ".db");

        var service = CreateDatabaseService();

        Assert.Equal(3, service.Entries.Count);
        Assert.Equal(Constants.DatabaseC + ".db", service.Entries[0].FileName);
        Assert.Equal(Constants.DatabaseB + ".db", service.Entries[1].FileName);
        Assert.Equal(Constants.DatabaseA + ".db", service.Entries[2].FileName);
    }

    [Fact]
    public async Task EnumerateZipDbEntryNamesAsync_MalformedZip_ShouldReturnEmpty_NotThrow()
    {
        CreateDatabaseDirectory();
        var sourceDir = Path.Combine(_testDirectory, "source");
        Directory.CreateDirectory(sourceDir);

        var malformedZip = Path.Combine(sourceDir, "malformed.zip");
        File.WriteAllBytes(malformedZip, [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07]);

        var service = CreateDatabaseService();

        var names = await service.EnumerateZipDbEntryNamesAsync(
            malformedZip,
            TestContext.Current.CancellationToken);

        Assert.Empty(names);
    }

    [Fact]
    public async Task EnumerateZipDbEntryNamesAsync_ShouldReturnDbEntries_NotOtherFileTypes()
    {
        CreateDatabaseDirectory();
        var sourceDir = Path.Combine(_testDirectory, "source");
        Directory.CreateDirectory(sourceDir);

        var zipPath = Path.Combine(sourceDir, "import.zip");

        CreateZipWithEntries(zipPath,
        [
            (Constants.TestDb1, "db1"),
            ("readme.txt", "ignored"),
            (Constants.TestDb2, "db2")
        ]);

        var service = CreateDatabaseService();

        var names = await service.EnumerateZipDbEntryNamesAsync(zipPath, TestContext.Current.CancellationToken);

        Assert.Equal(2, names.Count);
        Assert.Contains(Constants.TestDb1, names);
        Assert.Contains(Constants.TestDb2, names);
        Assert.DoesNotContain("readme.txt", names);
    }

    [Fact]
    public async Task ImportAsync_FreshlyImportedV3Db_ShouldAutoUpgradeToReady_AndStayDisabled()
    {
        CreateDatabaseDirectory();
        var sourceDir = Path.Combine(_testDirectory, "source");
        Directory.CreateDirectory(sourceDir);

        var sourceFile = Path.Combine(sourceDir, Constants.TestDb1);
        DatabaseSeedUtils.SeedV3Schema(sourceFile);

        var preferences = Substitute.For<IDatabasePreferencesProvider>();
        preferences.DisabledDatabasesPreference.Returns([]);

        var service = CreateDatabaseService(preferences);

        var result = await service.ImportAsync([sourceFile], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Failures);
        Assert.Empty(result.UpgradeFailures);

        var entry = Assert.Single(service.Entries);
        Assert.Equal(Constants.TestDb1, entry.FileName);
        Assert.Equal(DatabaseStatus.Ready, entry.Status);
        Assert.False(entry.IsEnabled);
        Assert.False(entry.BackupExists);
    }

    [Fact]
    public async Task ImportAsync_FreshlyImportedV3Db_WithStaleBackupAtDestination_ShouldPopulateUpgradeFailures()
    {
        var databasePath = CreateDatabaseDirectory();

        File.WriteAllText(Path.Combine(databasePath, Constants.TestDb1 + ".upgrade.bak"), "stale-backup");

        var sourceDir = Path.Combine(_testDirectory, "source");
        Directory.CreateDirectory(sourceDir);

        var sourceFile = Path.Combine(sourceDir, Constants.TestDb1);
        DatabaseSeedUtils.SeedV3Schema(sourceFile);

        var service = CreateDatabaseService();

        var result = await service.ImportAsync([sourceFile], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Failures);

        var failure = Assert.Single(result.UpgradeFailures);
        Assert.Equal(Constants.TestDb1, failure.FileName);
        Assert.Contains("Recovery required", failure.Reason, StringComparison.OrdinalIgnoreCase);

        var entry = Assert.Single(service.Entries);
        Assert.Equal(DatabaseStatus.UpgradeRequired, entry.Status);
        Assert.True(entry.BackupExists);
    }

    [Fact]
    public async Task ImportAsync_FreshlyImportedV4Db_ShouldDefaultDisabled_AndNotEnqueueUpgradeBatch()
    {
        CreateDatabaseDirectory();
        var sourceDir = Path.Combine(_testDirectory, "source");
        Directory.CreateDirectory(sourceDir);

        var sourceFile = Path.Combine(sourceDir, Constants.TestDb1);
        DatabaseSeedUtils.SeedV4Schema(sourceFile);

        var preferences = Substitute.For<IDatabasePreferencesProvider>();
        preferences.DisabledDatabasesPreference.Returns([]);

        var service = CreateDatabaseService(preferences);

        var batchStartedCount = 0;
        service.UpgradeBatchStarted += (_, _) => Interlocked.Increment(ref batchStartedCount);

        var result = await service.ImportAsync([sourceFile], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Failures);
        Assert.Empty(result.UpgradeFailures);
        Assert.Equal(0, batchStartedCount);

        var entry = Assert.Single(service.Entries);
        Assert.Equal(Constants.TestDb1, entry.FileName);
        Assert.False(entry.IsEnabled);
        Assert.Equal(DatabaseStatus.Ready, entry.Status);

        preferences.Received().DisabledDatabasesPreference =
            Arg.Is<List<string>>(disabled => disabled != null && disabled.Contains(Constants.TestDb1, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ImportAsync_ReimportedDb_NotOnSkipList_ShouldOverwriteAndPreserveEnabledState()
    {
        var databasePath = CreateDatabaseDirectory();
        var existingPath = Path.Combine(databasePath, Constants.TestDb1);
        DatabaseSeedUtils.SeedV4Schema(existingPath);

        var preferences = Substitute.For<IDatabasePreferencesProvider>();
        preferences.DisabledDatabasesPreference.Returns([]);

        var service = CreateDatabaseService(preferences);
        Assert.True(service.Entries[0].IsEnabled);

        var sourceDir = Path.Combine(_testDirectory, "source");
        Directory.CreateDirectory(sourceDir);
        var sourceFile = Path.Combine(sourceDir, Constants.TestDb1);

        const string overwriteContent = "fresh-overwrite-content";
        File.WriteAllText(sourceFile, overwriteContent);

        var result = await service.ImportAsync([sourceFile], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Failures);
        Assert.Empty(result.UpgradeFailures);

        SqliteConnection.ClearAllPools();
        Assert.Equal(overwriteContent, File.ReadAllText(existingPath));

        var entry = Assert.Single(service.Entries);
        Assert.True(entry.IsEnabled);

        preferences.DidNotReceive().DisabledDatabasesPreference =
            Arg.Is<List<string>>(disabled => disabled != null && disabled.Contains(Constants.TestDb1, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ImportAsync_ReimportedDb_OnSkipList_ShouldPreserveExistingFileAndEnabledState()
    {
        var databasePath = CreateDatabaseDirectory();
        var existingPath = Path.Combine(databasePath, Constants.TestDb1);
        DatabaseSeedUtils.SeedV4Schema(existingPath);
        var existingLength = new FileInfo(existingPath).Length;

        var preferences = Substitute.For<IDatabasePreferencesProvider>();
        preferences.DisabledDatabasesPreference.Returns([]);

        var service = CreateDatabaseService(preferences);
        Assert.True(service.Entries[0].IsEnabled);

        var sourceDir = Path.Combine(_testDirectory, "source");
        Directory.CreateDirectory(sourceDir);
        var sourceFile = Path.Combine(sourceDir, Constants.TestDb1);
        File.WriteAllText(sourceFile, "would-overwrite-if-not-skipped");

        var skipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Constants.TestDb1 };

        var result = await service.ImportAsync([sourceFile], skipNames, TestContext.Current.CancellationToken);

        Assert.Equal(0, result.Imported);
        Assert.Empty(result.Failures);
        Assert.Empty(result.UpgradeFailures);

        SqliteConnection.ClearAllPools();
        Assert.Equal(existingLength, new FileInfo(existingPath).Length);

        var entry = Assert.Single(service.Entries);
        Assert.True(entry.IsEnabled);
        Assert.Equal(DatabaseStatus.Ready, entry.Status);

        preferences.DidNotReceive().DisabledDatabasesPreference =
            Arg.Is<List<string>>(disabled => disabled != null && disabled.Contains(Constants.TestDb1, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ImportAsync_ReimportedV3DbOverV4_ShouldAutoUpgradeAndPreservePriorEnabledState()
    {
        var databasePath = CreateDatabaseDirectory();
        var existingPath = Path.Combine(databasePath, Constants.TestDb1);
        DatabaseSeedUtils.SeedV4Schema(existingPath);

        var preferences = Substitute.For<IDatabasePreferencesProvider>();
        preferences.DisabledDatabasesPreference.Returns([]);

        var service = CreateDatabaseService(preferences);
        Assert.True(service.Entries[0].IsEnabled);

        var sourceDir = Path.Combine(_testDirectory, "source");
        Directory.CreateDirectory(sourceDir);
        var sourceFile = Path.Combine(sourceDir, Constants.TestDb1);
        DatabaseSeedUtils.SeedV3Schema(sourceFile);

        var result = await service.ImportAsync([sourceFile], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Failures);
        Assert.Empty(result.UpgradeFailures);

        var entry = Assert.Single(service.Entries);
        Assert.True(entry.IsEnabled);
        Assert.Equal(DatabaseStatus.Ready, entry.Status);
    }

    [Fact]
    public async Task ImportAsync_SkipFileNamesProvidedAsCaseSensitiveSet_ShouldStillSkipCaseInsensitively()
    {
        var databasePath = CreateDatabaseDirectory();
        var existingPath = Path.Combine(databasePath, Constants.TestDb1);
        DatabaseSeedUtils.SeedV4Schema(existingPath);
        var existingLength = new FileInfo(existingPath).Length;

        var preferences = Substitute.For<IDatabasePreferencesProvider>();
        preferences.DisabledDatabasesPreference.Returns([]);

        var service = CreateDatabaseService(preferences);

        var sourceDir = Path.Combine(_testDirectory, "source");
        Directory.CreateDirectory(sourceDir);
        var sourceFile = Path.Combine(sourceDir, Constants.TestDb1);
        File.WriteAllText(sourceFile, "would-overwrite-if-comparer-mismatched");

        var caseSensitiveSkip = new HashSet<string>(StringComparer.Ordinal)
        {
            Constants.TestDb1.ToUpperInvariant()
        };

        var result = await service.ImportAsync(
            [sourceFile],
            caseSensitiveSkip,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, result.Imported);
        Assert.Empty(result.Failures);
        Assert.Empty(result.UpgradeFailures);

        SqliteConnection.ClearAllPools();
        Assert.Equal(existingLength, new FileInfo(existingPath).Length);
    }

    [Fact]
    public async Task ImportAsync_SkipNamesIncludesZipEntry_ShouldNotExtractThatEntry_OthersExtracted()
    {
        var databasePath = CreateDatabaseDirectory();

        var preExistingPath = Path.Combine(databasePath, Constants.TestDb1);
        DatabaseSeedUtils.SeedV4Schema(preExistingPath);
        var preExistingLength = new FileInfo(preExistingPath).Length;

        var sourceDir = Path.Combine(_testDirectory, "source");
        Directory.CreateDirectory(sourceDir);
        var zipPath = Path.Combine(sourceDir, "import.zip");

        CreateZipWithEntries(zipPath,
        [
            (Constants.TestDb1, "would-overwrite-if-not-skipped"),
            (Constants.TestDb2, "fresh content")
        ]);

        var service = CreateDatabaseService();

        var skipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Constants.TestDb1 };

        var result = await service.ImportAsync([zipPath], skipNames, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Failures);

        SqliteConnection.ClearAllPools();
        Assert.Equal(preExistingLength, new FileInfo(preExistingPath).Length);
        Assert.True(File.Exists(Path.Combine(databasePath, Constants.TestDb2)));
        Assert.Equal(2, service.Entries.Count);
    }

    [Fact]
    public async Task ImportAsync_WhenDbFilesProvided_ShouldCopyAndRefresh()
    {
        CreateDatabaseDirectory();
        var sourceDir = Path.Combine(_testDirectory, "source");
        Directory.CreateDirectory(sourceDir);

        var sourceFile = Path.Combine(sourceDir, Constants.TestDb1);
        File.WriteAllText(sourceFile, "test content");

        var service = CreateDatabaseService();

        var result = await service.ImportAsync([sourceFile], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Failures);
        Assert.Single(service.Entries);
        Assert.Equal(Constants.TestDb1, service.Entries[0].FileName);
    }

    [Fact]
    public async Task ImportAsync_WhenMixedSuccessAndFailure_ShouldReturnPartialResult()
    {
        var databasePath = CreateDatabaseDirectory();
        var sourceDir = Path.Combine(_testDirectory, "source");
        Directory.CreateDirectory(sourceDir);

        var goodZip = Path.Combine(sourceDir, "good.zip");
        CreateZipWithEntries(goodZip, [(Constants.TestDb1, "good content")]);

        var malformedZip = Path.Combine(sourceDir, "bad.zip");
        File.WriteAllBytes(malformedZip, [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07]);

        var service = CreateDatabaseService();

        var result = await service.ImportAsync([goodZip, malformedZip], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Imported);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("bad.zip", failure.FileName);
        Assert.True(File.Exists(Path.Combine(databasePath, Constants.TestDb1)));
        Assert.Single(service.Entries);
    }

    [Fact]
    public async Task ImportAsync_WhenNoFilesProvided_ShouldReturnZeroAndNotRefresh()
    {
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var service = CreateDatabaseService();
        var raisedCount = 0;
        service.EntriesChanged += (_, _) => raisedCount++;

        var result = await service.ImportAsync([], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.Imported);
        Assert.Empty(result.Failures);
        Assert.Equal(0, raisedCount);
    }

    [Fact]
    public async Task ImportAsync_WhenTokenAlreadyCanceled_ShouldThrowOperationCanceledException()
    {
        CreateDatabaseDirectory();
        var sourceDir = Path.Combine(_testDirectory, "source");
        Directory.CreateDirectory(sourceDir);
        var sourcePath = Path.Combine(sourceDir, Constants.TestDb1);
        File.WriteAllText(sourcePath, "test content");

        var service = CreateDatabaseService();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ImportAsync([sourcePath], cts.Token));
    }

    [Fact]
    public async Task ImportAsync_WhenZipContainsNonDbFiles_ShouldExtractOnlyDbEntries()
    {
        var databasePath = CreateDatabaseDirectory();
        var sourceDir = Path.Combine(_testDirectory, "source");
        Directory.CreateDirectory(sourceDir);

        var zipPath = Path.Combine(sourceDir, "import.zip");
        CreateZipWithEntries(zipPath, [(Constants.TestDb1, "db content"), ("readme.txt", "ignored")]);

        var service = CreateDatabaseService();

        var result = await service.ImportAsync([zipPath], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Failures);
        Assert.True(File.Exists(Path.Combine(databasePath, Constants.TestDb1)));
        Assert.False(File.Exists(Path.Combine(databasePath, "readme.txt")));
    }

    [Fact]
    public async Task ImportAsync_WhenZipContainsValidDatabases_ShouldExtractDbFiles()
    {
        var databasePath = CreateDatabaseDirectory();
        var sourceDir = Path.Combine(_testDirectory, "source");
        Directory.CreateDirectory(sourceDir);

        var zipPath = Path.Combine(sourceDir, "import.zip");
        CreateZipWithEntries(zipPath, [(Constants.TestDb1, "db1 content"), (Constants.TestDb2, "db2 content")]);

        var service = CreateDatabaseService();

        var result = await service.ImportAsync([zipPath], TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Imported);
        Assert.Empty(result.Failures);
        Assert.True(File.Exists(Path.Combine(databasePath, Constants.TestDb1)));
        Assert.True(File.Exists(Path.Combine(databasePath, Constants.TestDb2)));
        Assert.False(File.Exists(Path.Combine(databasePath, "import.zip")));
    }

    [Fact]
    public async Task ImportAsync_WhenZipIsMalformed_ShouldReturnFailureAndNotLeakFiles()
    {
        var databasePath = CreateDatabaseDirectory();
        var sourceDir = Path.Combine(_testDirectory, "source");
        Directory.CreateDirectory(sourceDir);

        var malformedZip = Path.Combine(sourceDir, "malformed.zip");
        File.WriteAllBytes(malformedZip, [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07]);

        var service = CreateDatabaseService();

        var result = await service.ImportAsync([malformedZip], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.Imported);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("malformed.zip", failure.FileName);
        Assert.Contains("Could not open archive", failure.Reason, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(databasePath));
    }

    [Fact]
    public async Task InitialClassificationTask_NeverFaults_EvenWhenAllEntriesThrow()
    {
        var databasePath = CreateDatabaseDirectory();
        var db1Path = Path.Combine(databasePath, Constants.TestDb1);
        var db2Path = Path.Combine(databasePath, Constants.TestDb2);
        DatabaseSeedUtils.SeedV3Schema(db1Path);
        DatabaseSeedUtils.SeedV3Schema(db2Path);

        // wrapper must keep the exposed task in RanToCompletion.
        using var handle1 = new FileStream(db1Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var handle2 = new FileStream(db2Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var service = CreateDatabaseService();

        await service.InitialClassificationTask;

        Assert.Equal(TaskStatus.RanToCompletion, service.InitialClassificationTask.Status);
        Assert.Equal(2, service.Entries.Count);
        Assert.All(service.Entries, entry => Assert.Equal(DatabaseStatus.ClassificationFailed, entry.Status));
    }

    [Fact]
    public async Task InitialClassificationTask_WhenAllSchemasValid_CompletesSuccessfullyAndPopulatesStatuses()
    {
        var databasePath = CreateDatabaseDirectory();
        DatabaseSeedUtils.SeedV4Schema(Path.Combine(databasePath, Constants.TestDb1));
        DatabaseSeedUtils.SeedV3Schema(Path.Combine(databasePath, Constants.TestDb2));

        var service = CreateDatabaseService();

        await service.InitialClassificationTask;

        Assert.Equal(TaskStatus.RanToCompletion, service.InitialClassificationTask.Status);
        var v4 = service.Entries.Single(entry => entry.FileName == Constants.TestDb1);
        var v3 = service.Entries.Single(entry => entry.FileName == Constants.TestDb2);
        Assert.Equal(DatabaseStatus.Ready, v4.Status);
        Assert.Equal(DatabaseStatus.UpgradeRequired, v3.Status);
    }

    [Fact]
    public async Task InitialClassificationTask_WhenLoggerThrowsOnWarn_StillCompletesAndAppliesStatuses()
    {
        var databasePath = CreateDatabaseDirectory();
        var db1Path = Path.Combine(databasePath, Constants.TestDb1);
        var db2Path = Path.Combine(databasePath, Constants.TestDb2);
        DatabaseSeedUtils.SeedV3Schema(db1Path);
        DatabaseSeedUtils.SeedV3Schema(db2Path);

        using var handle1 = new FileStream(db1Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var handle2 = new FileStream(db2Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        // Logger throws on every Warn — simulates debug.log being locked. Without SafeLog the
        // per-entry catch would propagate, faulting the worker before statuses are applied.
        var throwingLogger = Substitute.For<ITraceLogger>();

        throwingLogger.When(logger => logger.Warning(Arg.Any<WarningLogHandler>()))
            .Do(_ => throw new IOException("simulated log file lock"));

        var service = CreateDatabaseService(traceLogger: throwingLogger);

        await service.InitialClassificationTask;

        Assert.Equal(TaskStatus.RanToCompletion, service.InitialClassificationTask.Status);
        Assert.Equal(2, service.Entries.Count);
        Assert.All(service.Entries, entry => Assert.Equal(DatabaseStatus.ClassificationFailed, entry.Status));
    }

    [Fact]
    public async Task InitialClassificationTask_WhenSubscriberAndLoggerBothThrow_StillCompletes()
    {
        var databasePath = CreateDatabaseDirectory();
        var dbPath = Path.Combine(databasePath, Constants.TestDb1);
        DatabaseSeedUtils.SeedV3Schema(dbPath);

        using var handle = new FileStream(dbPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        using var subscriberAttached = new ManualResetEventSlim(false);

        var throwingLogger = Substitute.For<ITraceLogger>();

        throwingLogger.When(logger => logger.Warning(Arg.Any<WarningLogHandler>()))
            .Do(_ =>
            {
                // safety valve for slow CI — normal completion is sub-millisecond.
                subscriberAttached.Wait(s_testTimeout);
                throw new IOException("simulated log file lock");
            });

        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var prefs = Substitute.For<IDatabasePreferencesProvider>();
        prefs.DisabledDatabasesPreference.Returns([]);
        var maintenance = _maintenance;
        var entryStore = new DatabaseRegistry(fileLocationOptions, prefs, throwingLogger);
        entryStore.Refresh();

        var classification =
            new DatabaseClassificationService(entryStore, fileLocationOptions, maintenance, throwingLogger);

        var upgrade = new DatabaseUpgradeService(entryStore,
            classification.InitialClassificationTask,
            maintenance,
            throwingLogger);

        var import =
            new DatabaseImportService(entryStore, classification, upgrade, fileLocationOptions, throwingLogger);

        var recovery = new DatabaseRecoveryService(entryStore,
            classification,
            fileLocationOptions,
            maintenance,
            throwingLogger);

        var service = new DatabaseService(entryStore, classification, upgrade, import, recovery);
        Assert.Single(service.Entries);
        service.EntriesChanged += (_, _) => throw new InvalidOperationException("subscriber fault");
        subscriberAttached.Set();

        await service.InitialClassificationTask;

        Assert.Equal(TaskStatus.RanToCompletion, service.InitialClassificationTask.Status);
        // Per-entry SafeLog (ClassificationFailed) plus wrapper SafeLog (subscriber fault) — both
        // fired and both throws were swallowed for the task to RanToCompletion.
        throwingLogger.Received(2).Warning(Arg.Any<WarningLogHandler>());
    }

    [Fact]
    public void MarkStatus_ShouldBePreservedAcrossRefresh()
    {
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var service = CreateDatabaseService();
        service.MarkStatus(Constants.TestDb1, DatabaseStatus.UpgradeFailed);

        service.Refresh();

        Assert.Equal(DatabaseStatus.UpgradeFailed, service.Entries[0].Status);
    }

    [Fact]
    public void MarkStatus_WhenStatusChanges_ShouldUpdateAndRaiseEntriesChanged()
    {
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var service = CreateDatabaseService();
        var raisedCount = 0;
        service.EntriesChanged += (_, _) => raisedCount++;

        service.MarkStatus(Constants.TestDb1, DatabaseStatus.UpgradeFailed);

        Assert.Equal(DatabaseStatus.UpgradeFailed, service.Entries[0].Status);
        Assert.Equal(1, raisedCount);
    }

    [Fact]
    public void MarkStatus_WhenStatusUnchanged_ShouldNotRaiseEntriesChanged()
    {
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var service = CreateDatabaseService();
        service.MarkStatus(Constants.TestDb1, DatabaseStatus.Ready);

        var raisedCount = 0;
        service.EntriesChanged += (_, _) => raisedCount++;

        service.MarkStatus(Constants.TestDb1, DatabaseStatus.Ready);

        Assert.Equal(0, raisedCount);
    }

    [Fact]
    public async Task QueuedBatchCount_ShouldReflectQueuedBatchesNotCountingInFlight()
    {
        var databasePath = CreateDatabaseDirectory();
        DatabaseSeedUtils.SeedV3Schema(Path.Combine(databasePath, Constants.TestDb1));
        DatabaseSeedUtils.SeedV3Schema(Path.Combine(databasePath, Constants.TestDb2));
        DatabaseSeedUtils.SeedV3Schema(Path.Combine(databasePath, Constants.TestDb3));

        var service = CreateDatabaseService();

        using var inFlight = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        service.UpgradeBatchProgress += (_, args) =>
        {
            if (args.Phase == UpgradePhase.BackingUp &&
                string.Equals(args.FileName, Constants.TestDb1, StringComparison.OrdinalIgnoreCase))
            {
                inFlight.Set();
                release.Wait(s_testTimeout);
            }
        };

        var firstBatch = service.UpgradeBatchAsync(
            [Constants.TestDb1],
            UpgradeProgressScope.Background,
            TestContext.Current.CancellationToken);

        Assert.True(inFlight.Wait(s_testTimeout, TestContext.Current.CancellationToken));
        Assert.Equal(0, service.QueuedBatchCount);

        var secondBatch = service.UpgradeBatchAsync(
            [Constants.TestDb2],
            UpgradeProgressScope.Background,
            TestContext.Current.CancellationToken);

        var thirdBatch = service.UpgradeBatchAsync(
            [Constants.TestDb3],
            UpgradeProgressScope.Background,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, service.QueuedBatchCount);

        release.Set();
        await Task.WhenAll(firstBatch, secondBatch, thirdBatch);

        Assert.Equal(0, service.QueuedBatchCount);
    }

    [Fact]
    public async Task Refresh_AfterClassificationSetsBackupExistsTrue_ShouldPreserveBackupExists()
    {
        var databasePath = CreateDatabaseDirectory();
        var dbPath = Path.Combine(databasePath, Constants.TestDb1);
        DatabaseSeedUtils.SeedV3Schema(dbPath);
        File.WriteAllText(dbPath + DatabaseService.UpgradeBackupSuffix, "interrupted-upgrade-backup");

        var service = CreateDatabaseService();

        var beforeRefresh = Assert.Single(service.Entries);
        Assert.Equal(DatabaseStatus.UpgradeRequired, beforeRefresh.Status);
        Assert.True(beforeRefresh.BackupExists);

        service.Refresh();

        var afterRefresh = Assert.Single(service.Entries);
        Assert.Equal(DatabaseStatus.UpgradeRequired, afterRefresh.Status);
        Assert.True(afterRefresh.BackupExists);
    }

    [Fact]
    public void Refresh_WhenCalled_ShouldRaiseEntriesChanged()
    {
        CreateDatabaseDirectory();
        var service = CreateDatabaseService();
        var raised = false;
        service.EntriesChanged += (_, _) => raised = true;

        service.Refresh();

        Assert.True(raised);
    }

    [Fact]
    public void Refresh_WhenNewFilesAppear_ShouldPickThemUp()
    {
        var databasePath = CreateDatabaseDirectory();
        var service = CreateDatabaseService();
        Assert.Empty(service.Entries);

        CreateDatabaseFile(databasePath, Constants.TestDb1);

        service.Refresh();

        Assert.Single(service.Entries);
        Assert.Equal(Constants.TestDb1, service.Entries[0].FileName);
    }

    [Fact]
    public async Task RemoveAsync_DoesNotTouchUserCreatedDotBakFiles()
    {
        var databasePath = CreateDatabaseDirectory();
        var dbPath = Path.Combine(databasePath, Constants.TestDb1);
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        const string userBackupContent = "user-created-content";
        var userBakPath = dbPath + ".bak";
        File.WriteAllText(userBakPath, userBackupContent);

        var service = CreateDatabaseService();

        await service.RemoveAsync(Constants.TestDb1, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(File.Exists(dbPath));
        Assert.True(File.Exists(userBakPath));
        Assert.Equal(userBackupContent, File.ReadAllText(userBakPath));
    }

    [Fact]
    public async Task RemoveAsync_DuringInFlightUpgrade_ShouldThrowInvalidOperationException()
    {
        var databasePath = CreateDatabaseDirectory();
        DatabaseSeedUtils.SeedV3Schema(Path.Combine(databasePath, Constants.TestDb1));

        var service = CreateDatabaseService();

        using var inFlight = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        service.UpgradeBatchProgress += (_, args) =>
        {
            if (args.Phase == UpgradePhase.BackingUp)
            {
                inFlight.Set();
                release.Wait(s_testTimeout);
            }
        };

        var batchTask = service.UpgradeBatchAsync(
            [Constants.TestDb1],
            UpgradeProgressScope.Background,
            TestContext.Current.CancellationToken);

        Assert.True(inFlight.Wait(s_testTimeout, TestContext.Current.CancellationToken));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RemoveAsync(Constants.TestDb1, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("another operation is in progress", ex.Message, StringComparison.OrdinalIgnoreCase);

        release.Set();
        await batchTask;
    }

    [Fact]
    public async Task RemoveAsync_WhenAlreadyCancelledCallerCt_ThrowsOperationCanceled_BeforeAnyPhase()
    {
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);
        var service = CreateDatabaseService();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var dbPath = Path.Combine(databasePath, Constants.TestDb1);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.RemoveAsync(Constants.TestDb1, cancellationToken: cts.Token));

        Assert.True(File.Exists(dbPath));
        Assert.Single(service.Entries);
    }

    [Fact]
    public async Task RemoveAsync_WhenCalled_ShouldDeleteDatabaseAndSidecars()
    {
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);
        File.WriteAllText(Path.Combine(databasePath, $"{Constants.TestDb1}-wal"), "");
        File.WriteAllText(Path.Combine(databasePath, $"{Constants.TestDb1}-shm"), "");

        var service = CreateDatabaseService();

        await service.RemoveAsync(Constants.TestDb1, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(File.Exists(Path.Combine(databasePath, Constants.TestDb1)));
        Assert.False(File.Exists(Path.Combine(databasePath, $"{Constants.TestDb1}-wal")));
        Assert.False(File.Exists(Path.Combine(databasePath, $"{Constants.TestDb1}-shm")));
        Assert.Empty(service.Entries);
    }

    [Fact]
    public async Task RemoveAsync_WhenCalled_ShouldRaiseEntriesChanged()
    {
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var service = CreateDatabaseService();
        var raised = false;
        service.EntriesChanged += (_, _) => raised = true;

        await service.RemoveAsync(Constants.TestDb1, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(raised);
    }

    [Fact]
    public async Task RemoveAsync_WhenEntryDisabled_StillInvokesPrepareCallback()
    {
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var prefs = Substitute.For<IDatabasePreferencesProvider>();
        prefs.DisabledDatabasesPreference.Returns([Constants.TestDb1]);
        var service = CreateDatabaseService(prefs);

        var disabledEntry = Assert.Single(service.Entries);
        Assert.False(disabledEntry.IsEnabled);

        var prepareInvoked = false;

        Task PrepareCallback(CancellationToken ct)
        {
            prepareInvoked = true;
            return Task.CompletedTask;
        }

        await service.RemoveAsync(
            Constants.TestDb1,
            PrepareCallback,
            TestContext.Current.CancellationToken);

        Assert.True(prepareInvoked);
        Assert.Empty(service.Entries);
    }

    [Fact]
    public async Task RemoveAsync_WhenEntryEnabled_AwaitsPrepareCallback_AfterDisable_BeforeFileDelete()
    {
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var prefs = Substitute.For<IDatabasePreferencesProvider>();
        prefs.DisabledDatabasesPreference.Returns([]);
        var service = CreateDatabaseService(prefs);

        var enabledEntry = Assert.Single(service.Entries);
        Assert.True(enabledEntry.IsEnabled);

        var observations = new List<(string Phase, bool IsEnabled, bool FileExists)>();
        var dbPath = Path.Combine(databasePath, Constants.TestDb1);

        Task PrepareCallback(CancellationToken ct)
        {
            var current = service.Entries.SingleOrDefault(e =>
                string.Equals(e.FileName, Constants.TestDb1, StringComparison.OrdinalIgnoreCase));

            observations.Add(("prepare", current?.IsEnabled ?? false, File.Exists(dbPath)));
            return Task.CompletedTask;
        }

        await service.RemoveAsync(
            Constants.TestDb1,
            PrepareCallback,
            TestContext.Current.CancellationToken);

        var observation = Assert.Single(observations);
        Assert.False(observation.IsEnabled);
        Assert.True(observation.FileExists);

        Assert.False(File.Exists(dbPath));
        Assert.Empty(service.Entries);
    }

    [Fact]
    public async Task RemoveAsync_WhenEntryEnabled_RaisesEntriesChangedTwice_OncePerPhaseMutation()
    {
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);
        var service = CreateDatabaseService();

        Assert.True(service.Entries.Single().IsEnabled);

        var raiseCount = 0;
        service.EntriesChanged += (_, _) => Interlocked.Increment(ref raiseCount);

        await service.RemoveAsync(Constants.TestDb1, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, raiseCount);
        Assert.Empty(service.Entries);
    }

    [Fact]
    public async Task RemoveAsync_WhenFileNameUnknown_ShouldThrow()
    {
        CreateDatabaseDirectory();
        var service = CreateDatabaseService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RemoveAsync("does-not-exist.db", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveAsync_WhenPrepareCallbackThrows_RestoresIsEnabled_AndRethrows()
    {
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var prefs = Substitute.For<IDatabasePreferencesProvider>();
        prefs.DisabledDatabasesPreference.Returns([]);
        var service = CreateDatabaseService(prefs);

        Assert.True(service.Entries.Single().IsEnabled);

        var dbPath = Path.Combine(databasePath, Constants.TestDb1);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveAsync(
            Constants.TestDb1,
            _ => throw new InvalidOperationException("prepare boom"),
            TestContext.Current.CancellationToken));

        Assert.Equal("prepare boom", ex.Message);
        Assert.True(File.Exists(dbPath));
        var rolledBack = Assert.Single(service.Entries);
        Assert.True(rolledBack.IsEnabled);
    }

    [Fact]
    public async Task RestoreFromBackupAsync_BackupMissing_ReturnsFalseAndDoesNotMutate()
    {
        var databasePath = CreateDatabaseDirectory();
        var dbPath = Path.Combine(databasePath, Constants.TestDb1);
        DatabaseSeedUtils.SeedV4Schema(dbPath);

        var service = CreateDatabaseService();
        var entryBefore = Assert.Single(service.Entries);

        var result = await service.RestoreFromBackupAsync(Constants.TestDb1, TestContext.Current.CancellationToken);

        Assert.False(result);
        Assert.True(File.Exists(dbPath));
        var entryAfter = Assert.Single(service.Entries);
        Assert.Equal(entryBefore.Status, entryAfter.Status);
    }

    [Fact]
    public async Task RestoreFromBackupAsync_DuringInFlightUpgrade_ShouldThrowInvalidOperationException()
    {
        var databasePath = CreateDatabaseDirectory();
        DatabaseSeedUtils.SeedV3Schema(Path.Combine(databasePath, Constants.TestDb1));

        var service = CreateDatabaseService();

        using var inFlight = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        service.UpgradeBatchProgress += (_, args) =>
        {
            if (args.Phase == UpgradePhase.BackingUp)
            {
                inFlight.Set();
                release.Wait(s_testTimeout);
            }
        };

        var batchTask = service.UpgradeBatchAsync(
            [Constants.TestDb1],
            UpgradeProgressScope.Background,
            TestContext.Current.CancellationToken);

        Assert.True(inFlight.Wait(s_testTimeout, TestContext.Current.CancellationToken));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RestoreFromBackupAsync(Constants.TestDb1, TestContext.Current.CancellationToken));

        Assert.Contains("another operation is in progress", ex.Message, StringComparison.OrdinalIgnoreCase);

        release.Set();
        await batchTask;
    }

    [Fact]
    public async Task RestoreFromBackupAsync_RaisesEntriesChangedExactlyOnce_AfterReclassification()
    {
        var databasePath = CreateDatabaseDirectory();
        var dbPath = Path.Combine(databasePath, Constants.TestDb1);

        DatabaseSeedUtils.SeedV4Schema(dbPath);

        var service = CreateDatabaseService();

        var bakPath = dbPath + DatabaseService.UpgradeBackupSuffix;
        DatabaseSeedUtils.SeedV3Schema(bakPath);

        var raisedCount = 0;
        service.EntriesChanged += (_, _) => Interlocked.Increment(ref raisedCount);

        await service.RestoreFromBackupAsync(Constants.TestDb1, TestContext.Current.CancellationToken);

        Assert.Equal(1, raisedCount);
    }

    [Fact]
    public async Task RestoreFromBackupAsync_TokenAlreadyCanceled_Throws()
    {
        var databasePath = CreateDatabaseDirectory();
        var dbPath = Path.Combine(databasePath, Constants.TestDb1);
        DatabaseSeedUtils.SeedV4Schema(dbPath);

        var service = CreateDatabaseService();

        var bakPath = dbPath + DatabaseService.UpgradeBackupSuffix;
        DatabaseSeedUtils.SeedV3Schema(bakPath);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await service.RestoreFromBackupAsync(Constants.TestDb1, cts.Token));

        Assert.True(File.Exists(bakPath));
    }

    [Fact]
    public async Task RestoreFromBackupAsync_UnknownFileName_Throws()
    {
        CreateDatabaseDirectory();
        var service = CreateDatabaseService();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.RestoreFromBackupAsync("does-not-exist.db", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task
        RestoreFromBackupAsync_WhenMainIsV4AndBackupIsV3_RestoresMainDeletesSidecarsAndBackup_StatusBecomesUpgradeRequired()
    {
        var databasePath = CreateDatabaseDirectory();
        var dbPath = Path.Combine(databasePath, Constants.TestDb1);

        DatabaseSeedUtils.SeedV4Schema(dbPath);

        var service = CreateDatabaseService();

        var bakPath = dbPath + DatabaseService.UpgradeBackupSuffix;
        DatabaseSeedUtils.SeedV3Schema(bakPath);

        var journalPath = dbPath + "-journal";
        var walPath = dbPath + "-wal";
        var shmPath = dbPath + "-shm";
        File.WriteAllText(journalPath, "stale-journal");
        File.WriteAllText(walPath, "stale-wal");
        File.WriteAllText(shmPath, "stale-shm");

        var result = await service.RestoreFromBackupAsync(Constants.TestDb1, TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.True(File.Exists(dbPath));
        Assert.False(File.Exists(journalPath));
        Assert.False(File.Exists(walPath));
        Assert.False(File.Exists(shmPath));
        Assert.False(File.Exists(bakPath));

        var entry = Assert.Single(service.Entries);
        Assert.Equal(DatabaseStatus.UpgradeRequired, entry.Status);
        Assert.False(entry.BackupExists);
    }

    [Fact]
    public async Task RestoreFromBackupAsync_WhenSidecarDeleteFails_PreservesBackupAndReturnsFalse()
    {
        var databasePath = CreateDatabaseDirectory();
        var dbPath = Path.Combine(databasePath, Constants.TestDb1);

        DatabaseSeedUtils.SeedV4Schema(dbPath);

        var service = CreateDatabaseService();

        var bakPath = dbPath + DatabaseService.UpgradeBackupSuffix;
        DatabaseSeedUtils.SeedV3Schema(bakPath);

        var walPath = dbPath + "-wal";
        File.WriteAllText(walPath, "wal-content");

        var mainBytesBefore = File.ReadAllBytes(dbPath);

        using var lockHandle = new FileStream(walPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var result = await service.RestoreFromBackupAsync(Constants.TestDb1, TestContext.Current.CancellationToken);

        Assert.False(result);
        Assert.True(File.Exists(bakPath));
        Assert.Equal(mainBytesBefore, File.ReadAllBytes(dbPath));
    }

    [Fact]
    public async Task RetryClassificationAsync_AlreadyCancelled_Throws()
    {
        var databasePath = CreateDatabaseDirectory();
        DatabaseSeedUtils.SeedV4Schema(Path.Combine(databasePath, Constants.TestDb1));

        var service = CreateDatabaseService();
        Assert.Equal(DatabaseStatus.Ready, service.Entries[0].Status);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await service.RetryClassificationAsync(Constants.TestDb1, cts.Token));

        Assert.Equal(DatabaseStatus.Ready, service.Entries[0].Status);
    }

    [Fact]
    public async Task RetryClassificationAsync_MaintenanceThrows_SetsClassificationFailed()
    {
        var databasePath = CreateDatabaseDirectory();
        var dbPath = Path.Combine(databasePath, Constants.TestDb1);
        DatabaseSeedUtils.SeedV4Schema(dbPath);

        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var preferences = Substitute.For<IDatabasePreferencesProvider>();
        preferences.DisabledDatabasesPreference.Returns([]);
        var logger = Substitute.For<ITraceLogger>();
        var failingMaintenance = Substitute.For<IProviderDatabaseMaintenance>();
        failingMaintenance.CheckSchemaState(Arg.Any<string>()).Throws(new SqliteException("boom", 1));

        var entryStore = new DatabaseRegistry(fileLocationOptions, preferences, logger);
        entryStore.Refresh();
        var classification = new DatabaseClassificationService(entryStore, fileLocationOptions, failingMaintenance, logger);

        await classification.RetryClassificationAsync(Constants.TestDb1, TestContext.Current.CancellationToken);

        var entry = entryStore.Entries.Single(e => e.FileName == Constants.TestDb1);
        Assert.Equal(DatabaseStatus.ClassificationFailed, entry.Status);
        Assert.False(entry.BackupExists);
    }

    [Fact]
    public async Task RetryClassificationAsync_MissingEntry_NoOps()
    {
        CreateDatabaseDirectory();
        var service = CreateDatabaseService();

        await service.RetryClassificationAsync("does-not-exist.db", TestContext.Current.CancellationToken);

        Assert.Empty(service.Entries);
    }

    [Fact]
    public async Task RetryClassificationAsync_Success_UpdatesEntryToReady()
    {
        var databasePath = CreateDatabaseDirectory();
        DatabaseSeedUtils.SeedV4Schema(Path.Combine(databasePath, Constants.TestDb1));

        var service = CreateDatabaseService();
        service.MarkStatus(Constants.TestDb1, DatabaseStatus.ClassificationFailed);
        Assert.Equal(DatabaseStatus.ClassificationFailed, service.Entries[0].Status);

        await service.RetryClassificationAsync(Constants.TestDb1, TestContext.Current.CancellationToken);

        Assert.Equal(DatabaseStatus.Ready, service.Entries[0].Status);
    }

    [Fact]
    public void Toggle_WhenCalled_ShouldFlipIsEnabledAndPersist()
    {
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var preferences = Substitute.For<IDatabasePreferencesProvider>();
        preferences.DisabledDatabasesPreference.Returns([]);

        var service = CreateDatabaseService(preferences);

        service.Toggle(Constants.TestDb1);

        Assert.False(service.Entries[0].IsEnabled);

        preferences.Received(1).DisabledDatabasesPreference =
            Arg.Is<IEnumerable<string>>(disabled => disabled != null && disabled.Contains(Constants.TestDb1));
    }

    [Fact]
    public void Toggle_WhenCalled_ShouldRaiseEntriesChanged()
    {
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var service = CreateDatabaseService();
        var raised = false;
        service.EntriesChanged += (_, _) => raised = true;

        service.Toggle(Constants.TestDb1);

        Assert.True(raised);
    }

    [Fact]
    public void Toggle_WhenFileNameUnknown_ShouldThrow()
    {
        CreateDatabaseDirectory();
        var service = CreateDatabaseService();

        Assert.Throws<InvalidOperationException>(() => service.Toggle("does-not-exist.db"));
    }

    [Fact]
    public async Task UpgradeBatchAsync_AfterDispose_ShouldThrowObjectDisposedException()
    {
        var databasePath = CreateDatabaseDirectory();
        DatabaseSeedUtils.SeedV3Schema(Path.Combine(databasePath, Constants.TestDb1));

        var service = CreateDatabaseService();
        await service.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.UpgradeBatchAsync(
            [Constants.TestDb1],
            UpgradeProgressScope.Background,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpgradeBatchAsync_AllEntriesRejected_ShouldShortCircuitWithoutRaisingEvents()
    {
        var databasePath = CreateDatabaseDirectory();
        DatabaseSeedUtils.SeedV4Schema(Path.Combine(databasePath, Constants.TestDb1));

        var service = CreateDatabaseService();

        Assert.Equal(DatabaseStatus.Ready, service.Entries[0].Status);

        var raisedEvents = 0;
        service.UpgradeBatchStarted += (_, _) => Interlocked.Increment(ref raisedEvents);
        service.UpgradeBatchProgress += (_, _) => Interlocked.Increment(ref raisedEvents);
        service.UpgradeBatchCompleted += (_, _) => Interlocked.Increment(ref raisedEvents);

        var result = await service.UpgradeBatchAsync(
            [Constants.TestDb1],
            UpgradeProgressScope.Background,
            TestContext.Current.CancellationToken);

        Assert.Empty(result.Succeeded);
        Assert.Empty(result.Cancelled);
        Assert.Single(result.Failed);
        Assert.Equal(Constants.TestDb1, result.Failed[0].FileName);
        Assert.Equal(0, raisedEvents);
    }

    [Fact]
    public async Task UpgradeBatchAsync_AlreadyCancelledCallerToken_ShouldShortCircuitWithoutRaisingEvents()
    {
        var databasePath = CreateDatabaseDirectory();
        DatabaseSeedUtils.SeedV3Schema(Path.Combine(databasePath, Constants.TestDb1));

        var service = CreateDatabaseService();
        var raisedEvents = 0;
        service.UpgradeBatchStarted += (_, _) => Interlocked.Increment(ref raisedEvents);
        service.UpgradeBatchProgress += (_, _) => Interlocked.Increment(ref raisedEvents);
        service.UpgradeBatchCompleted += (_, _) => Interlocked.Increment(ref raisedEvents);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await service.UpgradeBatchAsync(
            [Constants.TestDb1],
            UpgradeProgressScope.Background,
            cts.Token);

        Assert.Empty(result.Succeeded);
        Assert.Single(result.Cancelled);
        Assert.Equal(Constants.TestDb1, result.Cancelled[0]);
        Assert.Empty(result.Failed);
        Assert.Equal(0, raisedEvents);
    }

    [Fact]
    public async Task UpgradeBatchAsync_BackupAppearsAfterEnqueue_ShouldFailWithRecoveryRequired_AndNotOverwriteBackup()
    {
        var databasePath = CreateDatabaseDirectory();
        var dbPath = Path.Combine(databasePath, Constants.TestDb1);
        DatabaseSeedUtils.SeedV3Schema(dbPath);

        var service = CreateDatabaseService();

        var stalePayload = new byte[] { 0x42, 0x43 };
        File.WriteAllBytes(dbPath + ".upgrade.bak", stalePayload);

        var result = await service.UpgradeBatchAsync(
            [Constants.TestDb1],
            UpgradeProgressScope.Background,
            TestContext.Current.CancellationToken);

        Assert.Single(result.Failed);
        Assert.Contains("Recovery required", result.Failed[0].Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(stalePayload, File.ReadAllBytes(dbPath + ".upgrade.bak"));

        Assert.True(service.Entries[0].BackupExists);
    }

    [Fact]
    public async Task UpgradeBatchAsync_BackupCleanupFails_ShouldMarkReadyWithBackupAndReportFailure()
    {
        var databasePath = CreateDatabaseDirectory();
        var dbPath = Path.Combine(databasePath, Constants.TestDb1);
        DatabaseSeedUtils.SeedV3Schema(dbPath);

        var service = CreateDatabaseService();

        var backupPath = dbPath + ".upgrade.bak";
        FileStream? backupLock = null;

        service.UpgradeBatchProgress += (_, args) =>
        {
            if (args.Phase == UpgradePhase.MigratingSchema && backupLock is null)
            {
                backupLock = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.None);
            }
        };

        try
        {
            var result = await service.UpgradeBatchAsync(
                [Constants.TestDb1],
                UpgradeProgressScope.Background,
                TestContext.Current.CancellationToken);

            Assert.Empty(result.Succeeded);
            Assert.Empty(result.Cancelled);
            Assert.Single(result.Failed);
            Assert.Contains("backup cleanup failed", result.Failed[0].Message, StringComparison.OrdinalIgnoreCase);

            var entry = service.Entries[0];
            Assert.Equal(DatabaseStatus.Ready, entry.Status);
            Assert.True(entry.BackupExists);
            Assert.True(File.Exists(backupPath));
        }
        finally
        {
            backupLock?.Dispose();
        }
    }

    [Fact]
    public async Task UpgradeBatchAsync_DuplicateFileNames_ShouldDedupePreservingFirstOccurrence()
    {
        var databasePath = CreateDatabaseDirectory();
        DatabaseSeedUtils.SeedV3Schema(Path.Combine(databasePath, Constants.TestDb1));

        var service = CreateDatabaseService();

        var startedBatchSizes = new List<int>();
        service.UpgradeBatchStarted += (_, args) => startedBatchSizes.Add(args.BatchSize);

        var result = await service.UpgradeBatchAsync(
            [Constants.TestDb1, Constants.TestDb1, Constants.TestDb1],
            UpgradeProgressScope.Background,
            TestContext.Current.CancellationToken);

        Assert.Single(startedBatchSizes);
        Assert.Equal(1, startedBatchSizes[0]);
        Assert.Single(result.Succeeded);
        Assert.Equal(Constants.TestDb1, result.Succeeded[0]);
    }

    [Fact]
    public async Task UpgradeBatchAsync_DuringMigration_ShouldNotSetBackupExistsOnEntry()
    {
        var databasePath = CreateDatabaseDirectory();
        DatabaseSeedUtils.SeedV3Schema(Path.Combine(databasePath, Constants.TestDb1));

        var service = CreateDatabaseService();

        bool? backupExistsDuringMigration = null;

        service.UpgradeBatchProgress += (_, args) =>
        {
            if (args.Phase == UpgradePhase.MigratingSchema)
            {
                backupExistsDuringMigration = service.Entries[0].BackupExists;
            }
        };

        var result = await service.UpgradeBatchAsync(
            [Constants.TestDb1],
            UpgradeProgressScope.Background,
            TestContext.Current.CancellationToken);

        Assert.Single(result.Succeeded);
        Assert.NotNull(backupExistsDuringMigration);
        Assert.False(backupExistsDuringMigration.Value);
    }

    [Fact]
    public async Task UpgradeBatchAsync_EntryWithBackupExists_ShouldRejectWithRecoveryRequiredMessage()
    {
        var databasePath = CreateDatabaseDirectory();
        var dbPath = Path.Combine(databasePath, Constants.TestDb1);
        DatabaseSeedUtils.SeedV3Schema(dbPath);
        File.WriteAllText(dbPath + ".upgrade.bak", "stale-backup");

        var service = CreateDatabaseService();

        Assert.True(service.Entries[0].BackupExists);

        var result = await service.UpgradeBatchAsync(
            [Constants.TestDb1],
            UpgradeProgressScope.Background,
            TestContext.Current.CancellationToken);

        Assert.Empty(result.Succeeded);
        Assert.Empty(result.Cancelled);
        Assert.Single(result.Failed);
        Assert.Contains("Recovery required", result.Failed[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpgradeBatchAsync_HappyPath_ShouldRaiseStartedProgressCompletedInOrder_WithMatchingBatchId()
    {
        var databasePath = CreateDatabaseDirectory();
        DatabaseSeedUtils.SeedV3Schema(Path.Combine(databasePath, Constants.TestDb1));

        var service = CreateDatabaseService();

        var events = new List<(string Name, UpgradeBatchId BatchId)>();
        var eventLock = new object();

        service.UpgradeBatchStarted += (_, args) =>
        {
            lock (eventLock) { events.Add((nameof(IDatabaseService.UpgradeBatchStarted), args.BatchId)); }
        };

        service.UpgradeBatchProgress += (_, args) =>
        {
            lock (eventLock) { events.Add(($"Progress.{args.Phase}", args.BatchId)); }
        };

        service.UpgradeBatchCompleted += (_, args) =>
        {
            lock (eventLock) { events.Add((nameof(IDatabaseService.UpgradeBatchCompleted), args.BatchId)); }
        };

        await service.UpgradeBatchAsync(
            [Constants.TestDb1],
            UpgradeProgressScope.Background,
            TestContext.Current.CancellationToken);

        lock (eventLock)
        {
            Assert.Equal(5, events.Count);
            Assert.Equal(nameof(IDatabaseService.UpgradeBatchStarted), events[0].Name);
            Assert.Equal($"Progress.{UpgradePhase.BackingUp}", events[1].Name);
            Assert.Equal($"Progress.{UpgradePhase.MigratingSchema}", events[2].Name);
            Assert.Equal($"Progress.{UpgradePhase.Verifying}", events[3].Name);
            Assert.Equal(nameof(IDatabaseService.UpgradeBatchCompleted), events[4].Name);

            var batchId = events[0].BatchId;
            Assert.NotEqual(default(UpgradeBatchId), batchId);
            Assert.All(events, e => Assert.Equal(batchId, e.BatchId));
        }
    }

    [Fact]
    public async Task UpgradeBatchAsync_HappyPath_ShouldUpgradeFile_DeleteBackup_AndMarkReady()
    {
        var databasePath = CreateDatabaseDirectory();
        var dbPath = Path.Combine(databasePath, Constants.TestDb1);
        DatabaseSeedUtils.SeedV3Schema(dbPath);

        var service = CreateDatabaseService();

        Assert.Equal(DatabaseStatus.UpgradeRequired, service.Entries[0].Status);

        var result = await service.UpgradeBatchAsync(
            [Constants.TestDb1],
            UpgradeProgressScope.Background,
            TestContext.Current.CancellationToken);

        Assert.Single(result.Succeeded);
        Assert.Equal(Constants.TestDb1, result.Succeeded[0]);
        Assert.Empty(result.Cancelled);
        Assert.Empty(result.Failed);

        Assert.False(File.Exists(dbPath + ".upgrade.bak"));
        Assert.Equal(DatabaseStatus.Ready, service.Entries[0].Status);
        Assert.False(service.Entries[0].BackupExists);
    }

    [Fact]
    public async Task UpgradeBatchAsync_MultipleProgressSubscribers_FirstThrows_ShouldStillInvokeRest()
    {
        var databasePath = CreateDatabaseDirectory();
        DatabaseSeedUtils.SeedV3Schema(Path.Combine(databasePath, Constants.TestDb1));

        var service = CreateDatabaseService();

        var secondSubscriberInvocations = 0;

        service.UpgradeBatchProgress += (_, _) => throw new InvalidOperationException("first subscriber throws");
        service.UpgradeBatchProgress += (_, _) => Interlocked.Increment(ref secondSubscriberInvocations);

        var result = await service.UpgradeBatchAsync(
            [Constants.TestDb1],
            UpgradeProgressScope.Background,
            TestContext.Current.CancellationToken);

        Assert.Single(result.Succeeded);

        Assert.Equal(3, secondSubscriberInvocations);
    }

    [Fact]
    public async Task UpgradeBatchAsync_SubscriberThrows_ShouldNotBreakConsumer_AndCompleteBatch()
    {
        var databasePath = CreateDatabaseDirectory();
        DatabaseSeedUtils.SeedV3Schema(Path.Combine(databasePath, Constants.TestDb1));

        var service = CreateDatabaseService();

        service.UpgradeBatchStarted += (_, _) => throw new InvalidOperationException("subscriber-threw");
        service.UpgradeBatchProgress += (_, _) => throw new InvalidOperationException("subscriber-threw");
        service.UpgradeBatchCompleted += (_, _) => throw new InvalidOperationException("subscriber-threw");

        var result = await service.UpgradeBatchAsync(
            [Constants.TestDb1],
            UpgradeProgressScope.Background,
            TestContext.Current.CancellationToken);

        Assert.Single(result.Succeeded);
        Assert.Equal(DatabaseStatus.Ready, service.Entries[0].Status);
    }

    [Fact]
    public async Task UpgradeBatchAsync_TwoConcurrentBatches_ShouldRunSequentiallyInFifoOrder()
    {
        var databasePath = CreateDatabaseDirectory();
        DatabaseSeedUtils.SeedV3Schema(Path.Combine(databasePath, Constants.TestDb1));
        DatabaseSeedUtils.SeedV3Schema(Path.Combine(databasePath, Constants.TestDb2));

        var service = CreateDatabaseService();

        using var firstBackingUp = new ManualResetEventSlim(false);
        using var releaseFirst = new ManualResetEventSlim(false);
        var startedBatchIds = new List<UpgradeBatchId>();
        var startedTimes = new List<DateTime>();

        service.UpgradeBatchStarted += (_, args) =>
        {
            lock (startedBatchIds)
            {
                startedBatchIds.Add(args.BatchId);

                startedTimes.Add(
                    new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc).AddTicks(Environment.TickCount64));
            }
        };

        service.UpgradeBatchProgress += (_, args) =>
        {
            if (args.Phase == UpgradePhase.BackingUp &&
                string.Equals(args.FileName, Constants.TestDb1, StringComparison.OrdinalIgnoreCase))
            {
                firstBackingUp.Set();
                releaseFirst.Wait(s_testTimeout);
            }
        };

        var firstBatch = service.UpgradeBatchAsync(
            [Constants.TestDb1],
            UpgradeProgressScope.Background,
            TestContext.Current.CancellationToken);

        Assert.True(firstBackingUp.Wait(s_testTimeout, TestContext.Current.CancellationToken));

        var secondBatch = service.UpgradeBatchAsync(
            [Constants.TestDb2],
            UpgradeProgressScope.Background,
            TestContext.Current.CancellationToken);

        await Task.Delay(SecondBatchStartDelayMs, TestContext.Current.CancellationToken);

        lock (startedBatchIds)
        {
            Assert.Single(startedBatchIds);
        }

        releaseFirst.Set();

        var firstResult = await firstBatch;
        var secondResult = await secondBatch;

        Assert.Single(firstResult.Succeeded);
        Assert.Equal(Constants.TestDb1, firstResult.Succeeded[0]);
        Assert.Single(secondResult.Succeeded);
        Assert.Equal(Constants.TestDb2, secondResult.Succeeded[0]);

        lock (startedBatchIds)
        {
            Assert.Equal(2, startedBatchIds.Count);
        }
    }

    [Fact]
    public async Task UpgradeBatchAsync_UpgradeFailedEntryWithoutBackup_ShouldBeRetryable()
    {
        var databasePath = CreateDatabaseDirectory();
        var dbPath = Path.Combine(databasePath, Constants.TestDb1);
        DatabaseSeedUtils.SeedV3Schema(dbPath);

        var service = CreateDatabaseService();
        service.MarkStatus(Constants.TestDb1, DatabaseStatus.UpgradeFailed);

        var result = await service.UpgradeBatchAsync(
            [Constants.TestDb1],
            UpgradeProgressScope.Background,
            TestContext.Current.CancellationToken);

        Assert.Single(result.Succeeded);
        Assert.Equal(Constants.TestDb1, result.Succeeded[0]);
        Assert.Equal(DatabaseStatus.Ready, service.Entries[0].Status);
    }

    private static void CreateDatabaseFile(string directory, string fileName) =>
        File.WriteAllText(Path.Combine(directory, fileName), string.Empty);

    private static void CreateZipWithEntries(string zipPath, IEnumerable<(string entryName, string content)> entries)
    {
        using var fileStream = File.Create(zipPath);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create);

        foreach (var (entryName, content) in entries)
        {
            var entry = archive.CreateEntry(entryName);
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream);
            writer.Write(content);
        }
    }

    private string CreateDatabaseDirectory()
    {
        var path = Path.Join(_testDirectory, "Databases");
        Directory.CreateDirectory(path);
        return path;
    }

    private DatabaseService CreateDatabaseService(
        IDatabasePreferencesProvider? preferences = null,
        ITraceLogger? traceLogger = null)
    {
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var prefs = preferences ?? Substitute.For<IDatabasePreferencesProvider>();

        if (preferences is null)
        {
            prefs.DisabledDatabasesPreference.Returns([]);
        }

        var logger = traceLogger ?? Substitute.For<ITraceLogger>();
        var maintenance = _maintenance;
        var entryStore = new DatabaseRegistry(fileLocationOptions, prefs, logger);
        entryStore.Refresh();
        var classification = new DatabaseClassificationService(entryStore, fileLocationOptions, maintenance, logger);

        var upgrade =
            new DatabaseUpgradeService(entryStore, classification.InitialClassificationTask, maintenance, logger);

        var import = new DatabaseImportService(entryStore, classification, upgrade, fileLocationOptions, logger);

        var recovery =
            new DatabaseRecoveryService(entryStore, classification, fileLocationOptions, maintenance, logger);

        var service = new DatabaseService(entryStore, classification, upgrade, import, recovery);
        _services.Add(service);

        service.InitialClassificationTask.GetAwaiter().GetResult();
        return service;
    }
}
