// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Providers;
using EventLogExpert.Eventing.Tests.TestUtils.Constants;
using NSubstitute;

namespace EventLogExpert.Eventing.Tests.Providers;

public sealed class EventMessageProviderTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(Constants.LocalComputer)]
    [InlineData(Constants.RemoteComputer)]
    public void Constructor_WhenDifferentComputerNames_ShouldCreateInstances(string? computerName)
    {
        // Arrange & Act
        EventMessageProvider provider = new(Constants.TestProviderName, computerName);

        // Assert
        Assert.NotNull(provider);
    }

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
    public void GetMessages_WhenBinaryUsesMuiSatellite_ShouldLoadMessagesFromMuiFile()
    {
        // wevtsvc.dll on Windows keeps its message table in en-US\wevtsvc.dll.mui rather
        // than in the binary itself. The previous load path used LOAD_LIBRARY_AS_DATAFILE
        // with only the leaf filename and therefore failed with ERROR_RESOURCE_TYPE_NOT_FOUND
        // (1813) for binaries like this — including the WMIRegistrationService.exe scenario
        // that motivated this fix. The MUI-aware load path should resolve them.
        const string muiBinary = @"C:\Windows\System32\wevtsvc.dll";

        Assert.SkipUnless(
            File.Exists(muiBinary) && File.Exists(@"C:\Windows\System32\en-US\wevtsvc.dll.mui"),
            "Test requires wevtsvc.dll and its en-US MUI satellite to be present.");

        // Act
        var messages = EventMessageProvider.GetMessages([muiBinary], Constants.TestProviderName);

        // Assert
        Assert.NotNull(messages);
        Assert.NotEmpty(messages);
        Assert.All(messages, m => Assert.Equal(Constants.TestProviderName, m.ProviderName));
    }

    [Fact]
    public void Constructor_WhenProviderNameAndComputerNameProvided_ShouldCreateInstance()
    {
        // Arrange & Act
        EventMessageProvider provider = new(Constants.TestProviderName, Constants.LocalComputer);

        // Assert
        Assert.NotNull(provider);
    }

    [Fact]
    public void GetMessages_WhenDuplicateFiles_ShouldProcessAll()
    {
        // Arrange
        var duplicateFiles = new[] { Constants.NonExistentDll, Constants.NonExistentDll };
        var mockLogger = Substitute.For<ITraceLogger>();

        // Act
        var messages = EventMessageProvider.GetMessages(duplicateFiles, Constants.TestProviderName, mockLogger);

        // Assert
        Assert.NotNull(messages);

        // Each missing file produces two LoadLibraryEx failure logs (MUI-aware attempt and
        // leaf-name fallback). Two files therefore produce four such logs total.
        mockLogger.Received(4)
            .Debug(Arg.Is<DebugLogHandler>(h => h.ToString().Contains("LoadLibraryEx failed")));
    }

    [Fact]
    public void GetMessages_WhenEmptyFileList_ShouldReturnEmptyList()
    {
        // Arrange & Act
        var messages = EventMessageProvider.GetMessages([], Constants.TestProviderName);

        // Assert
        Assert.NotNull(messages);
        Assert.Empty(messages);
    }

    [Fact]
    public void GetMessages_WhenFileListIsNull_ShouldThrowNullReferenceException()
    {
        // Arrange
        var mockLogger = Substitute.For<ITraceLogger>();

        // Act & Assert
        Assert.Throws<NullReferenceException>(() =>
            EventMessageProvider.GetMessages(null!, Constants.TestProviderName, mockLogger));
    }

    [Fact]
    public void GetMessages_WhenFilePathContainsEnvironmentVariable_ShouldHandleCorrectly()
    {
        // Arrange
        var filesWithEnvVar = new[] { Constants.NonExistentDllSystemRootFullPath };

        // Act
        var messages = EventMessageProvider.GetMessages(filesWithEnvVar, Constants.TestProviderName);

        // Assert
        Assert.NotNull(messages);
    }

    [Fact]
    public void GetMessages_WhenFilePathHasMultipleBackslashes_ShouldExtractFileName()
    {
        // Arrange
        var filesWithPath = new[] { Constants.NonExistentDllFullPath };

        // Act
        var messages = EventMessageProvider.GetMessages(filesWithPath, Constants.TestProviderName);

        // Assert
        Assert.NotNull(messages);
    }

    [Fact]
    public void GetMessages_WhenInvalidFile_ShouldLogWarning()
    {
        // Arrange
        var invalidFiles = new[] { Constants.NonExistentDll };
        var mockLogger = Substitute.For<ITraceLogger>();

        // Act
        EventMessageProvider.GetMessages(invalidFiles, Constants.TestProviderName, mockLogger);

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
    public void GetMessages_WhenInvalidFile_ShouldReturnEmptyList()
    {
        // Arrange
        var invalidFiles = new[] { Constants.NonExistentDll };

        // Act
        var messages = EventMessageProvider.GetMessages(invalidFiles, Constants.TestProviderName);

        // Assert
        Assert.NotNull(messages);
        Assert.Empty(messages);
    }

    [Fact]
    public void GetMessages_WhenMultipleInvalidFiles_ShouldLogMultipleWarnings()
    {
        // Arrange
        var invalidFiles = new[] { Constants.NonExistentDll, Constants.NonExistentDll };
        var mockLogger = Substitute.For<ITraceLogger>();

        // Act
        EventMessageProvider.GetMessages(invalidFiles, Constants.TestProviderName, mockLogger);

        // Assert: each missing file produces two LoadLibraryEx failure logs (MUI-aware attempt
        // and leaf-name fallback), so two files produce four logs total.
        mockLogger.Received(4)
            .Debug(Arg.Is<DebugLogHandler>(h => h.ToString().Contains("LoadLibraryEx failed")));
    }

    [Fact]
    public void GetMessages_WhenMultipleInvalidFiles_ShouldReturnEmptyList()
    {
        // Arrange
        var invalidFiles = new[] { Constants.NonExistentDll, Constants.NonExistentDll, Constants.NonExistentDll };

        // Act
        var messages = EventMessageProvider.GetMessages(invalidFiles, Constants.TestProviderName);

        // Assert
        Assert.NotNull(messages);
        Assert.Empty(messages);
    }

    [Fact]
    public void GetMessages_WhenProviderNameProvided_ShouldIncludeInMessages()
    {
        // Arrange & Act
        var messages = EventMessageProvider.GetMessages([], Constants.TestProviderName);

        // Assert
        Assert.NotNull(messages);
    }

    [Fact]
    public void LoadProviderDetails_ShouldLogProviderLoadingAttempt()
    {
        // Arrange
        var mockLogger = Substitute.For<ITraceLogger>();
        EventMessageProvider provider = new(Constants.TestProviderName, logger: mockLogger);

        // Act
        provider.LoadProviderDetails();

        // Assert
        mockLogger.Received().Debug(Arg.Any<DebugLogHandler>());
    }

    [Fact]
    public void LoadProviderDetails_WhenCalled_ShouldHaveNonNullCollections()
    {
        // Arrange
        EventMessageProvider provider = new(Constants.TestProviderName);

        // Act
        var details = provider.LoadProviderDetails();

        // Assert
        Assert.NotNull(details);
        Assert.NotNull(details.Events);
        Assert.NotNull(details.Keywords);
        Assert.NotNull(details.Opcodes);
        Assert.NotNull(details.Tasks);
        Assert.NotNull(details.Messages);
        Assert.NotNull(details.Parameters);
    }

    [Fact]
    public void LoadProviderDetails_WhenCalled_ShouldReturnProviderDetails()
    {
        // Arrange
        EventMessageProvider provider = new(Constants.TestProviderName);

        // Act
        var details = provider.LoadProviderDetails();

        // Assert
        Assert.NotNull(details);
        Assert.Equal(Constants.TestProviderName, details.ProviderName);
    }

    [Fact]
    public void LoadProviderDetails_WhenCalledMultipleTimes_ShouldReturnConsistentResults()
    {
        // Arrange
        EventMessageProvider provider = new(Constants.TestProviderName);

        // Act
        var details1 = provider.LoadProviderDetails();
        var details2 = provider.LoadProviderDetails();

        // Assert
        Assert.NotNull(details1);
        Assert.NotNull(details2);
        Assert.Equal(details1.ProviderName, details2.ProviderName);
    }

    [Fact]
    public void LoadProviderDetails_WhenProviderHasNoData_ShouldReturnEmptyCollections()
    {
        // Arrange
        EventMessageProvider provider = new(Constants.TestProviderName);

        // Act
        var details = provider.LoadProviderDetails();

        // Assert
        Assert.NotNull(details);
        Assert.Empty(details.Messages);
        Assert.Empty(details.Parameters);
    }

    [Fact]
    public void LoadProviderDetails_WhenProviderNotFound_ShouldReturnDetailsWithProviderName()
    {
        // Arrange
        EventMessageProvider provider = new(Constants.TestProviderName);

        // Act
        var details = provider.LoadProviderDetails();

        // Assert
        Assert.NotNull(details);
        Assert.Equal(Constants.TestProviderName, details.ProviderName);
    }
}
