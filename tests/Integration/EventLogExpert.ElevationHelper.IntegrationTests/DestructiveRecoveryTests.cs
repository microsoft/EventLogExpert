// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.DatabaseTools.CreateDatabase;
using EventLogExpert.DatabaseTools.DiffDatabase;
using EventLogExpert.DatabaseTools.UpgradeDatabase;
using EventLogExpert.ElevationHelper.IntegrationTests.TestUtils;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.DatabaseTools.Elevation;

namespace EventLogExpert.ElevationHelper.IntegrationTests;

public sealed class DestructiveRecoveryTests
{
    [Fact]
    public async Task CreateDatabase_TargetAlreadyExists_FailsWithoutDeletingPreExistingFile()
    {
        var ct = TestContext.Current.CancellationToken;
        var tempDir = CreateTempDir();
        var targetPath = Path.Combine(tempDir, "preexisting.db");

        const string PreExistingContents = "USER_PRECIOUS_DATABASE_CONTENT_DO_NOT_DELETE";
        await File.WriteAllTextAsync(targetPath, PreExistingContents, ct);

        var logger = new IntegrationTraceLogger();
        var host = new TestElevatedHelperProcessHost(logger);
        var runner = new ElevatedDatabaseToolsRunner(host, logger);
        var logSink = new ListProgress<LogRecord>();

        try
        {
            var result = await runner.CreateAsync(
                new CreateDatabaseRequest(targetPath, SourcePath: null, FilterRegex: null, SkipProvidersInFile: null),
                logSink, progress: null, ct);

            Assert.Equal(DatabaseToolsOutcome.Failed, result.Outcome);
            Assert.True(File.Exists(targetPath),
                $"Pre-existing user file at {targetPath} must NOT be deleted when Create refuses to overwrite.");
            var actual = await File.ReadAllTextAsync(targetPath, ct);
            Assert.Equal(PreExistingContents, actual);
        }
        finally
        {
            TryDeleteDir(tempDir);
        }
    }

    [Fact]
    public async Task CreateDatabase_WithMissingSourceFile_FailsAndDeletesPartialTargetDb()
    {
        var ct = TestContext.Current.CancellationToken;
        var tempDir = CreateTempDir();
        var targetPath = Path.Combine(tempDir, "doomed.db");
        var bogusSourcePath = Path.Combine(tempDir, "does-not-exist.db");

        var logger = new IntegrationTraceLogger();
        var host = new TestElevatedHelperProcessHost(logger);
        var runner = new ElevatedDatabaseToolsRunner(host, logger);
        var logSink = new ListProgress<LogRecord>();

        try
        {
            var result = await runner.CreateAsync(
                new CreateDatabaseRequest(targetPath, bogusSourcePath, FilterRegex: null, SkipProvidersInFile: null),
                logSink, progress: null, ct);

            Assert.Equal(DatabaseToolsOutcome.Failed, result.Outcome);
            Assert.False(File.Exists(targetPath),
                $"Destructive recovery must delete the partial target db at {targetPath} after Failed Create.");
        }
        finally
        {
            TryDeleteDir(tempDir);
        }
    }

    [Fact]
    public async Task DiffDatabase_OutputAlreadyExists_FailsWithoutDeletingPreExistingFile()
    {
        var ct = TestContext.Current.CancellationToken;
        var tempDir = CreateTempDir();
        var firstPath = Path.Combine(tempDir, "first.db");
        var secondPath = Path.Combine(tempDir, "second.db");
        var diffOutputPath = Path.Combine(tempDir, "preexisting-diff.db");

        const string PreExistingContents = "USER_PRECIOUS_DIFF_OUTPUT_DO_NOT_DELETE";
        await File.WriteAllTextAsync(diffOutputPath, PreExistingContents, ct);

        var logger = new IntegrationTraceLogger();
        var host = new TestElevatedHelperProcessHost(logger);
        var runner = new ElevatedDatabaseToolsRunner(host, logger);
        var logSink = new ListProgress<LogRecord>();

        try
        {
            var firstCreate = await runner.CreateAsync(
                new CreateDatabaseRequest(firstPath, null, null, null), logSink, progress: null, ct);
            Assert.Equal(DatabaseToolsOutcome.Succeeded, firstCreate.Outcome);

            var secondCreate = await runner.CreateAsync(
                new CreateDatabaseRequest(secondPath, null, null, null), logSink, progress: null, ct);
            Assert.Equal(DatabaseToolsOutcome.Succeeded, secondCreate.Outcome);

            var result = await runner.DiffAsync(
                new DiffDatabaseRequest(firstPath, secondPath, diffOutputPath),
                logSink, progress: null, ct);

            Assert.Equal(DatabaseToolsOutcome.Failed, result.Outcome);
            Assert.True(File.Exists(diffOutputPath),
                $"Pre-existing user file at {diffOutputPath} must NOT be deleted when Diff refuses to overwrite.");
            var actual = await File.ReadAllTextAsync(diffOutputPath, ct);
            Assert.Equal(PreExistingContents, actual);
        }
        finally
        {
            TryDeleteDir(tempDir);
        }
    }

