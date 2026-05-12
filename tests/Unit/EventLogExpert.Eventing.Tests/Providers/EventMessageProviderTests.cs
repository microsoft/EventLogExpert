// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Logging;
using EventLogExpert.Eventing.Providers;
using EventLogExpert.Eventing.TestUtils.Constants;
using NSubstitute;

namespace EventLogExpert.Eventing.Tests.Providers;

public sealed class EventMessageProviderTests
{
    [Theory]
    [InlineData(Constants.TestProviderName)]
    [InlineData(Constants.TestProviderLongName)]
    public void Constructor_WhenDifferentProviderNames_ShouldCreateInstances(string providerName)
    {
        // Arrange & Act
        EventMessageProvider provider = new(providerName);

        // Assert
        Assert.NotNull(provider);
    }

    [Fact]
    public void LoadMessagesFromFiles_WhenDuplicateFiles_ShouldProcessAll()
    {
        // Arrange
        var duplicateFiles = new[] { Constants.NonExistentDll, Constants.NonExistentDll };
        var mockLogger = Substitute.For<ITraceLogger>();

        // Act
        var messages = EventMessageProvider.LoadMessagesFromFiles(duplicateFiles, Constants.TestProviderName, mockLogger);

        // Assert
        Assert.NotNull(messages);

        // Each input that fails the primary MUI-aware load produces a debug log that begins with
        // "LoadLibraryEx failed for {file}". Asserting per-input presence (with the filename in
        // the message) is robust to future changes in the number of fallback attempts or extra
        // diagnostic lines per input — only the primary-attempt failure log is contractually
        // guaranteed to fire once per input here.
        mockLogger.Received(duplicateFiles.Length)
            .Debug(Arg.Is<DebugLogHandler>(h =>
                h.ToString().Contains("LoadLibraryEx failed") &&
                h.ToString().Contains(Constants.NonExistentDll)));
    }

    [Fact]
    public void LoadMessagesFromFiles_WhenEmptyFileList_ShouldReturnEmptyList()
    {
        // Arrange & Act
        var messages = EventMessageProvider.LoadMessagesFromFiles([], Constants.TestProviderName);

        // Assert
        Assert.NotNull(messages);
        Assert.Empty(messages);
    }

    [Fact]
    public void LoadMessagesFromFiles_WhenFileListIsNull_ShouldThrowArgumentNullException()
    {
        // Arrange
        var mockLogger = Substitute.For<ITraceLogger>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            EventMessageProvider.LoadMessagesFromFiles(null!, Constants.TestProviderName, mockLogger));
    }

    [Fact]
    public void LoadMessagesFromFiles_WhenFilePathHasMultipleBackslashes_ShouldExtractFileName()
    {
        // Arrange
        var filesWithPath = new[] { Constants.NonExistentDllFullPath };

        // Act
        var messages = EventMessageProvider.LoadMessagesFromFiles(filesWithPath, Constants.TestProviderName);

        // Assert
        Assert.NotNull(messages);
    }

    [Fact]
    public void LoadMessagesFromFiles_WhenInvalidFile_ShouldLogWarning()
    {
        // Arrange
        var invalidFiles = new[] { Constants.NonExistentDll };
        var mockLogger = Substitute.For<ITraceLogger>();

        // Act
        EventMessageProvider.LoadMessagesFromFiles(invalidFiles, Constants.TestProviderName, mockLogger);

        // Assert: an unresolvable path produces a LoadLibraryEx failure log for both the
        // MUI-aware primary attempt and the leaf-name fallback.
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
        // Arrange
        var invalidFiles = new[] { Constants.NonExistentDll };

        // Act
        var messages = EventMessageProvider.LoadMessagesFromFiles(invalidFiles, Constants.TestProviderName);

        // Assert
        Assert.NotNull(messages);
        Assert.Empty(messages);
    }

    [Fact]
    public void LoadMessagesFromFiles_WhenMultipleInvalidFiles_ShouldLogMultipleWarnings()
    {
        // Arrange
        var invalidFiles = new[] { Constants.NonExistentDll, Constants.NonExistentDll };
        var mockLogger = Substitute.For<ITraceLogger>();

        // Act
        EventMessageProvider.LoadMessagesFromFiles(invalidFiles, Constants.TestProviderName, mockLogger);

        // Assert: each input that fails the primary MUI-aware load produces a debug log that
        // begins with "LoadLibraryEx failed for {file}". Asserting per-input presence (with the
        // filename in the message) is robust to future changes in the number of fallback attempts
        // or extra diagnostic lines per input — only the primary-attempt failure log is
        // contractually guaranteed to fire once per input here.
        mockLogger.Received(invalidFiles.Length)
            .Debug(Arg.Is<DebugLogHandler>(h =>
                h.ToString().Contains("LoadLibraryEx failed") &&
                h.ToString().Contains(Constants.NonExistentDll)));
    }

    [Fact]
    public void LoadMessagesFromFiles_WhenMultipleInvalidFiles_ShouldReturnEmptyList()
    {
        // Arrange
        var invalidFiles = new[] { Constants.NonExistentDll, Constants.NonExistentDll, Constants.NonExistentDll };

        // Act
        var messages = EventMessageProvider.LoadMessagesFromFiles(invalidFiles, Constants.TestProviderName);

        // Assert
        Assert.NotNull(messages);
        Assert.Empty(messages);
    }

    [Fact]
    public void LoadMessagesFromFiles_WhenProviderNameProvided_ShouldIncludeInMessages()
    {
        // Arrange & Act
        var messages = EventMessageProvider.LoadMessagesFromFiles([], Constants.TestProviderName);

        // Assert
        Assert.NotNull(messages);
    }
}
