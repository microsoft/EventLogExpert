// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Logging;
using EventLogExpert.UI.Common.Files;
using EventLogExpert.UI.Common.Preferences;
using EventLogExpert.UI.Database;
using EventLogExpert.UI.Database.Upgrade;
using EventLogExpert.UI.IntegrationTests.TestUtils;
using EventLogExpert.UI.IntegrationTests.TestUtils.Constants;
using Microsoft.Data.Sqlite;
using NSubstitute;
using System.IO.Compression;

namespace EventLogExpert.UI.IntegrationTests.Database;

public sealed class DatabaseServiceTests : IDisposable
{
    private readonly List<DatabaseService> _services = [];
    private readonly string _testDirectory;

    public DatabaseServiceTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"DatabaseServiceTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
    }

    [Fact]
    public void ActiveDatabases_ShouldReturnFullPathsOfEnabledReadyEntriesOnly()
    {
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);
        CreateDatabaseFile(databasePath, Constants.TestDb2);
        CreateDatabaseFile(databasePath, Constants.TestDb3);

        var preferences = Substitute.For<IPreferencesProvider>();
        preferences.DisabledDatabasesPreference.Returns([Constants.TestDb2]);

        var service = CreateDatabaseService(preferences);
        service.MarkStatus(Constants.TestDb1, DatabaseStatus.Ready);
        service.MarkStatus(Constants.TestDb2, DatabaseStatus.Ready);
        service.MarkStatus(Constants.TestDb3, DatabaseStatus.UpgradeRequired);

        // Act
        var activeDatabases = service.ActiveDatabases;

        // Assert: only TestDb1 (TestDb2 disabled, TestDb3 not ready)
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

        // Reset to NotClassified so the explicit ClassifyEntriesAsync call below produces real
        // status changes (V3 → UpgradeRequired). The CreateDatabaseService helper already drained
        // the ctor-initiated classification.
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
        // An empty file would otherwise classify as Ready (PRAGMA inspection sees no tables →
        // currentVersion=Current → no upgrade needed). EventResolver would then EnsureCreated
        // it on first read, silently rewriting it as a V4 schema. Force it into UnrecognizedSchema
        // so the user sees the bad file in Settings instead of having it overwritten invisibly.
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

        // Hold an exclusive lock on the second file so ProviderDbContext cannot open it.
        // The classification pass must mark the locked entry as ClassificationFailed so the
        // resolver pipeline cannot consume it later (a Ready status would crash IEventResolver
        // when it tried to open the same locked file).
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

        // ClassificationFailed must be excluded from ActiveDatabases so the resolver pipeline
        // never tries to open the file.
        Assert.DoesNotContain(lockedPath, service.ActiveDatabases);
    }

    [Fact]
    public async Task ClassifyEntriesAsync_WhenSqliteFileWithoutProviderDetailsTable_ShouldClassifyAsUnrecognizedSchema()
    {
        // A valid SQLite file that lacks the ProviderDetails table is not one of our schemas
        // (V1/V2/V3/V4). Without quarantine, EventResolver would later crash on
        // ProviderDetails.FirstOrDefault(...) with "no such table". Force UnrecognizedSchema so
        // ActiveDatabases excludes it before the resolver pipeline ever sees it.
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
        Assert.DoesNotContain(dbPath, service.ActiveDatabases);
    }

    [Fact]
    public async Task ClassifyEntriesAsync_WhenV3BakAppearsBetweenClassifications_ShouldUpdateBackupExistsAndRaiseEntriesChanged()
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
    public void Constructor_WhenCalled_ShouldSeedEntriesFromDisk()
    {
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);
        CreateDatabaseFile(databasePath, Constants.TestDb2);

        // Act
        var service = CreateDatabaseService();

        // Assert
        Assert.Equal(2, service.Entries.Count);
        Assert.Contains(service.Entries, entry => entry.FileName == Constants.TestDb1);
        Assert.Contains(service.Entries, entry => entry.FileName == Constants.TestDb2);
        Assert.All(service.Entries, entry => Assert.True(entry.IsEnabled));
        Assert.All(service.Entries, entry => Assert.False(entry.BackupExists));
    }

    [Fact]
    public void Constructor_WhenDatabaseDirectoryDoesNotExist_ShouldHaveEmptyEntries()
    {
        // Arrange (no directory created)
        var service = CreateDatabaseService();

        // Assert
        Assert.Empty(service.Entries);
    }

    [Fact]
    public void Constructor_WhenDisabledFilenameIsCaseDifferent_ShouldStillMarkDisabled()
    {
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var preferences = Substitute.For<IPreferencesProvider>();
        preferences.DisabledDatabasesPreference.Returns([Constants.TestDb1.ToUpper()]);

        // Act
        var service = CreateDatabaseService(preferences);

        // Assert
        Assert.Single(service.Entries);
        Assert.False(service.Entries[0].IsEnabled);
    }

    [Fact]
    public void Constructor_WhenNonDbFilesPresent_ShouldOnlyIncludeDbFiles()
    {
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);
        File.WriteAllText(Path.Combine(databasePath, "ignored.txt"), "");
        File.WriteAllText(Path.Combine(databasePath, "ignored.json"), "");

        // Act
        var service = CreateDatabaseService();

        // Assert
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

        using var inFlight = new ManualResetEventSlim(initialState: false);
        using var release = new ManualResetEventSlim(initialState: false);

        service.UpgradeBatchProgress += (_, args) =>
        {
            if (args.Phase == UpgradePhase.BackingUp)
            {
                inFlight.Set();
                release.Wait(TimeSpan.FromSeconds(10));
            }
        };

        var batchTask = service.UpgradeBatchAsync(
            [Constants.TestDb1],
            UpgradeProgressScope.Background,
            TestContext.Current.CancellationToken);

        Assert.True(inFlight.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeleteEntryWithBackupAsync(Constants.TestDb1, TestContext.Current.CancellationToken));
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

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await service.DeleteEntryWithBackupAsync(Constants.TestDb1, cts.Token));

        Assert.True(File.Exists(Path.Combine(databasePath, Constants.TestDb1)));
    }

    [Fact]
    public async Task DeleteEntryWithBackupAsync_UnknownFileName_Throws()
    {
        CreateDatabaseDirectory();
        var service = CreateDatabaseService();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.DeleteEntryWithBackupAsync("does-not-exist.db", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteEntryWithBackupAsync_WhenSidecarDeleteFails_PreservesMainAndEntryAndReturnsFalse()
    {
        var databasePath = CreateDatabaseDirectory();
        var dbPath = Path.Combine(databasePath, Constants.TestDb1);
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var walPath = dbPath + "-wal";
        File.WriteAllText(walPath, "wal-content");

        // Hold the WAL with FileShare.None so File.Delete throws IOException; this simulates the
        // single-process race where SQLite still has the sidecar mapped at delete time. The main
        // file and the entry must survive so Refresh keeps the entry visible and the user can retry.
        using var lockHandle = new FileStream(walPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var service = CreateDatabaseService();

        var result = await service.DeleteEntryWithBackupAsync(Constants.TestDb1, TestContext.Current.CancellationToken);

        Assert.False(result);
        Assert.True(File.Exists(dbPath));
        Assert.Single(service.Entries);
    }

    public void Dispose()
    {
        // Dispose all services first so the consumer task halts and any in-flight upgrade rolls
        // back BEFORE we tear down the temp directory underneath it.
        foreach (var service in _services)
        {
            try
            {
                service.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception)
            {
                // Best-effort cleanup; a hung disposal should not fail an otherwise-passing test.
            }
        }

        // SQLite connections opened during ClassifyEntriesAsync are pooled; on Windows the pool
        // keeps the file handle open after `Database.CloseConnection`, blocking the recursive
        // delete below. Drop all pools before cleanup.
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

        using var inFlight = new ManualResetEventSlim(initialState: false);
        using var release = new ManualResetEventSlim(initialState: false);

        service.UpgradeBatchProgress += (_, args) =>
        {
            if (args.Phase == UpgradePhase.BackingUp && string.Equals(args.FileName, Constants.TestDb1, StringComparison.OrdinalIgnoreCase))
            {
                inFlight.Set();
                release.Wait(TimeSpan.FromSeconds(10));
            }
        };

        var firstBatch = service.UpgradeBatchAsync(
            [Constants.TestDb1],
            UpgradeProgressScope.Background,
            TestContext.Current.CancellationToken);

        Assert.True(inFlight.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        var pendingBatch = service.UpgradeBatchAsync(
            [Constants.TestDb2],
            UpgradeProgressScope.Background,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, service.QueuedBatchCount);

        var disposeTask = service.DisposeAsync().AsTask();

        release.Set();
        await disposeTask;

        // First batch was mid-upgrade when dispose cancelled; rollback restores it and surfaces as
        // Cancelled.
        var firstResult = await firstBatch;
        Assert.Empty(firstResult.Succeeded);
        Assert.Single(firstResult.Cancelled);
        Assert.Equal(Constants.TestDb1, firstResult.Cancelled[0]);

        // Pending batch was drained by the consumer, observed cancellation per-entry, and surfaced
        // as Cancelled — strictly more useful than throwing OperationCanceledException.
        var pendingResult = await pendingBatch;
        Assert.Empty(pendingResult.Succeeded);
        Assert.Single(pendingResult.Cancelled);
        Assert.Equal(Constants.TestDb2, pendingResult.Cancelled[0]);
    }

    [Fact]
    public void Entries_WhenMixedVersionedAndNonVersioned_ShouldSortCorrectly()
    {
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.Windows10 + ".db");
        CreateDatabaseFile(databasePath, Constants.Windows11 + ".db");
        CreateDatabaseFile(databasePath, Constants.SimpleDatabase + ".db");
        CreateDatabaseFile(databasePath, Constants.AnotherDb + ".db");

        // Act
        var service = CreateDatabaseService();

        // Assert: non-versioned first, then versioned numeric desc
        Assert.Equal(4, service.Entries.Count);
        Assert.Equal(Constants.AnotherDb + ".db", service.Entries[0].FileName);
        Assert.Equal(Constants.SimpleDatabase + ".db", service.Entries[1].FileName);
        Assert.Equal(Constants.Windows11 + ".db", service.Entries[2].FileName);
        Assert.Equal(Constants.Windows10 + ".db", service.Entries[3].FileName);
    }

    [Fact]
    public void Entries_WhenNumericVersions_ShouldSortNumericallyNotLexicographically()
    {
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.Server1 + ".db");
        CreateDatabaseFile(databasePath, Constants.Server2 + ".db");
        CreateDatabaseFile(databasePath, Constants.Server10 + ".db");
        CreateDatabaseFile(databasePath, Constants.Server20 + ".db");

        // Act
        var service = CreateDatabaseService();

        // Assert: numeric desc — 20, 10, 2, 1
        Assert.Equal(Constants.Server20 + ".db", service.Entries[0].FileName);
        Assert.Equal(Constants.Server10 + ".db", service.Entries[1].FileName);
        Assert.Equal(Constants.Server2 + ".db", service.Entries[2].FileName);
        Assert.Equal(Constants.Server1 + ".db", service.Entries[3].FileName);
    }

    [Fact]
    public void Entries_WhenSimpleNames_ShouldSortByNameAscThenVersionDesc()
    {
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.DatabaseA + ".db");
        CreateDatabaseFile(databasePath, Constants.DatabaseB + ".db");
        CreateDatabaseFile(databasePath, Constants.DatabaseC + ".db");

        // Act
        var service = CreateDatabaseService();

        // Assert: "Database X" splits to "Database " + "X"; FirstPart asc then SecondPart desc
        Assert.Equal(3, service.Entries.Count);
        Assert.Equal(Constants.DatabaseC + ".db", service.Entries[0].FileName);
        Assert.Equal(Constants.DatabaseB + ".db", service.Entries[1].FileName);
        Assert.Equal(Constants.DatabaseA + ".db", service.Entries[2].FileName);
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

        // If multicast invoke aborted on the first throwing subscriber, this would be 0.
        Assert.Equal(1, secondSubscriberInvocations);
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

        var preferences = Substitute.For<IPreferencesProvider>();
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

        // Stale .upgrade.bak at destination (no main file present yet) — simulates a recovery
        // scenario where a backup is sitting on disk waiting for the next upgrade attempt.
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

        var preferences = Substitute.For<IPreferencesProvider>();
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
            Arg.Is<List<string>>(disabled => disabled.Contains(Constants.TestDb1, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ImportAsync_ReimportedDb_NotOnSkipList_ShouldOverwriteAndPreserveEnabledState()
    {
        var databasePath = CreateDatabaseDirectory();
        var existingPath = Path.Combine(databasePath, Constants.TestDb1);
        DatabaseSeedUtils.SeedV4Schema(existingPath);

        var preferences = Substitute.For<IPreferencesProvider>();
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
            Arg.Is<List<string>>(disabled => disabled.Contains(Constants.TestDb1, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ImportAsync_ReimportedDb_OnSkipList_ShouldPreserveExistingFileAndEnabledState()
    {
        var databasePath = CreateDatabaseDirectory();
        var existingPath = Path.Combine(databasePath, Constants.TestDb1);
        DatabaseSeedUtils.SeedV4Schema(existingPath);
        var existingLength = new FileInfo(existingPath).Length;

        var preferences = Substitute.For<IPreferencesProvider>();
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
            Arg.Is<List<string>>(disabled => disabled.Contains(Constants.TestDb1, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ImportAsync_ReimportedV3DbOverV4_ShouldAutoUpgradeAndPreservePriorEnabledState()
    {
        var databasePath = CreateDatabaseDirectory();
        var existingPath = Path.Combine(databasePath, Constants.TestDb1);
        DatabaseSeedUtils.SeedV4Schema(existingPath);

        var preferences = Substitute.For<IPreferencesProvider>();
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

        var preferences = Substitute.For<IPreferencesProvider>();
        preferences.DisabledDatabasesPreference.Returns([]);

        var service = CreateDatabaseService(preferences);

        var sourceDir = Path.Combine(_testDirectory, "source");
        Directory.CreateDirectory(sourceDir);
        var sourceFile = Path.Combine(sourceDir, Constants.TestDb1);
        File.WriteAllText(sourceFile, "would-overwrite-if-comparer-mismatched");

        // Caller uses an explicitly case-sensitive set with the upper-cased name. Service should
        // honor its documented case-insensitive skip contract and not overwrite the existing file.
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

        // Pre-create the entry that we'll ask the import to skip.
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
        // Arrange
        CreateDatabaseDirectory();
        var sourceDir = Path.Combine(_testDirectory, "source");
        Directory.CreateDirectory(sourceDir);

        var sourceFile = Path.Combine(sourceDir, Constants.TestDb1);
        File.WriteAllText(sourceFile, "test content");

        var service = CreateDatabaseService();

        // Act
        var result = await service.ImportAsync([sourceFile], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Failures);
        Assert.Single(service.Entries);
        Assert.Equal(Constants.TestDb1, service.Entries[0].FileName);
    }

    [Fact]
    public async Task ImportAsync_WhenMixedSuccessAndFailure_ShouldReturnPartialResult()
    {
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        var sourceDir = Path.Combine(_testDirectory, "source");
        Directory.CreateDirectory(sourceDir);

        var goodZip = Path.Combine(sourceDir, "good.zip");
        CreateZipWithEntries(goodZip, [(Constants.TestDb1, "good content")]);

        var malformedZip = Path.Combine(sourceDir, "bad.zip");
        File.WriteAllBytes(malformedZip, [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07]);

        var service = CreateDatabaseService();

        // Act
        var result = await service.ImportAsync([goodZip, malformedZip], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, result.Imported);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("bad.zip", failure.FileName);
        Assert.True(File.Exists(Path.Combine(databasePath, Constants.TestDb1)));
        Assert.Single(service.Entries);
    }

    [Fact]
    public async Task ImportAsync_WhenNoFilesProvided_ShouldReturnZeroAndNotRefresh()
    {
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var service = CreateDatabaseService();
        var raisedCount = 0;
        service.EntriesChanged += (_, _) => raisedCount++;

        // Act
        var result = await service.ImportAsync([], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, result.Imported);
        Assert.Empty(result.Failures);
        Assert.Equal(0, raisedCount);
    }

    [Fact]
    public async Task ImportAsync_WhenTokenAlreadyCanceled_ShouldThrowOperationCanceledException()
    {
        // Arrange
        CreateDatabaseDirectory();
        var sourceDir = Path.Combine(_testDirectory, "source");
        Directory.CreateDirectory(sourceDir);
        var sourcePath = Path.Combine(sourceDir, Constants.TestDb1);
        File.WriteAllText(sourcePath, "test content");

        var service = CreateDatabaseService();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act + Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ImportAsync([sourcePath], cts.Token));
    }

    [Fact]
    public async Task ImportAsync_WhenZipContainsNonDbFiles_ShouldExtractOnlyDbEntries()
    {
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        var sourceDir = Path.Combine(_testDirectory, "source");
        Directory.CreateDirectory(sourceDir);

        var zipPath = Path.Combine(sourceDir, "import.zip");
        CreateZipWithEntries(zipPath, [(Constants.TestDb1, "db content"), ("readme.txt", "ignored")]);

        var service = CreateDatabaseService();

        // Act
        var result = await service.ImportAsync([zipPath], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Failures);
        Assert.True(File.Exists(Path.Combine(databasePath, Constants.TestDb1)));
        Assert.False(File.Exists(Path.Combine(databasePath, "readme.txt")));
    }

    [Fact]
    public async Task ImportAsync_WhenZipContainsValidDatabases_ShouldExtractDbFiles()
    {
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        var sourceDir = Path.Combine(_testDirectory, "source");
        Directory.CreateDirectory(sourceDir);

        var zipPath = Path.Combine(sourceDir, "import.zip");
        CreateZipWithEntries(zipPath, [(Constants.TestDb1, "db1 content"), (Constants.TestDb2, "db2 content")]);

        var service = CreateDatabaseService();

        // Act
        var result = await service.ImportAsync([zipPath], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.Imported);
        Assert.Empty(result.Failures);
        Assert.True(File.Exists(Path.Combine(databasePath, Constants.TestDb1)));
        Assert.True(File.Exists(Path.Combine(databasePath, Constants.TestDb2)));
        Assert.False(File.Exists(Path.Combine(databasePath, "import.zip")));
    }

    [Fact]
    public async Task ImportAsync_WhenZipIsMalformed_ShouldReturnFailureAndNotLeakFiles()
    {
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        var sourceDir = Path.Combine(_testDirectory, "source");
        Directory.CreateDirectory(sourceDir);

        var malformedZip = Path.Combine(sourceDir, "malformed.zip");
        File.WriteAllBytes(malformedZip, [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07]);

        var service = CreateDatabaseService();

        // Act
        var result = await service.ImportAsync([malformedZip], TestContext.Current.CancellationToken);

        // Assert
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

        // Hold exclusive locks on every DB file so ProviderDbContext cannot open any of them.
        // The per-entry catch must turn each failure into ClassificationFailed and the outer
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

        // Force per-entry classification failures so the per-entry catch fires Warn for every entry.
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

        // Lock the DB so per-entry classification fails — that fires the per-entry SafeLog,
        // which we use as a synchronization point to attach the throwing EntriesChanged
        // subscriber BEFORE ClassifyEntriesAsync reaches RaiseEntriesChanged.
        using var handle = new FileStream(dbPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        using var subscriberAttached = new ManualResetEventSlim(false);

        var throwingLogger = Substitute.For<ITraceLogger>();
        throwingLogger.When(logger => logger.Warning(Arg.Any<WarningLogHandler>()))
            .Do(_ =>
            {
                // Worker blocks here until subscriberAttached.Set() below; ensures the
                // subscriber is wired before the wrapper-catch path can fire. 10s is a
                // safety valve for slow CI — normal completion is sub-millisecond.
                subscriberAttached.Wait(TimeSpan.FromSeconds(10));
                throw new IOException("simulated log file lock");
            });

        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var prefs = Substitute.For<IPreferencesProvider>();
        prefs.DisabledDatabasesPreference.Returns([]);
        var service = new DatabaseService(fileLocationOptions, prefs, throwingLogger);
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
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var service = CreateDatabaseService();
        service.MarkStatus(Constants.TestDb1, DatabaseStatus.UpgradeFailed);

        // Act
        service.Refresh();

        // Assert
        Assert.Equal(DatabaseStatus.UpgradeFailed, service.Entries[0].Status);
    }

    [Fact]
    public void MarkStatus_WhenStatusChanges_ShouldUpdateAndRaiseEntriesChanged()
    {
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var service = CreateDatabaseService();
        var raisedCount = 0;
        service.EntriesChanged += (_, _) => raisedCount++;

        // Act
        service.MarkStatus(Constants.TestDb1, DatabaseStatus.UpgradeFailed);

        // Assert
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

        using var inFlight = new ManualResetEventSlim(initialState: false);
        using var release = new ManualResetEventSlim(initialState: false);

        service.UpgradeBatchProgress += (_, args) =>
        {
            if (args.Phase == UpgradePhase.BackingUp && string.Equals(args.FileName, Constants.TestDb1, StringComparison.OrdinalIgnoreCase))
            {
                inFlight.Set();
                release.Wait(TimeSpan.FromSeconds(10));
            }
        };

        var firstBatch = service.UpgradeBatchAsync(
            [Constants.TestDb1],
            UpgradeProgressScope.Background,
            TestContext.Current.CancellationToken);

        Assert.True(inFlight.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
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
        // Regression: c9 refactored Refresh() to use `existing with { IsEnabled = ... }` so all
        // other fields (Status, BackupExists) survive. This test pins that invariant — if a
        // future change reverts to the positional constructor, BackupExists silently drops to
        // false and the recovery dialog stops surfacing interrupted upgrades.
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
        // Arrange
        CreateDatabaseDirectory();
        var service = CreateDatabaseService();
        var raised = false;
        service.EntriesChanged += (_, _) => raised = true;

        // Act
        service.Refresh();

        // Assert
        Assert.True(raised);
    }

    [Fact]
    public void Refresh_WhenNewFilesAppear_ShouldPickThemUp()
    {
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        var service = CreateDatabaseService();
        Assert.Empty(service.Entries);

        CreateDatabaseFile(databasePath, Constants.TestDb1);

        // Act
        service.Refresh();

        // Assert
        Assert.Single(service.Entries);
        Assert.Equal(Constants.TestDb1, service.Entries[0].FileName);
    }

    [Fact]
    public async Task RemoveAsync_DoesNotTouchUserCreatedDotBakFiles()
    {
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        var dbPath = Path.Combine(databasePath, Constants.TestDb1);
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        const string userBackupContent = "user-created-content";
        var userBakPath = dbPath + ".bak";
        File.WriteAllText(userBakPath, userBackupContent);

        var service = CreateDatabaseService();

        // Act
        await service.RemoveAsync(Constants.TestDb1, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.False(File.Exists(dbPath));
        Assert.True(File.Exists(userBakPath));
        Assert.Equal(userBackupContent, File.ReadAllText(userBakPath));
    }

    [Fact]
    public async Task RemoveAsync_DuringInFlightUpgrade_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        DatabaseSeedUtils.SeedV3Schema(Path.Combine(databasePath, Constants.TestDb1));

        var service = CreateDatabaseService();

        using var inFlight = new ManualResetEventSlim(initialState: false);
        using var release = new ManualResetEventSlim(initialState: false);

        service.UpgradeBatchProgress += (_, args) =>
        {
            if (args.Phase == UpgradePhase.BackingUp)
            {
                inFlight.Set();
                release.Wait(TimeSpan.FromSeconds(10));
            }
        };

        var batchTask = service.UpgradeBatchAsync(
            [Constants.TestDb1],
            UpgradeProgressScope.Background,
            TestContext.Current.CancellationToken);

        Assert.True(inFlight.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        // Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RemoveAsync(Constants.TestDb1, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("another operation is in progress", ex.Message, StringComparison.OrdinalIgnoreCase);

        release.Set();
        await batchTask;
    }

    [Fact]
    public async Task RemoveAsync_WhenAlreadyCancelledCallerCt_ThrowsOperationCanceled_BeforeAnyPhase()
    {
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);
        var service = CreateDatabaseService();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var dbPath = Path.Combine(databasePath, Constants.TestDb1);

        // Act + Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.RemoveAsync(Constants.TestDb1, cancellationToken: cts.Token));

        Assert.True(File.Exists(dbPath));
        Assert.Single(service.Entries);
    }

    [Fact]
    public async Task RemoveAsync_WhenCalled_ShouldDeleteDatabaseAndSidecars()
    {
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);
        File.WriteAllText(Path.Combine(databasePath, $"{Constants.TestDb1}-wal"), "");
        File.WriteAllText(Path.Combine(databasePath, $"{Constants.TestDb1}-shm"), "");

        var service = CreateDatabaseService();

        // Act
        await service.RemoveAsync(Constants.TestDb1, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.False(File.Exists(Path.Combine(databasePath, Constants.TestDb1)));
        Assert.False(File.Exists(Path.Combine(databasePath, $"{Constants.TestDb1}-wal")));
        Assert.False(File.Exists(Path.Combine(databasePath, $"{Constants.TestDb1}-shm")));
        Assert.Empty(service.Entries);
    }

    [Fact]
    public async Task RemoveAsync_WhenCalled_ShouldRaiseEntriesChanged()
    {
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var service = CreateDatabaseService();
        var raised = false;
        service.EntriesChanged += (_, _) => raised = true;

        // Act
        await service.RemoveAsync(Constants.TestDb1, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(raised);
    }

    [Fact]
    public async Task RemoveAsync_WhenEntryDisabled_StillInvokesPrepareCallback()
    {
        // Arrange — a previously-disabled entry can still be referenced by IEventResolver
        // instances constructed before the disable (the user disabled it and declined the
        // modal-close reload prompt). The coordinator must still close any open log views
        // so SqliteConnection.ClearAllPools + File.Delete don't race with in-flight scopes.
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var prefs = Substitute.For<IPreferencesProvider>();
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

        // Act
        await service.RemoveAsync(
            Constants.TestDb1,
            prepareForDeletionAsync: PrepareCallback,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(prepareInvoked);
        Assert.Empty(service.Entries);
    }

    [Fact]
    public async Task RemoveAsync_WhenEntryEnabled_AwaitsPrepareCallback_AfterDisable_BeforeFileDelete()
    {
        // Arrange — verifies the 4-phase ordering: disable → prepare-callback → file delete.
        // Track phase order via a shared list. The callback observes IsEnabled (must already
        // be false) and confirms the file still exists (Phase 3 hasn't run yet).
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var prefs = Substitute.For<IPreferencesProvider>();
        prefs.DisabledDatabasesPreference.Returns([]);
        var service = CreateDatabaseService(prefs);

        // Default IsEnabled is true when the file is not in the disabled-preference list,
        // so the entry is already enabled — no toggle needed.
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

        // Act
        await service.RemoveAsync(
            Constants.TestDb1,
            prepareForDeletionAsync: PrepareCallback,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert — prepare callback observed IsEnabled=false (Phase 1 ran first) and the
        // file still present (Phase 3 had not yet run).
        var observation = Assert.Single(observations);
        Assert.False(observation.IsEnabled);
        Assert.True(observation.FileExists);

        // After RemoveAsync returned, the file is gone and the entry is removed.
        Assert.False(File.Exists(dbPath));
        Assert.Empty(service.Entries);
    }

    [Fact]
    public async Task RemoveAsync_WhenEntryEnabled_RaisesEntriesChangedTwice_OncePerPhaseMutation()
    {
        // Arrange — Phase 1 (disable) and Phase 4 (RemoveAt) should each fire EntriesChanged.
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);
        var service = CreateDatabaseService();

        Assert.True(service.Entries.Single().IsEnabled);

        var raiseCount = 0;
        service.EntriesChanged += (_, _) => Interlocked.Increment(ref raiseCount);

        // Act
        await service.RemoveAsync(Constants.TestDb1, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — exactly two raises: one for the disable mutation, one for the remove.
        Assert.Equal(2, raiseCount);
        Assert.Empty(service.Entries);
    }

    [Fact]
    public async Task RemoveAsync_WhenFileNameUnknown_ShouldThrow()
    {
        // Arrange
        CreateDatabaseDirectory();
        var service = CreateDatabaseService();

        // Act + Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RemoveAsync("does-not-exist.db", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveAsync_WhenPrepareCallbackThrows_RestoresIsEnabled_AndRethrows()
    {
        // Arrange — Phase 2 failure must roll back Phase 1. The entry should remain in
        // Entries with IsEnabled restored to true so the caller can retry.
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var prefs = Substitute.For<IPreferencesProvider>();
        prefs.DisabledDatabasesPreference.Returns([]);
        var service = CreateDatabaseService(prefs);

        Assert.True(service.Entries.Single().IsEnabled);

        var dbPath = Path.Combine(databasePath, Constants.TestDb1);

        // Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RemoveAsync(
                Constants.TestDb1,
                prepareForDeletionAsync: _ => throw new InvalidOperationException("prepare boom"),
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
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

        using var inFlight = new ManualResetEventSlim(initialState: false);
        using var release = new ManualResetEventSlim(initialState: false);

        service.UpgradeBatchProgress += (_, args) =>
        {
            if (args.Phase == UpgradePhase.BackingUp)
            {
                inFlight.Set();
                release.Wait(TimeSpan.FromSeconds(10));
            }
        };

        var batchTask = service.UpgradeBatchAsync(
            [Constants.TestDb1],
            UpgradeProgressScope.Background,
            TestContext.Current.CancellationToken);

        Assert.True(inFlight.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RestoreFromBackupAsync(Constants.TestDb1, TestContext.Current.CancellationToken));
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

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await service.RestoreFromBackupAsync(Constants.TestDb1, cts.Token));

        Assert.True(File.Exists(bakPath));
    }

    [Fact]
    public async Task RestoreFromBackupAsync_UnknownFileName_Throws()
    {
        CreateDatabaseDirectory();
        var service = CreateDatabaseService();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.RestoreFromBackupAsync("does-not-exist.db", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RestoreFromBackupAsync_WhenMainIsV4AndBackupIsV3_RestoresMainDeletesSidecarsAndBackup_StatusBecomesUpgradeRequired()
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

        // Hold the WAL with FileShare.None so File.Delete throws IOException; this simulates the
        // single-process race where SQLite still has the sidecar mapped at restore time. Main must
        // be untouched and the backup must survive so the user can retry.
        using var lockHandle = new FileStream(walPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var result = await service.RestoreFromBackupAsync(Constants.TestDb1, TestContext.Current.CancellationToken);

        Assert.False(result);
        Assert.True(File.Exists(bakPath));
        Assert.Equal(mainBytesBefore, File.ReadAllBytes(dbPath));
    }

    [Fact]
    public void Toggle_WhenCalled_ShouldFlipIsEnabledAndPersist()
    {
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var preferences = Substitute.For<IPreferencesProvider>();
        preferences.DisabledDatabasesPreference.Returns([]);

        var service = CreateDatabaseService(preferences);

        // Act
        service.Toggle(Constants.TestDb1);

        // Assert
        Assert.False(service.Entries[0].IsEnabled);

        preferences.Received(1).DisabledDatabasesPreference =
            Arg.Is<IEnumerable<string>>(disabled => disabled.Contains(Constants.TestDb1));
    }

    [Fact]
    public void Toggle_WhenCalled_ShouldRaiseEntriesChanged()
    {
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var service = CreateDatabaseService();
        var raised = false;
        service.EntriesChanged += (_, _) => raised = true;

        // Act
        service.Toggle(Constants.TestDb1);

        // Assert
        Assert.True(raised);
    }

    [Fact]
    public void Toggle_WhenFileNameUnknown_ShouldThrow()
    {
        // Arrange
        CreateDatabaseDirectory();
        var service = CreateDatabaseService();

        // Act + Assert
        Assert.Throws<InvalidOperationException>(() => service.Toggle("does-not-exist.db"));
    }

    [Fact]
    public async Task UpgradeBatchAsync_AfterDispose_ShouldThrowObjectDisposedException()
    {
        var databasePath = CreateDatabaseDirectory();
        DatabaseSeedUtils.SeedV3Schema(Path.Combine(databasePath, Constants.TestDb1));

        var service = CreateDatabaseService();
        await service.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => service.UpgradeBatchAsync(
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

        // Caller passed validation (no .upgrade.bak at enqueue), but a stale backup appears between
        // enqueue and the per-entry TOCTOU re-check inside UpgradeAsync.
        var stalePayload = new byte[] { 0x42, 0x43 };
        File.WriteAllBytes(dbPath + ".upgrade.bak", stalePayload);

        var result = await service.UpgradeBatchAsync(
            [Constants.TestDb1],
            UpgradeProgressScope.Background,
            TestContext.Current.CancellationToken);

        Assert.Single(result.Failed);
        Assert.Contains("Recovery required", result.Failed[0].Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(stalePayload, File.ReadAllBytes(dbPath + ".upgrade.bak"));

        // Disk truth must be reflected on the entry so the recovery host prompts immediately rather
        // than waiting for the next classification pass.
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

        // After File.Copy creates the backup (during BackingUp phase), MigratingSchema fires.
        // We hold an exclusive lock on the backup so the post-success TryDeleteFile fails — exercising
        // the cleanup-failure path that previously rolled back the successful upgrade.
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

        // BackingUp + MigratingSchema + Verifying = 3 progress events per entry. If multicast invoke
        // aborted on the first throwing subscriber, this would be 0.
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

        using var firstBackingUp = new ManualResetEventSlim(initialState: false);
        using var releaseFirst = new ManualResetEventSlim(initialState: false);
        var startedBatchIds = new List<UpgradeBatchId>();
        var startedTimes = new List<DateTime>();

        service.UpgradeBatchStarted += (_, args) =>
        {
            lock (startedBatchIds)
            {
                startedBatchIds.Add(args.BatchId);
                startedTimes.Add(new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc).AddTicks(Environment.TickCount64));
            }
        };

        service.UpgradeBatchProgress += (_, args) =>
        {
            if (args.Phase == UpgradePhase.BackingUp && string.Equals(args.FileName, Constants.TestDb1, StringComparison.OrdinalIgnoreCase))
            {
                firstBackingUp.Set();
                releaseFirst.Wait(TimeSpan.FromSeconds(10));
            }
        };

        var firstBatch = service.UpgradeBatchAsync(
            [Constants.TestDb1],
            UpgradeProgressScope.Background,
            TestContext.Current.CancellationToken);

        Assert.True(firstBackingUp.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        var secondBatch = service.UpgradeBatchAsync(
            [Constants.TestDb2],
            UpgradeProgressScope.Background,
            TestContext.Current.CancellationToken);

        await Task.Delay(100, TestContext.Current.CancellationToken);

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

    private DatabaseService CreateDatabaseService(IPreferencesProvider? preferences = null, ITraceLogger? traceLogger = null)
    {
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var prefs = preferences ?? Substitute.For<IPreferencesProvider>();

        if (preferences is null)
        {
            prefs.DisabledDatabasesPreference.Returns([]);
        }

        var logger = traceLogger ?? Substitute.For<ITraceLogger>();
        var service = new DatabaseService(fileLocationOptions, prefs, logger);
        _services.Add(service);

        // Block until ctor-initiated classification finishes so tests observe a stable post-classification
        // state. Tests that need to observe pre-classification state (e.g., InitialClassificationTask
        // contract tests) construct DatabaseService directly. Safe to block synchronously here because
        // ClassifyEntriesAsync runs on Task.Run worker threads and does not capture a sync context.
        service.InitialClassificationTask.GetAwaiter().GetResult();
        return service;
    }
}
