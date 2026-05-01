// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Options;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Tests.TestUtils;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using Microsoft.Data.Sqlite;
using NSubstitute;
using System.IO.Compression;

namespace EventLogExpert.UI.Tests.Services;

public sealed class DatabaseServiceTests : IDisposable
{
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

        // Hold an exclusive lock on the second file so EventProviderDbContext cannot open it.
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

    public void Dispose()
    {
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

        // Hold exclusive locks on every DB file so EventProviderDbContext cannot open any of them.
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
        throwingLogger.When(logger => logger.Warn(Arg.Any<WarnLogHandler>()))
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
        throwingLogger.When(logger => logger.Warn(Arg.Any<WarnLogHandler>()))
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
        throwingLogger.Received(2).Warn(Arg.Any<WarnLogHandler>());
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
    public void Remove_WhenCalled_ShouldDeleteDatabaseAndSidecars()
    {
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);
        File.WriteAllText(Path.Combine(databasePath, $"{Constants.TestDb1}-wal"), "");
        File.WriteAllText(Path.Combine(databasePath, $"{Constants.TestDb1}-shm"), "");

        var service = CreateDatabaseService();

        // Act
        service.Remove(Constants.TestDb1);

        // Assert
        Assert.False(File.Exists(Path.Combine(databasePath, Constants.TestDb1)));
        Assert.False(File.Exists(Path.Combine(databasePath, $"{Constants.TestDb1}-wal")));
        Assert.False(File.Exists(Path.Combine(databasePath, $"{Constants.TestDb1}-shm")));
        Assert.Empty(service.Entries);
    }

    [Fact]
    public void Remove_WhenCalled_ShouldRaiseEntriesChanged()
    {
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var service = CreateDatabaseService();
        var raised = false;
        service.EntriesChanged += (_, _) => raised = true;

        // Act
        service.Remove(Constants.TestDb1);

        // Assert
        Assert.True(raised);
    }

    [Fact]
    public void Remove_WhenFileNameUnknown_ShouldThrow()
    {
        // Arrange
        CreateDatabaseDirectory();
        var service = CreateDatabaseService();

        // Act + Assert
        Assert.Throws<InvalidOperationException>(() => service.Remove("does-not-exist.db"));
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

        // Block until ctor-initiated classification finishes so tests observe a stable post-classification
        // state. Tests that need to observe pre-classification state (e.g., InitialClassificationTask
        // contract tests) construct DatabaseService directly. Safe to block synchronously here because
        // ClassifyEntriesAsync runs on Task.Run worker threads and does not capture a sync context.
        service.InitialClassificationTask.GetAwaiter().GetResult();
        return service;
    }
}
