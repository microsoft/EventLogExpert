// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Tests.TestUtils;
using EventLogExpert.Eventing.Tests.TestUtils.Constants;
using NSubstitute;

namespace EventLogExpert.Eventing.Tests.EventResolvers;

public sealed class LocalProviderEventResolverTests
{
    [Fact]
    public void Constructor_WithCacheAndLogger_ShouldCreateInstance()
    {
        // Arrange
        var cache = new EventResolverCache();
        var logger = Substitute.For<ITraceLogger>();

        // Act
        var resolver = new LocalProviderEventResolver(cache, logger);

        // Assert
        Assert.NotNull(resolver);
    }

    [Fact]
    public void Constructor_WithNoParameters_ShouldCreateInstance()
    {
        // Act
        var resolver = new LocalProviderEventResolver();

        // Assert
        Assert.NotNull(resolver);
    }

    [Fact]
    public void Constructor_WithNullCache_ShouldCreateInstance()
    {
        // Act
        var resolver = new LocalProviderEventResolver(null);

        // Assert
        Assert.NotNull(resolver);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldCreateInstance()
    {
        // Arrange
        var cache = new EventResolverCache();

        // Act
        var resolver = new LocalProviderEventResolver(cache, null);

        // Assert
        Assert.NotNull(resolver);
    }

    [Fact]
    public void Constructor_WithOnlyCache_ShouldCreateInstance()
    {
        // Arrange
        var cache = new EventResolverCache();

        // Act
        var resolver = new LocalProviderEventResolver(cache);

        // Assert
        Assert.NotNull(resolver);
    }

    [Fact]
    public void MultipleResolvers_WithDifferentInstances_ShouldResolveSeparately()
    {
        // Arrange
        var resolver1 = new LocalProviderEventResolver();
        var resolver2 = new LocalProviderEventResolver();

        var eventRecord = EventUtils.CreateBasicEvent();

        // Act
        resolver1.ResolveProviderDetails(eventRecord);
        resolver2.ResolveProviderDetails(eventRecord);
        var displayEvent1 = resolver1.ResolveEvent(eventRecord);
        var displayEvent2 = resolver2.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent1);
        Assert.NotNull(displayEvent2);
        Assert.Equal(displayEvent1.Source, displayEvent2.Source);
    }