    [Fact]
    public async Task DiffDatabase_WithMissingFirstSource_FailsAndDeletesPartialNewDatabase()
    {
        var ct = TestContext.Current.CancellationToken;
        var tempDir = CreateTempDir();
        var bogusFirstPath = Path.Combine(tempDir, "missing-first.db");
        var bogusSecondPath = Path.Combine(tempDir, "missing-second.db");
        var diffOutputPath = Path.Combine(tempDir, "diff-output.db");

        var logger = new IntegrationTraceLogger();
        var host = new TestElevatedHelperProcessHost(logger);
        var runner = new ElevatedDatabaseToolsRunner(host, logger);
        var logSink = new ListProgress<LogRecord>();

        try
        {
            var result = await runner.DiffAsync(
                new DiffDatabaseRequest(bogusFirstPath, bogusSecondPath, diffOutputPath),
                logSink, progress: null, ct);

            Assert.Equal(DatabaseToolsOutcome.Failed, result.Outcome);
            Assert.False(File.Exists(diffOutputPath),
                $"Destructive recovery must delete the partial diff output at {diffOutputPath} after Failed Diff.");
        }
        finally
        {
            TryDeleteDir(tempDir);
        }
    }

    [Fact]
    public async Task UpgradeDatabase_OnMalformedFile_FailsRestoresOriginalContentDeletesBackup()
    {
        var ct = TestContext.Current.CancellationToken;
        var tempDir = CreateTempDir();
        var dbPath = Path.Combine(tempDir, "malformed.db");
        var bakPath = dbPath + ".bak";

        const string OriginalContents = "NOT A REAL SQLITE DATABASE - SHOULD BE RESTORED VERBATIM";
        await File.WriteAllTextAsync(dbPath, OriginalContents, ct);

        var logger = new IntegrationTraceLogger();
        var host = new TestElevatedHelperProcessHost(logger);
        var runner = new ElevatedDatabaseToolsRunner(host, logger);
        var logSink = new ListProgress<LogRecord>();

        try
        {
            var result = await runner.UpgradeAsync(
                new UpgradeDatabaseRequest(dbPath),
                logSink, progress: null, ct);

            Assert.Equal(DatabaseToolsOutcome.Failed, result.Outcome);
            Assert.True(File.Exists(dbPath), "Original file must be restored from backup after failed upgrade.");
            var restored = await File.ReadAllTextAsync(dbPath, ct);
            Assert.Equal(OriginalContents, restored);
            Assert.False(File.Exists(bakPath),
                $"Backup at {bakPath} must be deleted after destructive recovery restored from it.");
        }
        finally
        {
            TryDeleteDir(tempDir);
        }
    }

    [Fact]
    public async Task UpgradeDatabase_PreExistingBackup_RefusesAndPreservesBothFiles()
    {
        var ct = TestContext.Current.CancellationToken;
        var tempDir = CreateTempDir();
        var dbPath = Path.Combine(tempDir, "target.db");
        var bakPath = dbPath + ".bak";

        const string TargetContents = "CURRENT_TARGET_CONTENT";
        const string BakContents = "PREVIOUS_RECOVERY_BACKUP_DO_NOT_CLOBBER";
        await File.WriteAllTextAsync(dbPath, TargetContents, ct);
        await File.WriteAllTextAsync(bakPath, BakContents, ct);

        var logger = new IntegrationTraceLogger();
        var host = new TestElevatedHelperProcessHost(logger);
        var runner = new ElevatedDatabaseToolsRunner(host, logger);
        var logSink = new ListProgress<LogRecord>();

        try
        {
            var result = await runner.UpgradeAsync(
                new UpgradeDatabaseRequest(dbPath), logSink, progress: null, ct);

            Assert.Equal(DatabaseToolsOutcome.Failed, result.Outcome);
            Assert.NotNull(result.FailureSummary);
            Assert.Contains("recovery backup", result.FailureSummary, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(TargetContents, await File.ReadAllTextAsync(dbPath, ct));
            Assert.Equal(BakContents, await File.ReadAllTextAsync(bakPath, ct));
        }
        finally
        {
            TryDeleteDir(tempDir);
        }
    }

    [Fact]
    public async Task UpgradeDatabase_PreExistingBackupButMissingTarget_FailsWithoutTouchingBackup()
    {
        var ct = TestContext.Current.CancellationToken;
        var tempDir = CreateTempDir();
        var dbPath = Path.Combine(tempDir, "missing-target.db");
        var bakPath = dbPath + ".bak";

        const string PreExistingBakContents = "USER_RECOVERY_SNAPSHOT_DO_NOT_TOUCH";
        await File.WriteAllTextAsync(bakPath, PreExistingBakContents, ct);

        var logger = new IntegrationTraceLogger();
        var host = new TestElevatedHelperProcessHost(logger);
        var runner = new ElevatedDatabaseToolsRunner(host, logger);
        var logSink = new ListProgress<LogRecord>();

        try
        {
            var result = await runner.UpgradeAsync(
                new UpgradeDatabaseRequest(dbPath), logSink, progress: null, ct);

            Assert.Equal(DatabaseToolsOutcome.Failed, result.Outcome);
            Assert.False(File.Exists(dbPath),
                $"Target {dbPath} did not exist before the call and must NOT be materialized from a pre-existing .bak this run did not create.");
            Assert.True(File.Exists(bakPath),
                $"Pre-existing recovery backup at {bakPath} must NOT be deleted when this run did not create it.");
            Assert.Equal(PreExistingBakContents, await File.ReadAllTextAsync(bakPath, ct));
        }
        finally
        {
            TryDeleteDir(tempDir);
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"elt-recovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) { Directory.Delete(dir, recursive: true); }
        }
        catch { /* test cleanup, best effort */ }
    }
}
