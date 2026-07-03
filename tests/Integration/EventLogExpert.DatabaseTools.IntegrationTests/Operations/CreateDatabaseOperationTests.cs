// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.DatabaseTools.CreateDatabase;
using EventLogExpert.Eventing.TestUtils;
using EventLogExpert.Eventing.TestUtils.Constants;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using EventLogExpert.Provider.Database.Context;
using EventLogExpert.Provider.Database.Hashing;
using Microsoft.Data.Sqlite;
using NSubstitute;
using System.Text.RegularExpressions;

namespace EventLogExpert.DatabaseTools.IntegrationTests.Operations;

public sealed class CreateDatabaseCommandTests : IDisposable
{
    private readonly List<string> _tempDirs = [];
    private readonly List<string> _tempPaths = [];

    [Fact]
    public async Task CreateDatabase_FolderSourceWithSameContentUnderDifferentKeys_CollapsesInsteadOfColliding()
    {
        // Identical providers can arrive under different source keys; post-stamp dedup must collapse them.
        var dir = CreateTempDir();

        var unstamped = DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName);
        var stamped = DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName);
        stamped.VersionKey = VersionKeyCalculator.Compute(stamped);

        DatabaseTestUtils.CreateV4Database(Path.Combine(dir, "a.db"), unstamped);
        DatabaseTestUtils.CreateV4Database(Path.Combine(dir, "b.db"), stamped);

        var path = CreateTempPath();
        var logger = Substitute.For<ITraceLogger>();

        await new CreateDatabaseOperation(new CreateDatabaseRequest(path, dir, null, null)).ExecuteAsync(logger, null, CancellationToken.None);

        Assert.True(File.Exists(path), "Create should have produced a database (collapsed, not aborted).");

        using var verify = new ProviderDbContext(path, readOnly: true, ensureCreated: false);
        var single = Assert.Single(verify.ProviderDetails.ToList());
        Assert.Equal(Constants.FirstProviderName, single.ProviderName);
        Assert.StartsWith(VersionKeyCalculator.SchemePrefix, single.VersionKey);
        logger.DidNotReceive().Error(Arg.Any<ErrorLogHandler>());
    }

    [Fact]
    public async Task CreateDatabase_OverwriteBackupTearsOnFirstMove_PreservesOriginalDatabase()
    {
        // A failed first backup move leaves the original at target; cleanup must not delete it as a stub.
        var path = CreateTempPath();
        DatabaseTestUtils.CreateV4Database(path, DatabaseTestUtils.BuildProviderDetails("Original-Provider"));

        // Release pooled handles so only the cleanup guard protects the original from deletion.
        SqliteConnection.ClearAllPools();

        // A directory at .bak bypasses File.Exists preflight but makes the first backup move throw.
        var backupDir = path + ".bak";
        _tempDirs.Add(backupDir);
        Directory.CreateDirectory(backupDir);

        var source = CreateTempPath();
        var providers = Enumerable.Range(0, 150)
            .Select(i => DatabaseTestUtils.BuildProviderDetails($"Provider-{i:D4}"))
            .ToArray();
        DatabaseTestUtils.CreateV4Database(source, providers);

        var logger = Substitute.For<ITraceLogger>();

        var outcome = await new CreateDatabaseOperation(new CreateDatabaseRequest(
                path, source, FilterRegex: null, SkipProvidersInFile: null, Overwrite: true))
            .ExecuteAsync(logger, null, CancellationToken.None);

        Assert.Equal(DatabaseToolsOutcome.Failed, outcome);
        Assert.True(File.Exists(path), "Original database must survive a backup that tore on the first move.");

        using var verify = new ProviderDbContext(path, readOnly: true, ensureCreated: false);
        var single = Assert.Single(verify.ProviderDetails.ToList());
        Assert.Equal("Original-Provider", single.ProviderName);
    }

    [Fact]
    public async Task CreateDatabase_OverwriteCancelledAfterHeaderFlush_RestoresOriginalAndLeavesNoStaleSidecars()
    {
        // Cancellation after backup must restore the original and remove aborted WAL/SHM sidecars.
        var path = CreateTempPath();
        DatabaseTestUtils.CreateV4Database(path, DatabaseTestUtils.BuildProviderDetails("Original-Provider"));

        // Release pooled handles so the operation's backup move is not masked by the test.
        SqliteConnection.ClearAllPools();

        var source = CreateTempPath();
        var providers = Enumerable.Range(0, 150)
            .Select(i => DatabaseTestUtils.BuildProviderDetails($"Provider-{i:D4}"))
            .ToArray();
        DatabaseTestUtils.CreateV4Database(source, providers);

        var logger = Substitute.For<ITraceLogger>();
        using var cts = new CancellationTokenSource();
        var progress = new CancelWhenProcessedReaches(100, cts);

        var outcome = await new CreateDatabaseOperation(new CreateDatabaseRequest(
                path, source, FilterRegex: null, SkipProvidersInFile: null, Overwrite: true))
            .ExecuteAsync(logger, progress, cts.Token);

        Assert.Equal(DatabaseToolsOutcome.Cancelled, outcome);
        Assert.True(File.Exists(path), "Original database must be restored after a cancelled overwrite.");
        Assert.False(File.Exists(path + ".bak"), "Backup must be consumed by the restore.");
        Assert.False(File.Exists(path + "-wal"), "No stale WAL sidecar may remain.");
        Assert.False(File.Exists(path + "-shm"), "No stale SHM sidecar may remain.");
        Assert.False(File.Exists(path + "-wal.bak"));
        Assert.False(File.Exists(path + "-shm.bak"));

        using var verify = new ProviderDbContext(path, readOnly: true, ensureCreated: false);
        var single = Assert.Single(verify.ProviderDetails.ToList());
        Assert.Equal("Original-Provider", single.ProviderName);
    }

    [Fact]
    public async Task CreateDatabase_OverwriteRestoreBlocked_PreservesBackupAndLogsRecoveryGuidance()
    {
        // If restore cannot move .bak back, the backup must remain with recovery guidance logged.
        var path = CreateTempPath();
        DatabaseTestUtils.CreateV4Database(path, DatabaseTestUtils.BuildProviderDetails("Original-Provider"));

        // Release pooled handles so the operation's backup move is not blocked by the test.
        SqliteConnection.ClearAllPools();

        var source = CreateTempPath();
        var providers = Enumerable.Range(0, 150)
            .Select(i => DatabaseTestUtils.BuildProviderDetails($"Provider-{i:D4}"))
            .ToArray();
        DatabaseTestUtils.CreateV4Database(source, providers);

        var backupPath = path + ".bak";
        _tempPaths.Add(backupPath);
        var logger = Substitute.For<ITraceLogger>();
        using var cts = new CancellationTokenSource();
        FileStream? backupLock = null;
        var progress = new LockBackupThenCancel(100, cts, backupPath, stream => backupLock = stream);

        try
        {
            var outcome = await new CreateDatabaseOperation(new CreateDatabaseRequest(
                    path, source, FilterRegex: null, SkipProvidersInFile: null, Overwrite: true))
                .ExecuteAsync(logger, progress, cts.Token);

            Assert.Equal(DatabaseToolsOutcome.Cancelled, outcome);
            Assert.True(File.Exists(backupPath), "Backup must be preserved when the restore is blocked.");
            logger.Received(1).Error(Arg.Is<ErrorLogHandler>(h =>
                h.ToString().Contains("Could not restore the original database from backup") &&
                h.ToString().Contains(backupPath)));
        }
        finally
        {
            backupLock?.Dispose();
        }
    }

    [Fact]
    public async Task CreateDatabase_OverwriteWithZeroProviders_LeavesExistingDatabaseUntouched()
    {
        // Zero-provider overwrites never create a DbContext, so no backup should be taken.
        var path = CreateTempPath();
        DatabaseTestUtils.CreateV4Database(path, DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName));

        var source = CreateTempPath();
        DatabaseTestUtils.CreateV4Database(source, DatabaseTestUtils.BuildProviderDetails(Constants.SecondProviderName));

        var logger = Substitute.For<ITraceLogger>();

        var outcome = await new CreateDatabaseOperation(new CreateDatabaseRequest(
                path, source, new Regex("ZZZ_NoMatch_ZZZ", RegexOptions.IgnoreCase), SkipProvidersInFile: null, Overwrite: true))
            .ExecuteAsync(logger, null, CancellationToken.None);

        Assert.Equal(DatabaseToolsOutcome.Failed, outcome);
        Assert.True(File.Exists(path), "Existing database must remain when the overwrite resolved zero providers.");
        Assert.False(File.Exists(path + ".bak"), "No backup should be taken when no provider was ever persisted.");

        using var verify = new ProviderDbContext(path, readOnly: true, ensureCreated: false);
        var single = Assert.Single(verify.ProviderDetails.ToList());
        Assert.Equal(Constants.FirstProviderName, single.ProviderName);
    }

    [Fact]
    public async Task CreateDatabase_StampsContentHashVersionKey()
    {
        // Empty source VersionKey must be stamped so composite identity separates real versions.
        var source = CreateTempPath();
        DatabaseTestUtils.CreateV4Database(source, DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName));

        var path = CreateTempPath();
        var logger = Substitute.For<ITraceLogger>();

        await new CreateDatabaseOperation(new CreateDatabaseRequest(path, source, null, null)).ExecuteAsync(logger, null, CancellationToken.None);

        await using var context = new ProviderDbContext(path, readOnly: true, ensureCreated: false);
        var created = context.ProviderDetails.Single();

        Assert.Equal(Constants.FirstProviderName, created.ProviderName);
        Assert.StartsWith(VersionKeyCalculator.SchemePrefix, created.VersionKey);
        Assert.Equal(VersionKeyCalculator.Compute(created), created.VersionKey);
    }

    [Fact]
    public async Task CreateDatabase_WhenBothSourceAndOfflineImageGiven_LogsErrorAndDoesNotCreateFile()
    {
        // Mutual-exclusivity validation must beat source validation for a clear user error.
        var path = CreateTempPath();
        var logger = Substitute.For<ITraceLogger>();
        logger.ForCategory(Arg.Any<string>()).Returns(logger);

        await new CreateDatabaseOperation(new CreateDatabaseRequest(
            path, SourcePath: @"C:\src.db", FilterRegex: null, SkipProvidersInFile: null, OfflineImagePath: @"X:\"))
            .ExecuteAsync(logger, null, CancellationToken.None);

        Assert.False(File.Exists(path), "No file should be written when a source and an offline image are both given.");
        logger.Received(1).Error(Arg.Is<ErrorLogHandler>(h => h.ToString().Contains("source OR an offline image")));
    }

    [Fact]
    public async Task CreateDatabase_WhenExtensionNotDb_LogsErrorAndDoesNotCreateFile()
    {
        var path = DatabaseTestUtils.CreateTempPath(".txt");
        _tempPaths.Add(path);
        var logger = Substitute.For<ITraceLogger>();

        await new CreateDatabaseOperation(new CreateDatabaseRequest(path, null, null, null)).ExecuteAsync(logger, null, CancellationToken.None);

        Assert.False(File.Exists(path), "No file should be written when the extension is wrong.");
        logger.Received(1).Error(Arg.Is<ErrorLogHandler>(h =>
            h.ToString().Contains("File extension must be .db")));
    }

    [Fact]
    public async Task CreateDatabase_WhenFilterMatchesNoProviders_DoesNotLeaveEmptyDatabaseOnDisk()
    {
        // Zero resolved providers must not leave an empty .db that consumers would treat as a real collection.
        var source = CreateTempPath();
        DatabaseTestUtils.CreateV4Database(source,
            DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName),
            DatabaseTestUtils.BuildProviderDetails(Constants.SecondProviderName));

        var path = CreateTempPath();
        var logger = Substitute.For<ITraceLogger>();

        var operation = new CreateDatabaseOperation(new CreateDatabaseRequest(path, source, new Regex("ZZZ_NoMatch_ZZZ", RegexOptions.IgnoreCase), null));
        var outcome = await operation.ExecuteAsync(logger, null, CancellationToken.None);

        Assert.Equal(DatabaseToolsOutcome.Failed, outcome);
        Assert.Equal("No providers could be resolved from the source, so no database was created.", operation.FailureSummary);
        Assert.False(File.Exists(path), "No file should be written when zero providers were resolved.");
        logger.Received(1).Warning(Arg.Is<WarningLogHandler>(h =>
            h.ToString().Contains("No provider details could be resolved") &&
            h.ToString().Contains("Database was not created")));
        logger.DidNotReceive().Error(Arg.Any<ErrorLogHandler>());
    }

    [Fact]
    public async Task CreateDatabase_WhenProviderCountCrossesBatchSize_PersistsEveryProviderWithoutErrors()
    {
        // 101 providers crosses the 100-provider first-flush boundary and then appends another batch.
        const int ProviderCount = 101;
        var source = CreateTempPath();
        var providers = Enumerable.Range(0, ProviderCount)
            .Select(i => DatabaseTestUtils.BuildProviderDetails($"Provider-{i:D4}"))
            .ToArray();
        DatabaseTestUtils.CreateV4Database(source, providers);

        var path = CreateTempPath();
        var logger = Substitute.For<ITraceLogger>();

        await new CreateDatabaseOperation(new CreateDatabaseRequest(path, source, null, null)).ExecuteAsync(logger, null, CancellationToken.None);

        Assert.True(File.Exists(path));

        using var verify = new ProviderDbContext(path, readOnly: true, ensureCreated: false);
        Assert.Equal(ProviderCount, verify.ProviderDetails.Count());
        var firstName = verify.ProviderDetails.OrderBy(r => r.ProviderName).First().ProviderName;
        var lastName = verify.ProviderDetails.OrderBy(r => r.ProviderName).Last().ProviderName;
        Assert.Equal("Provider-0000", firstName);
        Assert.Equal($"Provider-{ProviderCount - 1:D4}", lastName);
        logger.DidNotReceive().Error(Arg.Any<ErrorLogHandler>());
        logger.DidNotReceive().Warning(Arg.Any<WarningLogHandler>());
    }

    [Fact]
    public async Task CreateDatabase_WhenSkipProvidersInFileExcludesAll_DoesNotLeaveEmptyDatabaseOnDisk()
    {
        // Skip-source exclusion can also resolve zero providers; it must not leave an empty .db.
        var source = CreateTempPath();
        DatabaseTestUtils.CreateV4Database(source,
            DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName),
            DatabaseTestUtils.BuildProviderDetails(Constants.SecondProviderName));

        var skipSource = CreateTempPath();
        DatabaseTestUtils.CreateV4Database(skipSource,
            DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName),
            DatabaseTestUtils.BuildProviderDetails(Constants.SecondProviderName));

        var path = CreateTempPath();
        var logger = Substitute.For<ITraceLogger>();

        var operation = new CreateDatabaseOperation(new CreateDatabaseRequest(path, source, null, skipSource));
        var outcome = await operation.ExecuteAsync(logger, null, CancellationToken.None);

        Assert.Equal(DatabaseToolsOutcome.Failed, outcome);
        Assert.Equal("No providers could be resolved from the source, so no database was created.", operation.FailureSummary);
        Assert.False(File.Exists(path), "No file should be written when the skip-set excludes all providers.");
        logger.Received(1).Warning(Arg.Is<WarningLogHandler>(h =>
            h.ToString().Contains("No provider details could be resolved") &&
            h.ToString().Contains("Database was not created")));
        logger.DidNotReceive().Error(Arg.Any<ErrorLogHandler>());
    }

    [Fact]
    public async Task CreateDatabase_WhenSkipProvidersInFileResolves_ExcludesThoseProvidersFromOutput()
    {
        var source = CreateTempPath();
        DatabaseTestUtils.CreateV4Database(source,
            DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName),
            DatabaseTestUtils.BuildProviderDetails(Constants.SecondProviderName),
            DatabaseTestUtils.BuildProviderDetails(Constants.SharedProviderName));

        var skipSource = CreateTempPath();
        DatabaseTestUtils.CreateV4Database(skipSource,
            DatabaseTestUtils.BuildProviderDetails(Constants.SharedProviderName));

        var path = CreateTempPath();
        var logger = Substitute.For<ITraceLogger>();

        await new CreateDatabaseOperation(new CreateDatabaseRequest(path, source, null, skipSource)).ExecuteAsync(logger, null, CancellationToken.None);

        Assert.True(File.Exists(path));

        using var verify = new ProviderDbContext(path, readOnly: true, ensureCreated: false);
        var names = verify.ProviderDetails.Select(r => r.ProviderName).OrderBy(n => n).ToList();
        Assert.Equal(new[] { Constants.FirstProviderName, Constants.SecondProviderName }, names);
        logger.DidNotReceive().Error(Arg.Any<ErrorLogHandler>());
        logger.DidNotReceive().Warning(Arg.Any<WarningLogHandler>());
    }

    [Fact]
    public async Task CreateDatabase_WhenSkipProvidersInFileSourceDoesNotExist_LogsErrorAndDoesNotCreateFile()
    {
        // Missing skip-source validation must run before any output database is created.
        var path = CreateTempPath();
        var source = CreateTempPath();
        DatabaseTestUtils.CreateV4Database(source,
            DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName));

        var missingSkipSource = DatabaseTestUtils.CreateTempPath();
        var logger = Substitute.For<ITraceLogger>();

        await new CreateDatabaseOperation(new CreateDatabaseRequest(path, source, null, missingSkipSource)).ExecuteAsync(logger, null, CancellationToken.None);

        Assert.False(File.Exists(path), "No file should be written when the skip-source is missing.");
        logger.Received(1).Error(Arg.Is<ErrorLogHandler>(h =>
            h.ToString().Contains("Source not found") && h.ToString().Contains(missingSkipSource)));
    }

    [Fact]
    public async Task CreateDatabase_WhenSourceDoesNotExist_LogsErrorAndDoesNotCreateFile()
    {
        var path = CreateTempPath();
        var missingSource = DatabaseTestUtils.CreateTempPath();
        var logger = Substitute.For<ITraceLogger>();

        await new CreateDatabaseOperation(new CreateDatabaseRequest(path, missingSource, null, null)).ExecuteAsync(logger, null, CancellationToken.None);

        Assert.False(File.Exists(path), "No file should be written when the source is missing.");
        logger.Received(1).Error(Arg.Is<ErrorLogHandler>(h =>
            h.ToString().Contains("Source not found") && h.ToString().Contains(missingSource)));
    }

    [Fact]
    public async Task CreateDatabase_WhenSourceProvidersResolved_PersistsAllProvidersAndPreservesOwningPublisher()
    {
        var source = CreateTempPath();
        DatabaseTestUtils.CreateV4Database(source,
            DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName),
            DatabaseTestUtils.BuildProviderDetails(Constants.SecondProviderName, Constants.OwningPublisherName));

        var path = CreateTempPath();
        var logger = Substitute.For<ITraceLogger>();

        await new CreateDatabaseOperation(new CreateDatabaseRequest(path, source, null, null)).ExecuteAsync(logger, null, CancellationToken.None);

        Assert.True(File.Exists(path), "Output database should be created when providers are resolved.");

        using var verify = new ProviderDbContext(path, readOnly: true, ensureCreated: false);
        var rows = verify.ProviderDetails.OrderBy(r => r.ProviderName).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(Constants.FirstProviderName, rows[0].ProviderName);
        Assert.Null(rows[0].ResolvedFromOwningPublisher);
        Assert.Equal(Constants.SecondProviderName, rows[1].ProviderName);
        Assert.Equal(Constants.OwningPublisherName, rows[1].ResolvedFromOwningPublisher);
        // Success must not emit stale zero-provider warnings.
        logger.DidNotReceive().Error(Arg.Any<ErrorLogHandler>());
        logger.DidNotReceive().Warning(Arg.Any<WarningLogHandler>());
    }

    [Fact]
    public async Task CreateDatabase_WhenStaleBackupExists_AndTargetMissing_FailsAndLeavesBackupUntouched()
    {
        // Stale .bak may be the only surviving copy after an interrupted overwrite; never proceed over it.
        var path = CreateTempPath();
        var backupPath = path + ".bak";
        _tempPaths.Add(backupPath);
        File.WriteAllText(backupPath, "STALE_BACKUP_CONTENT");

        var source = CreateTempPath();
        DatabaseTestUtils.CreateV4Database(source, DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName));

        var logger = Substitute.For<ITraceLogger>();

        var outcome = await new CreateDatabaseOperation(new CreateDatabaseRequest(
                path, source, FilterRegex: null, SkipProvidersInFile: null, Overwrite: true))
            .ExecuteAsync(logger, null, CancellationToken.None);

        Assert.Equal(DatabaseToolsOutcome.Failed, outcome);
        Assert.False(File.Exists(path), "Target must not be created while a stale .bak is present.");
        Assert.True(File.Exists(backupPath), "The stale backup must be left untouched.");
        Assert.Equal("STALE_BACKUP_CONTENT", File.ReadAllText(backupPath));
        logger.Received(1).Error(Arg.Is<ErrorLogHandler>(h => h.ToString().Contains("recovery backup")));
    }

    [Fact]
    public async Task CreateDatabase_WhenTargetFileAlreadyExists_LogsErrorAndDoesNotOverwrite()
    {
        var path = CreateTempPath();
        var sentinel = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        File.WriteAllBytes(path, sentinel);
        var logger = Substitute.For<ITraceLogger>();

        await new CreateDatabaseOperation(new CreateDatabaseRequest(path, null, null, null)).ExecuteAsync(logger, null, CancellationToken.None);

        Assert.Equal(sentinel, File.ReadAllBytes(path));
        logger.Received(1).Error(Arg.Is<ErrorLogHandler>(h =>
            h.ToString().Contains("file already exists") && h.ToString().Contains(path)));
    }

    [Fact]
    public async Task CreateDatabase_WhenWimImageFileMissing_LogsErrorAndDoesNotCreateFile()
    {
        var path = CreateTempPath();
        var logger = Substitute.For<ITraceLogger>();
        logger.ForCategory(Arg.Any<string>()).Returns(logger);

        await new CreateDatabaseOperation(new CreateDatabaseRequest(
            path, SourcePath: null, FilterRegex: null, SkipProvidersInFile: null, OfflineImagePath: @"X:\missing.wim",
            ImageKind: OfflineImageKind.Wim, WimIndex: 1)).ExecuteAsync(logger, null, CancellationToken.None);

        Assert.False(File.Exists(path), "A missing WIM file is rejected; no file should be written.");
        logger.Received(1).Error(Arg.Is<ErrorLogHandler>(h => h.ToString().Contains("WIM image file not found")));
    }

    [Fact]
    public async Task CreateDatabase_WhenWimIndexGivenForDirectoryImage_LogsErrorAndDoesNotCreateFile()
    {
        // WimIndex on a directory image must fail instead of being silently ignored.
        var path = CreateTempPath();
        var logger = Substitute.For<ITraceLogger>();
        logger.ForCategory(Arg.Any<string>()).Returns(logger);

        await new CreateDatabaseOperation(new CreateDatabaseRequest(
            path, SourcePath: null, FilterRegex: null, SkipProvidersInFile: null, OfflineImagePath: @"X:\",
            ImageKind: OfflineImageKind.Directory, WimIndex: 1)).ExecuteAsync(logger, null, CancellationToken.None);

        Assert.False(File.Exists(path), "WimIndex applies only to WIM images; no file should be written.");
        logger.Received(1).Error(Arg.Is<ErrorLogHandler>(h => h.ToString().Contains("--wim-index applies only to")));
    }

    public void Dispose()
    {
        foreach (var path in _tempPaths)
        {
            DatabaseTestUtils.DeleteDatabaseFile(path);
        }

        foreach (var dir in _tempDirs)
        {
            DatabaseTestUtils.DeleteDirectoryRecursive(dir);
        }
    }

    private string CreateTempDir()
    {
        var dir = DatabaseTestUtils.CreateTempDirectory();
        _tempDirs.Add(dir);

        return dir;
    }

    private string CreateTempPath()
    {
        var path = DatabaseTestUtils.CreateTempPath();
        _tempPaths.Add(path);

        return path;
    }

    private sealed class CancelWhenProcessedReaches(int threshold, CancellationTokenSource cts) : IProgress<DatabaseToolsProgress>
    {
        public void Report(DatabaseToolsProgress value)
        {
            if (value.Processed >= threshold) { cts.Cancel(); }
        }
    }

    // Locks .bak after backup so restore fails deterministically.
    private sealed class LockBackupThenCancel(int threshold, CancellationTokenSource cts, string backupPath, Action<FileStream> onLocked)
        : IProgress<DatabaseToolsProgress>
    {
        private bool _done;

        public void Report(DatabaseToolsProgress value)
        {
            if (_done || value.Processed < threshold) { return; }

            _done = true;
            onLocked(new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.None));
            cts.Cancel();
        }
    }
}
