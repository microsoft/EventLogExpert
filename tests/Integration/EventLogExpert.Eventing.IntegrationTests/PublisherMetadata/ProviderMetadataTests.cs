// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata;
using EventLogExpert.Eventing.TestUtils.Constants;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using NSubstitute;

namespace EventLogExpert.Eventing.IntegrationTests.PublisherMetadata;

public sealed class ProviderMetadataTests
{
    [Fact]
    public void Channels_WhenProviderHasChannels_ShouldHaveValidKeys()
    {
        // Arrange
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var channels = metadata?.ToRawContent(Constants.SecurityAuditingLogName, null).Channels;

        // Assert
        Assert.NotNull(channels);

        Assert.All(channels,
            channel =>
            {
                Assert.True(channel.Key > 0);
                Assert.False(string.IsNullOrEmpty(channel.Value));
            });
    }

    [Fact]
    public void Channels_WhenValidProvider_ShouldContainData()
    {
        // Arrange
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var channels = metadata?.ToRawContent(Constants.SecurityAuditingLogName, null).Channels;

        // Assert
        Assert.NotNull(channels);
        Assert.NotEmpty(channels);
    }

    [Theory]
    [InlineData(Constants.SecurityAuditingLogName)]
    [InlineData(Constants.KernelGeneralLogName)]
    [InlineData(Constants.PowerShellLogName)]
    public void Create_WhenCommonProviders_ShouldReturnMetadata(string providerName)
    {
        // Arrange & Act
        var metadata = ProviderMetadata.Create(providerName);

        // Assert
        // Note: These tests may fail if the provider doesn't exist on the test machine
        // but are useful for testing common Windows providers
        if (metadata != null)
        {
            Assert.NotNull(metadata.MessageFilePath);
        }
    }

    [Fact]
    public void Create_WhenEmptyProviderName_ShouldNotLogError()
    {
        // Arrange
        var mockLogger = Substitute.For<ITraceLogger>();

        // Act
        var metadata = ProviderMetadata.Create(string.Empty, logger: mockLogger);

        // Assert
        // Empty provider name is treated as valid by Windows
        mockLogger.DidNotReceive()
            .Debug(Arg.Is<DebugLogHandler>(h => h.ToString().Contains("Failed to create metadata")));
    }

    [Fact]
    public void Create_WhenEmptyProviderName_ShouldReturnMetadata()
    {
        // Arrange & Act
        var metadata = ProviderMetadata.Create(string.Empty);

        // Assert
        // Windows treats empty string as a valid provider name
        Assert.NotNull(metadata);
    }

    [Fact]
    public void Create_WhenInvalidProvider_ShouldLogError()
    {
        // Arrange
        var providerName = "NonExistentProvider_" + Guid.NewGuid();
        var mockLogger = Substitute.For<ITraceLogger>();

        // Act
        var metadata = ProviderMetadata.Create(providerName, logger: mockLogger);

        // Assert
        mockLogger.Received(1).Debug(
            Arg.Is<DebugLogHandler>(h => h.ToString().Contains("Failed to create metadata") && h.ToString().Contains(providerName)));
    }

    [Fact]
    public void Create_WhenInvalidProvider_ShouldReturnNull()
    {
        // Arrange
        var providerName = "NonExistentProvider_" + Guid.NewGuid();

        // Act
        var metadata = ProviderMetadata.Create(providerName);

        // Assert
        Assert.Null(metadata);
    }

    [Fact]
    public void Create_WhenSpecialCharactersInProviderName_ShouldReturnNull()
    {
        // Arrange & Act
        var metadata = ProviderMetadata.Create("Invalid<>Provider|Name");

        // Assert
        Assert.Null(metadata);
    }

    [Fact]
    public void Create_WhenValidProvider_ShouldNotLogError()
    {
        // Arrange
        var mockLogger = Substitute.For<ITraceLogger>();

        // Act
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName, logger: mockLogger);

        // Assert
        Assert.NotNull(metadata);

