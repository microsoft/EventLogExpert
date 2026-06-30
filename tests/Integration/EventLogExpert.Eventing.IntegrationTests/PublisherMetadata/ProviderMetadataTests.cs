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
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        var channels = metadata?.ToRawContent(Constants.SecurityAuditingLogName, null).Channels;

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
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        var channels = metadata?.ToRawContent(Constants.SecurityAuditingLogName, null).Channels;

        Assert.NotNull(channels);
        Assert.NotEmpty(channels);
    }

    [Theory]
    [InlineData(Constants.SecurityAuditingLogName)]
    [InlineData(Constants.KernelGeneralLogName)]
    [InlineData(Constants.PowerShellLogName)]
    public void Create_WhenCommonProviders_ShouldReturnMetadata(string providerName)
    {
        var metadata = ProviderMetadata.Create(providerName);

        if (metadata != null)
        {
            Assert.NotNull(metadata.MessageFilePath);
        }
    }

    [Fact]
    public void Create_WhenEmptyProviderName_ShouldNotLogError()
    {
        var mockLogger = Substitute.For<ITraceLogger>();

        var metadata = ProviderMetadata.Create(string.Empty, logger: mockLogger);

        mockLogger.DidNotReceive()
            .Debug(Arg.Is<DebugLogHandler>(h => h.ToString().Contains("Failed to create metadata")));
    }

    [Fact]
    public void Create_WhenEmptyProviderName_ShouldReturnMetadata()
    {
        var metadata = ProviderMetadata.Create(string.Empty);

        Assert.NotNull(metadata);
    }

    [Fact]
    public void Create_WhenInvalidProvider_ShouldLogError()
    {
        var providerName = "NonExistentProvider_" + Guid.NewGuid();
        var mockLogger = Substitute.For<ITraceLogger>();

        var metadata = ProviderMetadata.Create(providerName, logger: mockLogger);

        mockLogger.Received(1).Debug(
            Arg.Is<DebugLogHandler>(h => h.ToString().Contains("Failed to create metadata") && h.ToString().Contains(providerName)));
    }

    [Fact]
    public void Create_WhenInvalidProvider_ShouldReturnNull()
    {
        var providerName = "NonExistentProvider_" + Guid.NewGuid();

        var metadata = ProviderMetadata.Create(providerName);

        Assert.Null(metadata);
    }

    [Fact]
    public void Create_WhenSpecialCharactersInProviderName_ShouldReturnNull()
    {
        var metadata = ProviderMetadata.Create("Invalid<>Provider|Name");

        Assert.Null(metadata);
    }

    [Fact]
    public void Create_WhenValidProvider_ShouldNotLogError()
    {
        var mockLogger = Substitute.For<ITraceLogger>();

        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName, logger: mockLogger);

        Assert.NotNull(metadata);

        mockLogger.DidNotReceive()
            .Debug(Arg.Is<DebugLogHandler>(h => h.ToString().Contains("Failed to create metadata")));
    }

    [Fact]
    public void Create_WhenValidProvider_ShouldReturnMetadata()
    {
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        Assert.NotNull(metadata);
    }

    [Fact]
    public void Create_WhenWhitespaceProviderName_ShouldReturnNull()
    {
        var metadata = ProviderMetadata.Create("   ");

        Assert.Null(metadata);
    }

    [Fact]
    public void Error_WhenInvalidProvider_ShouldContainErrorMessage()
    {
        var providerName = "NonExistentProvider_" + Guid.NewGuid();
        var mockLogger = Substitute.For<ITraceLogger>();

        var metadata = ProviderMetadata.Create(providerName, logger: mockLogger);

        Assert.Null(metadata);

        mockLogger.Received(1)
            .Debug(Arg.Is<DebugLogHandler>(h => !string.IsNullOrEmpty(h.ToString())));
    }

    [Fact]
    public void Events_WhenProviderHasEvents_ShouldHaveValidEventMetadata()
    {
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        var events = metadata?.ToRawContent(Constants.SecurityAuditingLogName, null).Events;

        Assert.NotNull(events);

        if (events.Count == 0) { return; }

        var firstEvent = events[0];
        Assert.True(firstEvent.Id > 0);
    }

    [Fact]
    public void Events_WhenValidProvider_ShouldContainEventMetadata()
    {
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        var events = metadata?.ToRawContent(Constants.SecurityAuditingLogName, null).Events;

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
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        var events = metadata?.ToRawContent(Constants.SecurityAuditingLogName, null).Events;

        Assert.NotNull(events);
        Assert.NotEmpty(events);
    }

    [Fact]
    public void Keywords_WhenProviderHasKeywords_ShouldContainData()
    {
        var metadata = ProviderMetadata.Create(Constants.PowerShellLogName);

        var keywords = metadata?.ToRawContent(Constants.PowerShellLogName, null).Keywords;

        Assert.NotNull(keywords);

        if (keywords.Any())
        {
            Assert.NotEmpty(keywords);
        }
    }

    [Fact]
    public void Keywords_WhenProviderHasKeywords_ShouldHaveValidValues()
    {
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        var keywords = metadata?.ToRawContent(Constants.SecurityAuditingLogName, null).Keywords;

        Assert.NotNull(keywords);

        Assert.All(keywords,
            keyword =>
            {
                Assert.True(keyword.MessageId != uint.MaxValue || !string.IsNullOrEmpty(keyword.InlineName));
            });
    }

    [Fact]
    public void MessageFilePath_WhenCalledMultipleTimes_ShouldReturnConsistentPath()
    {
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        var path1 = metadata?.MessageFilePath;
        var path2 = metadata?.MessageFilePath;

        Assert.NotNull(path1);
        Assert.NotNull(path2);
        Assert.Equal(path1, path2);
    }

    [Fact]
    public void MessageFilePath_WhenManifestUsesEnvironmentVariables_ShouldReturnExpandedPath()
    {
        // Host manifests can store environment variables that must expand before LoadLibraryEx.
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
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        var messageFilePath = metadata?.MessageFilePath;

        Assert.NotNull(messageFilePath);
        Assert.Contains(".dll", messageFilePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MessageFilePath_WhenValidProvider_ShouldNotBeEmpty()
    {
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        var messageFilePath = metadata?.MessageFilePath;

        Assert.NotNull(messageFilePath);
        Assert.NotEmpty(messageFilePath);
    }

    [Fact]
    public void MessageFilePath_WhenValidProvider_ShouldReturnPath()
    {
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        var messageFilePath = metadata?.MessageFilePath;

        Assert.NotNull(messageFilePath);
    }

    [Fact]
    public void Opcodes_WhenProviderHasOpcodes_ShouldHaveValidValues()
    {
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        var opcodes = metadata?.ToRawContent(Constants.SecurityAuditingLogName, null).Opcodes;

        Assert.NotNull(opcodes);

        Assert.All(opcodes,
            opcode =>
            {
                Assert.True(opcode.MessageId != uint.MaxValue || !string.IsNullOrEmpty(opcode.InlineName));
            });
    }

    [Fact]
    public void Opcodes_WhenValidProvider_ShouldContainData()
    {
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        var opcodes = metadata?.ToRawContent(Constants.SecurityAuditingLogName, null).Opcodes;

        Assert.NotNull(opcodes);
        Assert.NotEmpty(opcodes);
    }

    [Fact]
    public void ParameterFilePath_WhenCalledMultipleTimes_ShouldReturnConsistentPath()
    {
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        var path1 = metadata?.ParameterFilePath;
        var path2 = metadata?.ParameterFilePath;

        Assert.NotNull(path1);
        Assert.NotNull(path2);
        Assert.Equal(path1, path2);
    }

    [Fact]
    public void ParameterFilePath_WhenManifestUsesEnvironmentVariables_ShouldReturnExpandedPath()
    {
        // Host manifests can store environment variables that must expand before LoadLibraryEx.
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
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        var parameterFilePath = metadata?.ParameterFilePath;

        Assert.NotNull(parameterFilePath);
    }

    [Fact]
    public void Tasks_WhenProviderHasTasks_ShouldHaveValidValues()
    {
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        var tasks = metadata?.ToRawContent(Constants.SecurityAuditingLogName, null).Tasks;

        Assert.NotNull(tasks);

        Assert.All(tasks,
            task =>
            {
                Assert.True(task.MessageId != uint.MaxValue || !string.IsNullOrEmpty(task.InlineName));
            });
    }

    [Fact]
    public void Tasks_WhenValidProvider_ShouldContainData()
    {
        var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        var tasks = metadata?.ToRawContent(Constants.SecurityAuditingLogName, null).Tasks;

        Assert.NotNull(tasks);
        Assert.NotEmpty(tasks);
    }
}
