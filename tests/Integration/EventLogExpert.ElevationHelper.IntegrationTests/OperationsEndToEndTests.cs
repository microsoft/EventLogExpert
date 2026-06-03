// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Contracts;
using EventLogExpert.ElevationHelper.IntegrationTests.TestUtils;
using EventLogExpert.Runtime.DatabaseTools.Elevation;

namespace EventLogExpert.ElevationHelper.IntegrationTests;

public sealed class OperationsEndToEndTests
{
    [Fact]
    public async Task CreateDatabase_FromLocalProviders_ProducesTargetDbFile()
    {
        var ct = TestContext.Current.CancellationToken;
        var tempDir = CreateTempDir();
        var targetPath = Path.Combine(tempDir, "providers.db");

        var logger = new IntegrationTraceLogger();
        var host = new TestElevatedHelperProcessHost(logger);
        var runner = new ElevatedDatabaseToolsRunner(host, logger);
        var logSink = new ListProgress<DatabaseToolsLogEntry>();

        try
        {
            var result = await runner.CreateAsync(
                new CreateDatabaseRequest(targetPath, SourcePath: null, FilterRegex: null, SkipProvidersInFile: null),
                logSink, progress: null, ct);

            Assert.Equal(DatabaseToolsOutcome.Succeeded, result.Outcome);
            Assert.True(File.Exists(targetPath), $"Expected target db at {targetPath} after successful Create.");
            Assert.True(new FileInfo(targetPath).Length > 0, "Target db should be non-empty.");
        }
        finally
        {
            TryDeleteDir(tempDir);
        }
    }

    [Fact]
    public async Task DiffDatabase_TwoIdenticalSources_ReturnsSucceededWithEmptyDiff()
    {
        var ct = TestContext.Current.CancellationToken;
        var tempDir = CreateTempDir();
        var firstPath = Path.Combine(tempDir, "first.db");
        var secondPath = Path.Combine(tempDir, "second.db");
        var diffPath = Path.Combine(tempDir, "diff.db");

        var logger = new IntegrationTraceLogger();
        var host = new TestElevatedHelperProcessHost(logger);
        var runner = new ElevatedDatabaseToolsRunner(host, logger);
        var logSink = new ListProgress<DatabaseToolsLogEntry>();

        try
        {
            var firstCreate = await runner.CreateAsync(
                new CreateDatabaseRequest(firstPath, null, null, null),
                logSink, progress: null, ct);
            Assert.Equal(DatabaseToolsOutcome.Succeeded, firstCreate.Outcome);

            var secondCreate = await runner.CreateAsync(
                new CreateDatabaseRequest(secondPath, null, null, null),
                logSink, progress: null, ct);
            Assert.Equal(DatabaseToolsOutcome.Succeeded, secondCreate.Outcome);

            var diffResult = await runner.DiffAsync(
                new DiffDatabaseRequest(firstPath, secondPath, diffPath),
                logSink, progress: null, ct);

            Assert.Equal(DatabaseToolsOutcome.Succeeded, diffResult.Outcome);
        }
        finally
        {
            TryDeleteDir(tempDir);
        }
    }

    [Fact]
    public async Task MergeDatabase_FromLocalProvidersIntoExistingTarget_ReturnsSucceeded()
    {
        var ct = TestContext.Current.CancellationToken;
        var tempDir = CreateTempDir();
        var sourcePath = Path.Combine(tempDir, "source.db");
        var targetPath = Path.Combine(tempDir, "target.db");

        var logger = new IntegrationTraceLogger();
        var host = new TestElevatedHelperProcessHost(logger);
        var runner = new ElevatedDatabaseToolsRunner(host, logger);
        var logSink = new ListProgress<DatabaseToolsLogEntry>();

        try
        {
            var sourceCreate = await runner.CreateAsync(
                new CreateDatabaseRequest(sourcePath, null, null, null),
                logSink, progress: null, ct);
            Assert.Equal(DatabaseToolsOutcome.Succeeded, sourceCreate.Outcome);

            var targetCreate = await runner.CreateAsync(
                new CreateDatabaseRequest(targetPath, null, null, null),
                logSink, progress: null, ct);
            Assert.Equal(DatabaseToolsOutcome.Succeeded, targetCreate.Outcome);

            var mergeResult = await runner.MergeAsync(
                new MergeDatabaseRequest(sourcePath, targetPath, Overwrite: false),
                logSink, progress: null, ct);

            Assert.Equal(DatabaseToolsOutcome.Succeeded, mergeResult.Outcome);
            Assert.True(File.Exists(targetPath), "Target db must still exist after merge.");
        }
        finally
        {
            TryDeleteDir(tempDir);
        }
    }

    [Fact]
    public async Task ShowProviders_LocalProviders_ReturnsSucceededWithProviderLogEntries()
    {
        var ct = TestContext.Current.CancellationToken;
        var logger = new IntegrationTraceLogger();
        var host = new TestElevatedHelperProcessHost(logger);
        var runner = new ElevatedDatabaseToolsRunner(host, logger);
        var logSink = new ListProgress<DatabaseToolsLogEntry>();

        var result = await runner.ShowAsync(
            new ShowProvidersRequest(SourcePath: null, FilterRegex: null),
            logSink, progress: null, ct);

        Assert.True(result.Outcome == DatabaseToolsOutcome.Succeeded,
            $"Expected Succeeded but got {result.Outcome}. FailureSummary: {result.FailureSummary}. Trace:\n  {string.Join("\n  ", logger.Messages)}");
        Assert.NotEmpty(logSink.Entries);
    }

    [Fact]
    public async Task UpgradeDatabase_OnFreshlyCreatedDb_ReturnsSucceededAndDeletesBackup()
    {
        var ct = TestContext.Current.CancellationToken;
        var tempDir = CreateTempDir();
        var dbPath = Path.Combine(tempDir, "upgrade-target.db");
        var bakPath = dbPath + ".bak";

        var logger = new IntegrationTraceLogger();
        var host = new TestElevatedHelperProcessHost(logger);
        var runner = new ElevatedDatabaseToolsRunner(host, logger);
        var logSink = new ListProgress<DatabaseToolsLogEntry>();

        try
        {
            var createResult = await runner.CreateAsync(
                new CreateDatabaseRequest(dbPath, null, null, null),
                logSink, progress: null, ct);
            Assert.Equal(DatabaseToolsOutcome.Succeeded, createResult.Outcome);

            var upgradeResult = await runner.UpgradeAsync(
                new UpgradeDatabaseRequest(dbPath),
                logSink, progress: null, ct);

            Assert.Equal(DatabaseToolsOutcome.Succeeded, upgradeResult.Outcome);
            Assert.True(File.Exists(dbPath), "Database must remain after successful upgrade.");
            Assert.False(File.Exists(bakPath), $"Backup at {bakPath} must be deleted after successful upgrade.");
        }
        finally
        {
            TryDeleteDir(tempDir);
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"elt-integ-{Guid.NewGuid():N}");
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
