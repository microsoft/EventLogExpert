// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Sources;
using EventLogExpert.Eventing.TestUtils;
using EventLogExpert.Eventing.TestUtils.Constants;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using NSubstitute;

namespace EventLogExpert.DatabaseTools.IntegrationTests.Sources;

public sealed class ProviderSourceTests : IDisposable
{
    private readonly List<string> _tempDirectories = [];
    private readonly List<string> _tempPaths = [];

    public void Dispose()
    {
        foreach (var path in _tempPaths)
        {
            DatabaseTestUtils.DeleteDatabaseFile(path);
        }

        foreach (var dir in _tempDirectories)
        {
            DatabaseTestUtils.DeleteDirectoryRecursive(dir);
        }
    }

    [Fact]
    public void ValidateSourceSchemas_WithCurrentSchemaDatabase_ReturnsTrue()
    {
        // Arrange
        var dbPath = CreateTempDb();
        DatabaseTestUtils.CreateV4Database(dbPath, DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName));
        var logger = Substitute.For<ITraceLogger>();

        // Act
        var result = ProviderSource.ValidateSourceSchemas(dbPath, logger);

        // Assert
        Assert.True(result);
        logger.DidNotReceive().Error(Arg.Any<ErrorLogHandler>());
    }

    [Fact]
    public void ValidateSourceSchemas_WithDirectoryContainingAnyInvalidDatabase_ReturnsFalse()
    {
        // Arrange — one valid, one Unknown shape. The whole directory must fail.
        var dir = CreateTempDir();
        var good = Path.Combine(dir, "good.db");
        var bad = Path.Combine(dir, "bad.db");
        DatabaseTestUtils.CreateV4Database(good, DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName));
        DatabaseTestUtils.CreateUnknownShapeDatabase(bad);
        var logger = Substitute.For<ITraceLogger>();

        // Act
        var result = ProviderSource.ValidateSourceSchemas(dir, logger);

        // Assert
        Assert.False(result);
        logger.Received(1).Error(Arg.Is<ErrorLogHandler>(handler =>
            handler.ToString().Contains("unrecognized schema") &&
            handler.ToString().Contains(bad)));
    }

    [Fact]
    public void ValidateSourceSchemas_WithDirectoryMixingDbAndEvtx_OnlyValidatesDb()
    {
        // Arrange — only the .db files participate in schema validation; the evtx is ignored.
        var dir = CreateTempDir();
        var dbPath = Path.Combine(dir, "valid.db");
        var evtxPath = Path.Combine(dir, "ignored.evtx");
        DatabaseTestUtils.CreateV4Database(dbPath, DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName));
        File.WriteAllBytes(evtxPath, new byte[] { 0x00 });
        var logger = Substitute.For<ITraceLogger>();

        // Act
        var result = ProviderSource.ValidateSourceSchemas(dir, logger);

        // Assert
        Assert.True(result);
        logger.DidNotReceive().Error(Arg.Any<ErrorLogHandler>());
    }

    [Fact]
    public void ValidateSourceSchemas_WithDirectoryOfValidDatabases_ReturnsTrue()
    {
        // Arrange
        var dir = CreateTempDir();
        var first = Path.Combine(dir, "first.db");
        var second = Path.Combine(dir, "second.db");
        DatabaseTestUtils.CreateV4Database(first, DatabaseTestUtils.BuildProviderDetails(Constants.FirstProviderName));
        DatabaseTestUtils.CreateV4Database(second, DatabaseTestUtils.BuildProviderDetails(Constants.SecondProviderName));
        var logger = Substitute.For<ITraceLogger>();

        // Act
        var result = ProviderSource.ValidateSourceSchemas(dir, logger);

        // Assert
        Assert.True(result);
        logger.DidNotReceive().Error(Arg.Any<ErrorLogHandler>());
    }

    [Fact]
    public void ValidateSourceSchemas_WithEvtxFile_IsSkipped()
    {
        // Arrange — .evtx files have no schema concept; ValidateSourceSchemas should ignore them
        // entirely (the evtx-specific load path goes through MtaProviderSource elsewhere).
        var evtxPath = CreateTempPath(".evtx");
        File.WriteAllBytes(evtxPath, new byte[] { 0x00 });
        var logger = Substitute.For<ITraceLogger>();

        // Act
        var result = ProviderSource.ValidateSourceSchemas(evtxPath, logger);

        // Assert
        Assert.True(result);
        logger.DidNotReceive().Error(Arg.Any<ErrorLogHandler>());
    }

    [Fact]
    public void ValidateSourceSchemas_WithObsoleteV3Database_ReturnsFalseAndLogsError()
    {
        // Arrange
        var dbPath = CreateTempDb();
        DatabaseTestUtils.CreateV3Database(dbPath);
        var logger = Substitute.For<ITraceLogger>();

        // Act
        var result = ProviderSource.ValidateSourceSchemas(dbPath, logger);

        // Assert
        Assert.False(result);
        logger.Received(1).Error(Arg.Is<ErrorLogHandler>(handler =>
            handler.ToString().Contains("schema v3") &&
            handler.ToString().Contains(dbPath) &&
            handler.ToString().Contains("upgrade")));
    }

    [Fact]
    public void ValidateSourceSchemas_WithUnknownSchemaDatabase_ReturnsFalseAndLogsError()
    {
        // Arrange
        var dbPath = CreateTempDb();
        DatabaseTestUtils.CreateUnknownShapeDatabase(dbPath);
        var logger = Substitute.For<ITraceLogger>();

        // Act
        var result = ProviderSource.ValidateSourceSchemas(dbPath, logger);

        // Assert
        Assert.False(result);
        logger.Received(1).Error(Arg.Is<ErrorLogHandler>(handler =>
            handler.ToString().Contains("unrecognized schema") &&
            handler.ToString().Contains(dbPath)));
    }

    private string CreateTempDb()
    {
        var path = DatabaseTestUtils.CreateTempPath();
        _tempPaths.Add(path);
        return path;
    }

    private string CreateTempDir()
    {
        var dir = DatabaseTestUtils.CreateTempDirectory();
        _tempDirectories.Add(dir);
        return dir;
    }

    private string CreateTempPath(string extension)
    {
        var path = DatabaseTestUtils.CreateTempPath(extension);
        _tempPaths.Add(path);
        return path;
    }
}