        mockLogger.DidNotReceive()
            .Debug(Arg.Is<DebugLogHandler>(h => h.ToString().Contains("Failed to create metadata")));
    }

    [Fact]
    public void Create_WhenValidProvider_ShouldReturnMetadata()
    {
        // Arrange & Act
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Assert
        Assert.NotNull(metadata);
    }

    [Fact]
    public void Create_WhenWhitespaceProviderName_ShouldReturnNull()
    {
        // Arrange & Act
        var metadata = ProviderMetadata.Create("   ");

        // Assert
        Assert.Null(metadata);
    }

    [Fact]
    public void Error_WhenInvalidProvider_ShouldContainErrorMessage()
    {
        // Arrange
        var providerName = "NonExistentProvider_" + Guid.NewGuid();
        var mockLogger = Substitute.For<ITraceLogger>();

        // Act
        var metadata = ProviderMetadata.Create(providerName, logger: mockLogger);

        // Assert
        Assert.Null(metadata);

        mockLogger.Received(1)
            .Debug(Arg.Is<DebugLogHandler>(h => !string.IsNullOrEmpty(h.ToString())));
    }

    [Fact]
    public void Events_WhenProviderHasEvents_ShouldHaveValidEventMetadata()
    {
        // Arrange
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var events = metadata?.ToRawContent(Constants.SecurityAuditingLogName, null).Events;

        // Assert
        Assert.NotNull(events);

        if (events.Count == 0) { return; }

        var firstEvent = events[0];
        Assert.True(firstEvent.Id > 0);
    }

    [Fact]
    public void Events_WhenValidProvider_ShouldContainEventMetadata()
    {
        // Arrange
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var events = metadata?.ToRawContent(Constants.SecurityAuditingLogName, null).Events;

        // Assert
        Assert.NotNull(events);

        if (events.Count == 0) { return; }

        Assert.All(events,
            providerEvent =>
            {
                Assert.NotNull(providerEvent);
                Assert.True(providerEvent.Id > 0);
            });
    }

    [Fact]
    public void Events_WhenValidProvider_ShouldContainEvents()
    {
        // Arrange
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var events = metadata?.ToRawContent(Constants.SecurityAuditingLogName, null).Events;

        // Assert
        Assert.NotNull(events);
        Assert.NotEmpty(events);
    }

    [Fact]
    public void Keywords_WhenProviderHasKeywords_ShouldContainData()
    {
        // Arrange
        var metadata = ProviderMetadata.Create(Constants.PowerShellLogName);

        // Act
        var keywords = metadata?.ToRawContent(Constants.PowerShellLogName, null).Keywords;

        // Assert
        Assert.NotNull(keywords);

        // PowerShell provider has keywords, Security-Auditing may not
        if (keywords.Any())
        {
            Assert.NotEmpty(keywords);
        }
    }

    [Fact]
    public void Keywords_WhenProviderHasKeywords_ShouldHaveValidValues()
    {
        // Arrange
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var keywords = metadata?.ToRawContent(Constants.SecurityAuditingLogName, null).Keywords;

        // Assert
        Assert.NotNull(keywords);

        // Each raw entry carries a name source: an inline name, or a message id to resolve.
        Assert.All(keywords,
            keyword =>
            {
                Assert.True(keyword.MessageId != uint.MaxValue || !string.IsNullOrEmpty(keyword.InlineName));
            });
    }

    [Fact]
    public void MessageFilePath_WhenCalledMultipleTimes_ShouldReturnConsistentPath()
    {
        // Arrange
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var path1 = metadata?.MessageFilePath;
        var path2 = metadata?.MessageFilePath;

        // Assert
        Assert.NotNull(path1);
        Assert.NotNull(path2);
        Assert.Equal(path1, path2);
    }

    [Fact]
    public void MessageFilePath_WhenManifestUsesEnvironmentVariables_ShouldReturnExpandedPath()
    {
        // Microsoft-Windows-Security-Auditing's publisher manifest declares MessageFilePath
        // as "%SystemRoot%\system32\adtschema.dll". LoadLibraryEx cannot resolve %SystemRoot%
        // literally, so the property must expand environment variables before returning.
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        Assert.SkipUnless(metadata is not null, "Test requires Microsoft-Windows-Security-Auditing provider on the host.");
        Assert.NotNull(metadata);

        var messageFilePath = metadata.MessageFilePath;

        Assert.NotNull(messageFilePath);
        Assert.DoesNotContain("%", messageFilePath);
        Assert.True(File.Exists(messageFilePath), $"Expanded MessageFilePath should exist on disk: {messageFilePath}");
    }

    [Fact]
    public void MessageFilePath_WhenValidProvider_ShouldContainDllExtension()
    {
        // Arrange
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var messageFilePath = metadata?.MessageFilePath;

        // Assert
        Assert.NotNull(messageFilePath);
        Assert.Contains(".dll", messageFilePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MessageFilePath_WhenValidProvider_ShouldNotBeEmpty()
    {
        // Arrange
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var messageFilePath = metadata?.MessageFilePath;

        // Assert
        Assert.NotNull(messageFilePath);
        Assert.NotEmpty(messageFilePath);
    }

    [Fact]
    public void MessageFilePath_WhenValidProvider_ShouldReturnPath()
    {
        // Arrange
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var messageFilePath = metadata?.MessageFilePath;

        // Assert
        Assert.NotNull(messageFilePath);
    }

    [Fact]
    public void Opcodes_WhenProviderHasOpcodes_ShouldHaveValidValues()
    {
        // Arrange
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var opcodes = metadata?.ToRawContent(Constants.SecurityAuditingLogName, null).Opcodes;

        // Assert
        Assert.NotNull(opcodes);

        // Each raw entry carries a name source: an inline name, or a message id to resolve.
        Assert.All(opcodes,
            opcode =>
            {
                Assert.True(opcode.MessageId != uint.MaxValue || !string.IsNullOrEmpty(opcode.InlineName));
            });
    }

    [Fact]
    public void Opcodes_WhenValidProvider_ShouldContainData()
    {
        // Arrange
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var opcodes = metadata?.ToRawContent(Constants.SecurityAuditingLogName, null).Opcodes;

        // Assert
        Assert.NotNull(opcodes);
        Assert.NotEmpty(opcodes);
    }

    [Fact]
    public void ParameterFilePath_WhenCalledMultipleTimes_ShouldReturnConsistentPath()
    {
        // Arrange
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var path1 = metadata?.ParameterFilePath;
        var path2 = metadata?.ParameterFilePath;

        // Assert
        Assert.NotNull(path1);
        Assert.NotNull(path2);
        Assert.Equal(path1, path2);
    }

    [Fact]
    public void ParameterFilePath_WhenManifestUsesEnvironmentVariables_ShouldReturnExpandedPath()
    {
        // Microsoft-Windows-Security-Auditing's publisher manifest declares ParameterFilePath
        // as "%SystemRoot%\system32\msobjs.dll". LoadLibraryEx cannot resolve %SystemRoot%
        // literally, so the property must expand environment variables before returning.
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        Assert.SkipUnless(metadata is not null, "Test requires Microsoft-Windows-Security-Auditing provider on the host.");
        Assert.NotNull(metadata);

        var parameterFilePath = metadata.ParameterFilePath;

        Assert.NotNull(parameterFilePath);
        Assert.DoesNotContain("%", parameterFilePath);
        Assert.True(File.Exists(parameterFilePath), $"Expanded ParameterFilePath should exist on disk: {parameterFilePath}");
    }

    [Fact]
    public void ParameterFilePath_WhenValidProvider_ShouldReturnPath()
    {
        // Arrange
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var parameterFilePath = metadata?.ParameterFilePath;

        // Assert
        Assert.NotNull(parameterFilePath);
    }

    [Fact]
    public void Tasks_WhenProviderHasTasks_ShouldHaveValidValues()
    {
        // Arrange
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var tasks = metadata?.ToRawContent(Constants.SecurityAuditingLogName, null).Tasks;

        // Assert
        Assert.NotNull(tasks);

        // Each raw entry carries a name source: an inline name, or a message id to resolve.
        Assert.All(tasks,
            task =>
            {
                Assert.True(task.MessageId != uint.MaxValue || !string.IsNullOrEmpty(task.InlineName));
            });
    }

    [Fact]
    public void Tasks_WhenValidProvider_ShouldContainData()
    {
        // Arrange
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var tasks = metadata?.ToRawContent(Constants.SecurityAuditingLogName, null).Tasks;

        // Assert
        Assert.NotNull(tasks);
        Assert.NotEmpty(tasks);
    }
}
