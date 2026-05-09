// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.EventDbTool.Tests.TestUtils;
using EventLogExpert.Eventing.Logging;
using EventLogExpert.Eventing.ProviderDatabase;
using NSubstitute;

namespace EventLogExpert.EventDbTool.Tests;

public sealed class UpgradeDatabaseCommandTests : IDisposable
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
    public void UpgradeDatabase_WithCorruptFile_LogsErrorWithoutThrowing()
    {
        var dbPath = CreateTempDb();
        File.WriteAllBytes(dbPath, [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07]);
        var logger = Substitute.For<ITraceLogger>();

        new UpgradeDatabaseCommand(logger).UpgradeDatabase(dbPath);

        logger.Received().Error(Arg.Is<ErrorLogHandler>(handler =>
            handler.ToString().Contains("Failed to upgrade database") && handler.ToString().Contains(dbPath)));
    }

    [Fact]
    public void UpgradeDatabase_WithEmptyFile_LogsUnrecognizedSchemaWithoutCreatingTables()
    {
        var dbPath = CreateTempDb();
        File.WriteAllBytes(dbPath, []);
        var logger = Substitute.For<ITraceLogger>();

        new UpgradeDatabaseCommand(logger).UpgradeDatabase(dbPath);

        logger.Received().Error(Arg.Is<ErrorLogHandler>(handler =>
            handler.ToString().Contains("unrecognized schema") && handler.ToString().Contains(dbPath)));

        using var verify = new ProviderDbContext(dbPath, readOnly: true, ensureCreated: false);
        var state = verify.IsUpgradeNeeded();
        Assert.Equal(ProviderDatabaseSchemaVersion.Unknown, state.CurrentVersion);
    }

    [Fact]
    public void UpgradeDatabase_WithMissingFile_LogsErrorAndReturns()
    {
        var missing = DatabaseTestUtils.CreateTempPath();
        var logger = Substitute.For<ITraceLogger>();

        new UpgradeDatabaseCommand(logger).UpgradeDatabase(missing);

        logger.Received().Error(Arg.Is<ErrorLogHandler>(handler =>
            handler.ToString().Contains("File not found") && handler.ToString().Contains(missing)));
    }

    [Fact]
    public void UpgradeDatabase_WithUnknownSchema_LogsErrorWithoutThrowing()
    {
        var dbPath = CreateTempDb();
        DatabaseTestUtils.CreateUnknownShapeDatabase(dbPath);
        var logger = Substitute.For<ITraceLogger>();

        new UpgradeDatabaseCommand(logger).UpgradeDatabase(dbPath);

        logger.Received().Error(Arg.Is<ErrorLogHandler>(handler =>
            handler.ToString().Contains("unrecognized schema") && handler.ToString().Contains(dbPath)));
    }

    [Fact]
    public void UpgradeDatabase_WithV3Database_UpgradesToCurrentSchema()
    {
        var dbPath = CreateTempDb();
        DatabaseTestUtils.CreateV3Database(dbPath);
        var logger = Substitute.For<ITraceLogger>();

        new UpgradeDatabaseCommand(logger).UpgradeDatabase(dbPath);

        using var verify = new ProviderDbContext(dbPath, true);
        var schemaState = verify.IsUpgradeNeeded();
        Assert.False(schemaState.NeedsUpgrade);
        Assert.Equal(ProviderDatabaseSchemaVersion.Current, schemaState.CurrentVersion);
    }

    [Fact]
    public void UpgradeDatabase_WithV4Database_LogsNoUpgradeNeeded()
    {
        var dbPath = CreateTempDb();
        DatabaseTestUtils.CreateV4Database(dbPath);
        var logger = Substitute.For<ITraceLogger>();

        new UpgradeDatabaseCommand(logger).UpgradeDatabase(dbPath);

        logger.Received().Information(Arg.Is<InformationLogHandler>(handler =>
            handler.ToString().Contains("does not need to be upgraded")));
    }

    private string CreateTempDb()
    {
        var path = DatabaseTestUtils.CreateTempPath();
        _tempPaths.Add(path);
        return path;
    }
}
