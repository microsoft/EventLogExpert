// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Contracts;
using EventLogExpert.DatabaseTools.Operations;
using EventLogExpert.Eventing.TestUtils;
using EventLogExpert.Eventing.TestUtils.Constants;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
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

    private string CreateTempDb()
    {
        var path = DatabaseTestUtils.CreateTempPath();
        _tempPaths.Add(path);
        return path;
    }
}
