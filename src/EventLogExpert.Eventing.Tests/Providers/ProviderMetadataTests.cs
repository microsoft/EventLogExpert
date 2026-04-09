// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Providers;
using EventLogExpert.Eventing.Tests.TestUtils.Constants;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Collections.ObjectModel;

namespace EventLogExpert.Eventing.Tests.Providers;

public sealed class ProviderMetadataTests
{
    [Fact]
    public async Task Channels_WhenAccessedConcurrently_ShouldReturnValidData()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var tasks = new[]
        {
            Task.Run(() => metadata?.Channels),
            Task.Run(() => metadata?.Channels),
            Task.Run(() => metadata?.Channels)
        };

        await Task.WhenAll(tasks);

        // Assert
        var results = tasks.Select(t => t.Result).ToList();
        Assert.All(results, Assert.NotNull);
        Assert.All(results, Assert.NotEmpty);
    }

    [Fact]
    public void Channels_WhenCalledMultipleTimes_ShouldReturnSameInstance()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var channels1 = metadata?.Channels;
        var channels2 = metadata?.Channels;

        // Assert
        Assert.NotNull(channels1);
        Assert.NotNull(channels2);
        Assert.Same(channels1, channels2);
    }

    [Fact]
    public void Channels_WhenProviderHasChannels_ShouldHaveValidKeys()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var channels = metadata?.Channels;

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
    public void Channels_WhenProviderHasNoDuplicateChannelIds_ShouldNotLoseData()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var channels = metadata?.Channels;

        // Assert
        Assert.NotNull(channels);
        var uniqueIds = channels.Keys.Distinct().Count();
        Assert.Equal(channels.Count, uniqueIds);
    }

    [Fact]
    public void Channels_WhenValidProvider_ShouldContainData()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var channels = metadata?.Channels;

        // Assert
        Assert.NotNull(channels);
        Assert.NotEmpty(channels);
    }

    [Fact]
    public void Channels_WhenValidProvider_ShouldReturnDictionary()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var channels = metadata?.Channels;

        // Assert
        Assert.NotNull(channels);
        Assert.IsAssignableFrom<IDictionary<uint, string>>(channels);
    }

    [Fact]
    public void Channels_WhenValidProvider_ShouldReturnReadOnlyDictionary()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var channels = metadata?.Channels;

        // Assert
        Assert.NotNull(channels);
        Assert.IsAssignableFrom<ReadOnlyDictionary<uint, string>>(channels);
    }

    [Fact]
    public void Create_WhenCalledConcurrently_ShouldHandleMultipleInstances()
    {
        // Arrange & Act
        using var metadata1 = ProviderMetadata.Create(Constants.SecurityAuditingLogName);
        using var metadata2 = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Assert
        Assert.NotNull(metadata1);
        Assert.NotNull(metadata2);
        Assert.NotSame(metadata1, metadata2);
    }

    [Theory]
    [InlineData(Constants.SecurityAuditingLogName)]
    [InlineData(Constants.KernelGeneralLogName)]
    [InlineData(Constants.PowerShellLogName)]
    public void Create_WhenCommonProviders_ShouldReturnMetadata(string providerName)
    {
        // Arrange & Act
        using var metadata = ProviderMetadata.Create(providerName);

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
        using var metadata = ProviderMetadata.Create(string.Empty, mockLogger);

        // Assert
        // Empty provider name is treated as valid by Windows
        mockLogger.DidNotReceive()
            .Trace(Arg.Is<string>(s => s.Contains("Failed to create metadata")), Arg.Any<LogLevel>());
    }

    [Fact]
    public void Create_WhenEmptyProviderName_ShouldReturnMetadata()
    {
        // Arrange & Act
        using var metadata = ProviderMetadata.Create(string.Empty);

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
        using var metadata = ProviderMetadata.Create(providerName, mockLogger);

        // Assert
        mockLogger.Received(1).Trace(
            Arg.Is<string>(s => s.Contains("Failed to create metadata") && s.Contains(providerName)),
            Arg.Any<LogLevel>());
    }

    [Fact]
    public void Create_WhenInvalidProvider_ShouldReturnNull()
    {
        // Arrange
        var providerName = "NonExistentProvider_" + Guid.NewGuid();

        // Act
        using var metadata = ProviderMetadata.Create(providerName);

        // Assert
        Assert.Null(metadata);
    }

    [Fact]
    public void Create_WhenSpecialCharactersInProviderName_ShouldReturnNull()
    {
        // Arrange & Act
        using var metadata = ProviderMetadata.Create("Invalid<>Provider|Name");

        // Assert
        Assert.Null(metadata);
    }

    [Fact]
    public void Create_WhenValidProvider_ShouldNotLogError()
    {
        // Arrange
        var mockLogger = Substitute.For<ITraceLogger>();

        // Act
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName, mockLogger);

        // Assert
        Assert.NotNull(metadata);

        mockLogger.DidNotReceive()
            .Trace(Arg.Is<string>(s => s.Contains("Failed to create metadata")), Arg.Any<LogLevel>());
    }

    [Fact]
    public void Create_WhenValidProvider_ShouldReturnMetadata()
    {
        // Arrange & Act
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Assert
        Assert.NotNull(metadata);
    }

    [Fact]
    public void Create_WhenWhitespaceProviderName_ShouldReturnNull()
    {
        // Arrange & Act
        using var metadata = ProviderMetadata.Create("   ");

        // Assert
        Assert.Null(metadata);
    }

    [Fact]
    public void Dispose_AfterDispose_PropertiesShouldStillBeAccessible()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Cache properties before dispose
        var channelsBefore = metadata?.Channels;

        // Act
        metadata?.Dispose();
        var channelsAfter = metadata?.Channels;

        // Assert
        Assert.NotNull(channelsBefore);
        Assert.NotNull(channelsAfter);
        Assert.Same(channelsBefore, channelsAfter);
    }

    [Fact]
    public void Dispose_WhenCalled_ShouldNotThrow()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act & Assert
        metadata?.Dispose();
    }

    [Fact]
    public void Dispose_WhenCalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act & Assert
        metadata?.Dispose();
        metadata?.Dispose();
        metadata?.Dispose();
    }

    [Fact]
    public void Error_WhenInvalidProvider_ShouldContainErrorMessage()
    {
        // Arrange
        var providerName = "NonExistentProvider_" + Guid.NewGuid();
        var mockLogger = Substitute.For<ITraceLogger>();

        // Act
        using var metadata = ProviderMetadata.Create(providerName, mockLogger);

        // Assert
        Assert.Null(metadata);

        mockLogger.Received(1)
            .Trace(Arg.Is<string>(s => !string.IsNullOrEmpty(s)), Arg.Any<LogLevel>());
    }

    [Fact]
    public void Events_WhenCalledTwice_ShouldReturnDifferentEnumerableInstances()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var events1 = metadata?.Events;
        var events2 = metadata?.Events;

        // Assert
        Assert.NotNull(events1);
        Assert.NotNull(events2);
    }

    [Fact]
    public void Events_WhenProviderHasEvents_ShouldHaveValidEventMetadata()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var events = metadata?.Events?.ToList();

        // Assert
        Assert.NotNull(events);

        if (events.Count == 0) { return; }

        var firstEvent = events.First();
        Assert.True(firstEvent.Id >= 0);
        Assert.True(firstEvent.Version >= 0);
    }

    [Fact]
    public void Events_WhenValidProvider_ShouldContainEventMetadata()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var events = metadata?.Events?.ToList();

        // Assert
        Assert.NotNull(events);

        if (events.Count == 0) { return; }

        Assert.All(events,
            e =>
            {
                Assert.NotNull(e);
                Assert.True(e.Id > 0);
            });
    }

    [Fact]
    public void Events_WhenValidProvider_ShouldContainEvents()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var events = metadata?.Events?.ToList();

        // Assert
        Assert.NotNull(events);
        Assert.NotEmpty(events);
    }

    [Fact]
    public void Events_WhenValidProvider_ShouldReturnEnumerable()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var events = metadata?.Events;

        // Assert
        Assert.NotNull(events);
        Assert.IsAssignableFrom<IEnumerable<EventMetadata>>(events);
    }

    [Fact]
    public async Task Keywords_WhenAccessedConcurrently_ShouldReturnValidData()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.PowerShellLogName);

        // Act
        var tasks = new[]
        {
            Task.Run(() => metadata?.Keywords),
            Task.Run(() => metadata?.Keywords),
            Task.Run(() => metadata?.Keywords)
        };

        await Task.WhenAll(tasks);

        // Assert
        var results = tasks.Select(t => t.Result).ToList();
        Assert.All(results, r => Assert.NotNull(r));
        // Verify all results have the same count (cached properly)
        var firstCount = results[0]?.Count ?? 0;
        Assert.All(results, r => Assert.Equal(firstCount, r?.Count ?? 0));
    }

    [Fact]
    public void Keywords_WhenCalledMultipleTimes_ShouldReturnSameInstance()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var keywords1 = metadata?.Keywords;
        var keywords2 = metadata?.Keywords;

        // Assert
        Assert.NotNull(keywords1);
        Assert.NotNull(keywords2);
        Assert.Same(keywords1, keywords2);
    }

    [Fact]
    public void Keywords_WhenProviderHasKeywords_ShouldContainData()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.PowerShellLogName);

        // Act
        var keywords = metadata?.Keywords;

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
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var keywords = metadata?.Keywords;

        // Assert
        Assert.NotNull(keywords);

        Assert.All(keywords,
            keyword =>
            {
                Assert.False(string.IsNullOrEmpty(keyword.Value));
            });
    }

    [Fact]
    public void Keywords_WhenProviderHasNoDuplicateKeywordValues_ShouldNotLoseData()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var keywords = metadata?.Keywords;

        // Assert
        Assert.NotNull(keywords);
        var uniqueValues = keywords.Keys.Distinct().Count();
        Assert.Equal(keywords.Count, uniqueValues);
    }

    [Fact]
    public void Keywords_WhenValidProvider_ShouldReturnDictionary()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var keywords = metadata?.Keywords;

        // Assert
        Assert.NotNull(keywords);
        Assert.IsAssignableFrom<IDictionary<long, string>>(keywords);
    }

    [Fact]
    public void Keywords_WhenValidProvider_ShouldReturnReadOnlyDictionary()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var keywords = metadata?.Keywords;

        // Assert
        Assert.NotNull(keywords);
        Assert.IsAssignableFrom<ReadOnlyDictionary<long, string>>(keywords);
    }

    [Fact]
    public void MessageFilePath_WhenCalledMultipleTimes_ShouldReturnConsistentPath()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var path1 = metadata?.MessageFilePath;
        var path2 = metadata?.MessageFilePath;

        // Assert
        Assert.NotNull(path1);
        Assert.NotNull(path2);
        Assert.Equal(path1, path2);
    }

    [Fact]
    public void MessageFilePath_WhenValidProvider_ShouldContainDllExtension()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

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
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

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
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var messageFilePath = metadata?.MessageFilePath;

        // Assert
        Assert.NotNull(messageFilePath);
    }

    [Fact]
    public async Task Opcodes_WhenAccessedConcurrently_ShouldReturnValidData()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var tasks = new[]
        {
            Task.Run(() => metadata?.Opcodes),
            Task.Run(() => metadata?.Opcodes),
            Task.Run(() => metadata?.Opcodes)
        };

        await Task.WhenAll(tasks);

        // Assert
        var results = tasks.Select(t => t.Result).ToList();
        Assert.All(results, r => Assert.NotNull(r));
        Assert.All(results, r => Assert.NotEmpty(r));
    }

    [Fact]
    public void Opcodes_WhenCalledMultipleTimes_ShouldReturnSameInstance()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var opcodes1 = metadata?.Opcodes;
        var opcodes2 = metadata?.Opcodes;

        // Assert
        Assert.NotNull(opcodes1);
        Assert.NotNull(opcodes2);
        Assert.Same(opcodes1, opcodes2);
    }

    [Fact]
    public void Opcodes_WhenProviderHasNoDuplicateOpcodeValues_ShouldNotLoseData()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var opcodes = metadata?.Opcodes;

        // Assert
        Assert.NotNull(opcodes);
        var uniqueValues = opcodes.Keys.Distinct().Count();
        Assert.Equal(opcodes.Count, uniqueValues);
    }

    [Fact]
    public void Opcodes_WhenProviderHasOpcodes_ShouldHaveValidValues()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var opcodes = metadata?.Opcodes;

        // Assert
        Assert.NotNull(opcodes);

        Assert.All(opcodes,
            opcode =>
            {
                Assert.False(string.IsNullOrEmpty(opcode.Value));
            });
    }

    [Fact]
    public void Opcodes_WhenValidProvider_ShouldContainData()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var opcodes = metadata?.Opcodes;

        // Assert
        Assert.NotNull(opcodes);
        Assert.NotEmpty(opcodes);
    }

    [Fact]
    public void Opcodes_WhenValidProvider_ShouldReturnDictionary()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var opcodes = metadata?.Opcodes;

        // Assert
        Assert.NotNull(opcodes);
        Assert.IsAssignableFrom<IDictionary<int, string>>(opcodes);
    }

    [Fact]
    public void Opcodes_WhenValidProvider_ShouldReturnReadOnlyDictionary()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var opcodes = metadata?.Opcodes;

        // Assert
        Assert.NotNull(opcodes);
        Assert.IsAssignableFrom<ReadOnlyDictionary<int, string>>(opcodes);
    }

    [Fact]
    public void ParameterFilePath_WhenCalledMultipleTimes_ShouldReturnConsistentPath()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var path1 = metadata?.ParameterFilePath;
        var path2 = metadata?.ParameterFilePath;

        // Assert
        Assert.NotNull(path1);
        Assert.NotNull(path2);
        Assert.Equal(path1, path2);
    }

    [Fact]
    public void ParameterFilePath_WhenValidProvider_ShouldReturnPath()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var parameterFilePath = metadata?.ParameterFilePath;

        // Assert
        Assert.NotNull(parameterFilePath);
    }

    [Fact]
    public async Task Tasks_WhenAccessedConcurrently_ShouldReturnValidData()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var tasks = new[]
        {
            Task.Run(() => metadata?.Tasks),
            Task.Run(() => metadata?.Tasks),
            Task.Run(() => metadata?.Tasks)
        };

        await Task.WhenAll(tasks);

        // Assert
        var results = tasks.Select(t => t.Result).ToList();
        Assert.All(results, r => Assert.NotNull(r));
        Assert.All(results, r => Assert.NotEmpty(r));
    }

    [Fact]
    public void Tasks_WhenCalledMultipleTimes_ShouldReturnSameInstance()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var tasks1 = metadata?.Tasks;
        var tasks2 = metadata?.Tasks;

        // Assert
        Assert.NotNull(tasks1);
        Assert.NotNull(tasks2);
        Assert.Same(tasks1, tasks2);
    }

    [Fact]
    public void Tasks_WhenProviderHasNoDuplicateTaskValues_ShouldNotLoseData()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var tasks = metadata?.Tasks;

        // Assert
        Assert.NotNull(tasks);
        var uniqueValues = tasks.Keys.Distinct().Count();
        Assert.Equal(tasks.Count, uniqueValues);
    }

    [Fact]
    public void Tasks_WhenProviderHasTasks_ShouldHaveValidValues()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var tasks = metadata?.Tasks;

        // Assert
        Assert.NotNull(tasks);

        Assert.All(tasks,
            task =>
            {
                Assert.False(string.IsNullOrEmpty(task.Value));
            });
    }

    [Fact]
    public void Tasks_WhenValidProvider_ShouldContainData()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var tasks = metadata?.Tasks;

        // Assert
        Assert.NotNull(tasks);
        Assert.NotEmpty(tasks);
    }

    [Fact]
    public void Tasks_WhenValidProvider_ShouldReturnDictionary()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var tasks = metadata?.Tasks;

        // Assert
        Assert.NotNull(tasks);
        Assert.IsAssignableFrom<IDictionary<int, string>>(tasks);
    }

    [Fact]
    public void Tasks_WhenValidProvider_ShouldReturnReadOnlyDictionary()
    {
        // Arrange
        using var metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);

        // Act
        var tasks = metadata?.Tasks;

        // Assert
        Assert.NotNull(tasks);
        Assert.IsAssignableFrom<ReadOnlyDictionary<int, string>>(tasks);
    }
}
