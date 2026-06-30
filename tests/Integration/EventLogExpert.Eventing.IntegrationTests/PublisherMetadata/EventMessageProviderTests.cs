// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata;
using EventLogExpert.Eventing.TestUtils.Constants;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using NSubstitute;

namespace EventLogExpert.Eventing.IntegrationTests.PublisherMetadata;

public sealed class EventMessageProviderTests
{
    [Fact]
    public void LoadMessagesFromFiles_WhenDuplicateFiles_ShouldProcessAll()
    {
        var duplicateFiles = new[] { Constants.NonExistentDll, Constants.NonExistentDll };
        var mockLogger = Substitute.For<ITraceLogger>();

        var messages = EventMessageProvider.LoadMessagesFromFiles(duplicateFiles, Constants.TestProviderName, mockLogger);

        Assert.NotNull(messages);

        // Only the primary-attempt failure log is guaranteed once per input.
        mockLogger.Received(duplicateFiles.Length)
            .Debug(Arg.Is<DebugLogHandler>(h =>
                h.ToString().Contains("LoadLibraryEx failed") &&
                h.ToString().Contains(Constants.NonExistentDll)));
    }

    [Fact]
    public void LoadMessagesFromFiles_WhenEmptyFileList_ShouldReturnEmptyList()
    {
        var messages = EventMessageProvider.LoadMessagesFromFiles([], Constants.TestProviderName);

        Assert.NotNull(messages);
        Assert.Empty(messages);
    }

    [Fact]
    public void LoadMessagesFromFiles_WhenFileListIsNull_ShouldThrowArgumentNullException()
    {
        var mockLogger = Substitute.For<ITraceLogger>();

        Assert.Throws<ArgumentNullException>(() =>
            EventMessageProvider.LoadMessagesFromFiles(null!, Constants.TestProviderName, mockLogger));
    }

    [Fact]
    public void LoadMessagesFromFiles_WhenInvalidFile_ShouldLogWarning()
    {
        var invalidFiles = new[] { Constants.NonExistentDll };
        var mockLogger = Substitute.For<ITraceLogger>();

        EventMessageProvider.LoadMessagesFromFiles(invalidFiles, Constants.TestProviderName, mockLogger);

        mockLogger.Received()
            .Debug(Arg.Is<DebugLogHandler>(h =>
                h.ToString().Contains("LoadLibraryEx failed") &&
                h.ToString().Contains("LOAD_LIBRARY_AS_IMAGE_RESOURCE")));

        mockLogger.Received()
            .Debug(Arg.Is<DebugLogHandler>(h =>
                h.ToString().Contains("LoadLibraryEx failed") &&
                h.ToString().Contains("leaf-name fallback")));
    }

    [Fact]
    public void LoadMessagesFromFiles_WhenInvalidFile_ShouldReturnEmptyList()
    {
        var invalidFiles = new[] { Constants.NonExistentDll };

        var messages = EventMessageProvider.LoadMessagesFromFiles(invalidFiles, Constants.TestProviderName);

        Assert.NotNull(messages);
        Assert.Empty(messages);
    }
}
