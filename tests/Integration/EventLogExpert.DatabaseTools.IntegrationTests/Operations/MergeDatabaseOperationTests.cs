// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.DatabaseTools.MergeDatabase;
using EventLogExpert.Eventing.TestUtils;
using EventLogExpert.Eventing.TestUtils.Constants;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using EventLogExpert.ProviderDatabase.Context;
using NSubstitute;

namespace EventLogExpert.DatabaseTools.IntegrationTests.Operations;

public sealed class MergeDatabaseCommandTests : IDisposable
{
    private readonly List<string> _tempPaths = [];

    public void Dispose()
    {
        foreach (var path in _tempPaths)
        {
            DatabaseTestUtils.DeleteDatabaseFile(path);
        }
    }

    [Fact]
    public async Task MergeDatabase_WithCorruptTarget_LogsErrorWithoutThrowing()
    {
        var source = CreateTempDb();
        var target = CreateTempDb();

        DatabaseTestUtils.CreateV4Database(source,
            DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName));
        File.WriteAllBytes(target, [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07]);

        var logger = Substitute.For<ITraceLogger>();

        await new MergeDatabaseOperation(new MergeDatabaseRequest(source, target, false)).ExecuteAsync(logger, null, CancellationToken.None);

        logger.Received().Error(Arg.Is<ErrorLogHandler>(handler =>
            handler.ToString().Contains("Failed to merge into database") && handler.ToString().Contains(target)));
    }

    [Fact]
    public async Task MergeDatabase_WithEmptyTargetFile_LogsUnrecognizedSchemaWithoutCreatingTables()
    {
        var source = CreateTempDb();
        var target = CreateTempDb();

        DatabaseTestUtils.CreateV4Database(source,
            DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName));
        File.WriteAllBytes(target, []);

        var logger = Substitute.For<ITraceLogger>();

        await new MergeDatabaseOperation(new MergeDatabaseRequest(source, target, false)).ExecuteAsync(logger, null, CancellationToken.None);

        logger.Received().Error(Arg.Is<ErrorLogHandler>(handler =>
            handler.ToString().Contains("unrecognized schema") && handler.ToString().Contains(target)));
    }

    [Fact]
    public async Task MergeDatabase_WithSourceNeedingUpgrade_FailsInsteadOfReportingSuccess()
    {
        var source = CreateTempDb();
        var target = CreateTempDb();

        // A pre-stamp (v3) source is classified as needing an upgrade. Merge must FAIL with a clear error rather than
        // silently reporting success while copying nothing (mirrors the diff operation's source-schema validation).
        DatabaseTestUtils.CreateV3Database(source);
        DatabaseTestUtils.CreateV4Database(target, DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName));

        var logger = Substitute.For<ITraceLogger>();

        var outcome = await new MergeDatabaseOperation(new MergeDatabaseRequest(source, target, false))
            .ExecuteAsync(logger, null, CancellationToken.None);

        Assert.Equal(DatabaseToolsOutcome.Failed, outcome);
        logger.Received().Error(Arg.Is<ErrorLogHandler>(handler => handler.ToString().Contains(source)));
    }

    [Fact]
    public async Task MergeDatabase_WithUnknownSchemaTarget_LogsErrorWithoutThrowing()
    {
        var source = CreateTempDb();
        var target = CreateTempDb();

        DatabaseTestUtils.CreateV4Database(source,
            DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName));
        DatabaseTestUtils.CreateUnknownShapeDatabase(target);

        var logger = Substitute.For<ITraceLogger>();

        await new MergeDatabaseOperation(new MergeDatabaseRequest(source, target, false)).ExecuteAsync(logger, null, CancellationToken.None);

        logger.Received().Error(Arg.Is<ErrorLogHandler>(handler =>
            handler.ToString().Contains("unrecognized schema") && handler.ToString().Contains(target)));
    }

    [Fact]
    public async Task MergeDatabase_WithZeroSourceProviders_FailsInsteadOfReportingSuccess()
    {
        var source = CreateTempDb();
        var target = CreateTempDb();

        // An empty (but valid v4) source resolves zero provider identities. Merge must FAIL with a clear summary rather
        // than reporting success while modifying nothing, mirroring the zero-result truthfulness of the create path.
        DatabaseTestUtils.CreateV4Database(source);
        DatabaseTestUtils.CreateV4Database(target, DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName));

        var logger = Substitute.For<ITraceLogger>();

        var operation = new MergeDatabaseOperation(new MergeDatabaseRequest(source, target, false));
        var outcome = await operation.ExecuteAsync(logger, null, CancellationToken.None);

        Assert.Equal(DatabaseToolsOutcome.Failed, outcome);
        Assert.Equal("No providers were discovered in the source, so the database was not modified.", operation.FailureSummary);
        logger.Received().Warning(Arg.Is<WarningLogHandler>(handler =>
            handler.ToString().Contains("No providers were discovered in the source")));
    }

    [Fact]
    public async Task MergeDatabaseWithoutOverwrite_CopiesNewVersionOfExistingProviderName()
    {
        var source = CreateTempDb();
        var target = CreateTempDb();

        var targetV1 = DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName);
        targetV1.VersionKey = "vk1";
        DatabaseTestUtils.CreateV4Database(target, targetV1);

        var sourceV1 = DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName);
        sourceV1.VersionKey = "vk1";
        var sourceV2 = DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName);
        sourceV2.VersionKey = "vk2";
        DatabaseTestUtils.CreateV4Database(source, sourceV1, sourceV2);

        var logger = Substitute.For<ITraceLogger>();

        // Without overwrite, the merge skips only the (name, vk1) identity already in the target and still copies the
        // source's new vk2 version of the same name. A name-level skip would drop vk2 as a duplicate name.
        await new MergeDatabaseOperation(new MergeDatabaseRequest(source, target, false)).ExecuteAsync(logger, null, CancellationToken.None);

        Assert.Equal(["vk1", "vk2"], ReadVersionKeys(target, Constants.FirstProviderName));
    }

    [Fact]
    public async Task MergeDatabaseWithOverwrite_NonAsciiCaseVariantNames_DoesNotCollide()
    {
        var source = CreateTempDb();
        var target = CreateTempDb();

        // Provider names differing only by NON-ASCII case (U+00C4 vs U+00E4). SQLite NOCASE folds only ASCII, so
        // these are DISTINCT primary-key rows; the in-memory identity must treat them as distinct too, or an overwrite
        // merge deletes only one and then re-inserts both, hitting a primary-key collision.
        DatabaseTestUtils.CreateV4Database(source,
            DatabaseTestUtils.BuildProviderDetails("Provider-\u00c4"),
            DatabaseTestUtils.BuildProviderDetails("Provider-\u00e4"));
        DatabaseTestUtils.CreateV4Database(target,
            DatabaseTestUtils.BuildProviderDetails("Provider-\u00c4"),
            DatabaseTestUtils.BuildProviderDetails("Provider-\u00e4"));

        var logger = Substitute.For<ITraceLogger>();

        await new MergeDatabaseOperation(new MergeDatabaseRequest(source, target, true)).ExecuteAsync(logger, null, CancellationToken.None);

        using var context = new ProviderDbContext(target, readOnly: true, ensureCreated: false);
        var names = context.ProviderDetails
            .Select(p => p.ProviderName)
            .AsEnumerable()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Provider-\u00c4", "Provider-\u00e4"], names);
    }

    [Fact]
    public async Task MergeDatabaseWithOverwrite_RemovesOnlyCollidingVersionAndKeepsOtherTargetVersion()
    {
        var source = CreateTempDb();
        var target = CreateTempDb();

        var targetV1 = DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName);
        targetV1.VersionKey = "vk1";
        var targetV2 = DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName);
        targetV2.VersionKey = "vk2";
        DatabaseTestUtils.CreateV4Database(target, targetV1, targetV2);

        var sourceV1 = DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName);
        sourceV1.VersionKey = "vk1";
        DatabaseTestUtils.CreateV4Database(source, sourceV1);

        var logger = Substitute.For<ITraceLogger>();

        // Overwrite deletes only the colliding (name, vk1) identity by composite key and re-copies it from the source;
        // the target's vk2 version, which the source does not carry, must survive. A name-level delete would drop vk2.
        await new MergeDatabaseOperation(new MergeDatabaseRequest(source, target, true)).ExecuteAsync(logger, null, CancellationToken.None);

        Assert.Equal(["vk1", "vk2"], ReadVersionKeys(target, Constants.FirstProviderName));
    }

    private static string[] ReadVersionKeys(string databasePath, string providerName)
    {
        using var context = new ProviderDbContext(databasePath, readOnly: true, ensureCreated: false);

        return context.ProviderDetails
            .Where(p => p.ProviderName == providerName)
            .Select(p => p.VersionKey)
            .AsEnumerable()
            .OrderBy(versionKey => versionKey, StringComparer.Ordinal)
            .ToArray();
    }

    private string CreateTempDb()
    {
        var path = DatabaseTestUtils.CreateTempPath();
        _tempPaths.Add(path);
        return path;
    }
}