    [Fact]
    public void MultipleResolvers_WithSharedCache_ShouldShareCachedStrings()
    {
        // Arrange
        var sharedCache = new EventResolverCache();
        var resolver1 = new LocalProviderEventResolver(sharedCache);
        var resolver2 = new LocalProviderEventResolver(sharedCache);

        var eventRecord = EventUtils.CreateBasicEvent();

        // Act
        resolver1.ResolveProviderDetails(eventRecord);
        var displayEvent1 = resolver1.ResolveEvent(eventRecord);

        resolver2.ResolveProviderDetails(eventRecord);
        var displayEvent2 = resolver2.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent1);
        Assert.NotNull(displayEvent2);
        // With shared cache, string values should be the same reference
        Assert.Same(displayEvent1.ComputerName, displayEvent2.ComputerName);
        Assert.Same(displayEvent1.LogName, displayEvent2.LogName);
        Assert.Same(displayEvent1.Source, displayEvent2.Source);
    }

    [Fact]
    public void ResolveEvent_WithMultipleProviders_ShouldResolveEachCorrectly()
    {
        // Arrange
        var resolver = new LocalProviderEventResolver();
        var eventRecords = EventUtils.CreateDifferentEvents().ToList();

        // Act
        resolver.ResolveProviderDetails(eventRecords[0]);
        resolver.ResolveProviderDetails(eventRecords[1]);
        var displayEvent1 = resolver.ResolveEvent(eventRecords[0]);
        var displayEvent2 = resolver.ResolveEvent(eventRecords[1]);

        // Assert
        Assert.NotNull(displayEvent1);
        Assert.NotNull(displayEvent2);
        Assert.Equal(Constants.ApplicationLogName, displayEvent1.LogName);
        Assert.Equal(Constants.SystemLogName, displayEvent2.LogName);
        Assert.NotEqual(displayEvent1.ComputerName, displayEvent2.ComputerName);
    }

    [Fact]
    public void ResolveEvent_WithoutCallingResolveProviderDetails_ShouldStillResolve()
    {
        // Arrange
        var resolver = new LocalProviderEventResolver();
        var eventRecord = EventUtils.CreateBasicEvent();

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Equal(eventRecord.Id, displayEvent.Id);
        Assert.Equal(eventRecord.ComputerName, displayEvent.ComputerName);
        Assert.NotNull(displayEvent.Description);
    }

    [Fact]
    public void ResolveProviderDetails_CalledTwiceForSameProvider_ShouldNotThrow()
    {
        // Arrange
        var resolver = new LocalProviderEventResolver();
        var eventRecord = EventUtils.CreateBasicEvent();

        // Act
        resolver.ResolveProviderDetails(eventRecord);

        // Second call should use cached provider details
        var exception = Record.Exception(() => resolver.ResolveProviderDetails(eventRecord));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void ResolveProviderDetails_ConcurrentCalls_ShouldHandleThreadSafely()
    {
        // Arrange
        var resolver = new LocalProviderEventResolver();

        var providerNames = new[]
        {
            Constants.ApplicationLogName,
            Constants.SystemLogName,
            Constants.TestProviderName,
            Constants.TestProviderLongName
        };

        var exceptions = new Exception?[20];

        // Act
        Parallel.For(0, 20, i =>
            {
                try
                {
                    var eventRecord = new EventRecord
                    {
                        ProviderName = providerNames[i % providerNames.Length],
                        Id = (ushort)(1000 + i)
                    };

                    resolver.ResolveProviderDetails(eventRecord);
                }
                catch (Exception ex)
                {
                    exceptions[i] = ex;
                }
            });

        // Assert
        Assert.All(exceptions, ex => Assert.Null(ex));
    }

    [Fact]
    public void ResolveProviderDetails_ConcurrentCallsForSameProvider_ShouldHandleThreadSafely()
    {
        // Arrange
        var resolver = new LocalProviderEventResolver();
        var exceptions = new Exception?[50];

        // Act
        Parallel.For(0, 50, i =>
            {
                try
                {
                    var eventRecord = EventUtils.CreateBasicEvent();
                    eventRecord.Id = (ushort)(1000 + i);
                    resolver.ResolveProviderDetails(eventRecord);
                }
                catch (Exception ex)
                {
                    exceptions[i] = ex;
                }
            });

        // Assert
        Assert.All(exceptions, ex => Assert.Null(ex));
    }

    [Fact]
    public void ResolveProviderDetails_MixedConcurrentProvidersWithCaching_ShouldHandleThreadSafely()
    {
        // Arrange
        var cache = new EventResolverCache();
        var resolver = new LocalProviderEventResolver(cache);

        var providerNames = new[]
        {
            Constants.ApplicationLogName,
            Constants.SystemLogName,
            Constants.TestProviderName
        };

        var exceptions = new Exception?[30];

        // Act
        Parallel.For(0, 30, i =>
            {
                try
                {
                    var eventRecord = new EventRecord
                    {
                        ProviderName = providerNames[i % providerNames.Length],
                        Id = (ushort)(1000 + i)
                    };

                    resolver.ResolveProviderDetails(eventRecord);
                }
                catch (Exception ex)
                {
                    exceptions[i] = ex;
                }
            });

        // Assert
        Assert.All(exceptions, ex => Assert.Null(ex));
    }

    [Fact]
    public void ResolveProviderDetails_ThenResolveEvent_ShouldReturnPopulatedModel()
    {
        // Arrange
        var resolver = new LocalProviderEventResolver();
        var eventRecord = EventUtils.CreateBasicEvent();

        // Act
        resolver.ResolveProviderDetails(eventRecord);
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Equal(eventRecord.Id, displayEvent.Id);
        Assert.Equal(eventRecord.ComputerName, displayEvent.ComputerName);
        Assert.Equal(eventRecord.LogName, displayEvent.LogName);
        Assert.Equal(eventRecord.ProviderName, displayEvent.Source);
        Assert.Equal(eventRecord.TimeCreated, displayEvent.TimeCreated);
        Assert.Equal(eventRecord.RecordId, displayEvent.RecordId);
        Assert.NotNull(displayEvent.Description);
    }

    [Fact]
    public void ResolveProviderDetails_WithCache_ThenResolveEvent_ShouldUseCachedStrings()
    {
        // Arrange
        var cache = new EventResolverCache();
        var resolver = new LocalProviderEventResolver(cache);
        var eventRecord = EventUtils.CreateBasicEvent();

        // Act
        resolver.ResolveProviderDetails(eventRecord);
        var displayEvent1 = resolver.ResolveEvent(eventRecord);
        var displayEvent2 = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent1);
        Assert.NotNull(displayEvent2);
        // With cache, string values should be the same reference
        Assert.Same(displayEvent1.ComputerName, displayEvent2.ComputerName);
        Assert.Same(displayEvent1.LogName, displayEvent2.LogName);
        Assert.Same(displayEvent1.Source, displayEvent2.Source);
    }

    [Fact]
    public void ResolveProviderDetails_WithDifferentProviders_ShouldResolveEachIndependently()
    {
        // Arrange
        var resolver = new LocalProviderEventResolver();
        var eventRecords = EventUtils.CreateDifferentEvents().ToList();

        // Act
        resolver.ResolveProviderDetails(eventRecords[0]);
        resolver.ResolveProviderDetails(eventRecords[1]);
        var displayEvent1 = resolver.ResolveEvent(eventRecords[0]);
        var displayEvent2 = resolver.ResolveEvent(eventRecords[1]);

        // Assert
        Assert.NotNull(displayEvent1);
        Assert.NotNull(displayEvent2);
        Assert.Equal(Constants.ApplicationLogName, displayEvent1.LogName);
        Assert.Equal(Constants.SystemLogName, displayEvent2.LogName);
    }

    [Fact]
    public void ResolveProviderDetails_WithEmptyProviderName_ShouldResolveWithoutException()
    {
        // Arrange
        var resolver = new LocalProviderEventResolver();

        var eventRecord = new EventRecord
        {
            ProviderName = string.Empty,
            Id = 1000,
            ComputerName = "TestComputer",
            LogName = Constants.ApplicationLogName
        };

        // Act
        resolver.ResolveProviderDetails(eventRecord);
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Equal(string.Empty, displayEvent.Source);
    }

    [Fact]
    public void ResolveProviderDetails_WithNonExistentProvider_ShouldResolveWithDefaultDescription()
    {
        // Arrange
        var resolver = new LocalProviderEventResolver();
        var nonExistentProvider = "NonExistentProvider_" + Guid.NewGuid();

        var eventRecord = new EventRecord
        {
            ProviderName = nonExistentProvider,
            Id = 1000,
            ComputerName = "TestComputer",
            LogName = Constants.ApplicationLogName
        };

        // Act
        resolver.ResolveProviderDetails(eventRecord);
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Equal(nonExistentProvider, displayEvent.Source);
        Assert.NotNull(displayEvent.Description);
        // Non-existent providers should get a default description
        Assert.Contains("No matching", displayEvent.Description);
    }
}
