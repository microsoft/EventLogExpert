// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.DatabaseTools.MergeDatabase;
using EventLogExpert.Eventing.TestUtils;
using EventLogExpert.Eventing.TestUtils.Constants;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using EventLogExpert.Provider.Database.Context;
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

        // Pre-stamp sources must fail clearly instead of reporting success while copying nothing.
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

        // Zero source providers must fail truthfully instead of reporting success while modifying nothing.
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

        // Without overwrite, skip by composite identity; a name-level skip would drop vk2.
        await new MergeDatabaseOperation(new MergeDatabaseRequest(source, target, false)).ExecuteAsync(logger, null, CancellationToken.None);

        Assert.Equal(["vk1", "vk2"], ReadVersionKeys(target, Constants.FirstProviderName));
    }

    [Fact]
    public async Task MergeDatabaseWithOverwrite_NonAsciiCaseVariantNames_DoesNotCollide()
    {
        var source = CreateTempDb();
        var target = CreateTempDb();

        // SQLite NOCASE folds only ASCII, so non-ASCII case variants must remain distinct identities.
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

        // Overwrite must delete by composite identity; a name-level delete would drop target vk2.
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
