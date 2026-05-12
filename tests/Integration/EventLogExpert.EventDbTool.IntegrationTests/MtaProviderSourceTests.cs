// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.EventDbTool.IntegrationTests.TestUtils;
using EventLogExpert.Eventing.Logging;
using NSubstitute;

namespace EventLogExpert.EventDbTool.IntegrationTests;

public sealed class MtaProviderSourceTests : IDisposable
{
    private readonly List<string> _tempDirs = [];
    private readonly List<string> _tempFiles = [];

    [Fact]
    public void DiscoverProviderNames_WhenEvtxFileMissing_LogsErrorReturnsEmpty()
    {
        // Arrange
        var missing = DatabaseTestUtils.CreateTempPath(".evtx");
        var logger = Substitute.For<ITraceLogger>();

        // Act
        var providers = MtaProviderSource.DiscoverProviderNames(missing, logger);

        // Assert
        Assert.Empty(providers);
        logger.Received(1).Error(Arg.Is<ErrorLogHandler>(h =>
            h.ToString().Contains("Evtx file not found") && h.ToString().Contains(missing)));
    }

    [Fact]
    public void DiscoverProviderNames_WhenFilterRegexIsInvalid_LogsErrorReturnsEmpty()
    {
        // Arrange
        var missing = DatabaseTestUtils.CreateTempPath(".evtx");
        var logger = Substitute.For<ITraceLogger>();

        // Act — invalid pattern is rejected before the evtx is even opened.
        var providers = MtaProviderSource.DiscoverProviderNames(missing, logger, filter: "[unclosed");

        // Assert
        Assert.Empty(providers);
        logger.Received(1).Error(Arg.Is<ErrorLogHandler>(h =>
            h.ToString().Contains("Invalid --filter regex")));
        // No "Evtx file not found" should be logged because we short-circuited on the bad regex.
        logger.DidNotReceive().Error(Arg.Is<ErrorLogHandler>(h =>
            h.ToString().Contains("Evtx file not found")));
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            DatabaseTestUtils.DeleteDirectoryRecursive(dir);
        }

        foreach (var file in _tempFiles)
        {
            DatabaseTestUtils.DeleteDatabaseFile(file);
        }
    }

    [Fact]
    public void FindMtaFiles_WhenLocaleMetaDataContainsMtaFiles_ReturnsAllOrdinalSortedAndLogsCount()
    {
        // Arrange
        var dir = DatabaseTestUtils.CreateTempDirectory();
        _tempDirs.Add(dir);
        var evtxPath = Path.Combine(dir, "test.evtx");
        File.WriteAllBytes(evtxPath, []);
        var localeDir = Path.Combine(dir, "LocaleMetaData");
        Directory.CreateDirectory(localeDir);

        // Names chosen so non-ordinal sorts (e.g., culture-aware) would reorder them differently.
        var b = Path.Combine(localeDir, "B-Provider.MTA");
        var a = Path.Combine(localeDir, "A-Provider.MTA");
        var c = Path.Combine(localeDir, "C-Provider.MTA");
        File.WriteAllBytes(a, [0x00]);
        File.WriteAllBytes(b, [0x00]);
        File.WriteAllBytes(c, [0x00]);

        var logger = Substitute.For<ITraceLogger>();

        // Act
        var files = MtaProviderSource.FindMtaFiles(evtxPath, logger);

        // Assert — ordinal order ensures consistent provider lookup precedence across locales.
        Assert.Equal(3, files.Count);
        Assert.Equal(a, files[0]);
        Assert.Equal(b, files[1]);
        Assert.Equal(c, files[2]);
        logger.Received(1).Information(Arg.Is<InformationLogHandler>(h =>
            h.ToString().Contains("3 locale metadata file") && h.ToString().Contains(localeDir)));
        logger.DidNotReceive().Error(Arg.Any<ErrorLogHandler>());
    }

    [Fact]
    public void FindMtaFiles_WhenLocaleMetaDataDirectoryIsEmpty_LogsErrorReturnsEmpty()
    {
        // Arrange
        var dir = DatabaseTestUtils.CreateTempDirectory();
        _tempDirs.Add(dir);
        var evtxPath = Path.Combine(dir, "test.evtx");
        File.WriteAllBytes(evtxPath, []);
        var localeDir = Path.Combine(dir, "LocaleMetaData");
        Directory.CreateDirectory(localeDir);
        var logger = Substitute.For<ITraceLogger>();

        // Act
        var files = MtaProviderSource.FindMtaFiles(evtxPath, logger);

        // Assert
        Assert.Empty(files);
        logger.Received(1).Error(Arg.Is<ErrorLogHandler>(h =>
            h.ToString().Contains("contains no MTA files") && h.ToString().Contains(localeDir)));
    }

    [Fact]
    public void FindMtaFiles_WhenLocaleMetaDataDirectoryMissing_LogsErrorReturnsEmpty()
    {
        // Arrange — create a temp dir with an evtx file but NO LocaleMetaData subdir.
        var dir = DatabaseTestUtils.CreateTempDirectory();
        _tempDirs.Add(dir);
        var evtxPath = Path.Combine(dir, "test.evtx");
        File.WriteAllBytes(evtxPath, []);
        var logger = Substitute.For<ITraceLogger>();

        // Act
        var files = MtaProviderSource.FindMtaFiles(evtxPath, logger);

        // Assert
        Assert.Empty(files);
        logger.Received(1).Error(Arg.Is<ErrorLogHandler>(h =>
            h.ToString().Contains("No LocaleMetaData folder") && h.ToString().Contains(evtxPath)));
    }
}
