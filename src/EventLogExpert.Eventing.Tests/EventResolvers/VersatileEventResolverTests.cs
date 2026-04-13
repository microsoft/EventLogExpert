// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventProviderDatabase;
using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Providers;
using EventLogExpert.Eventing.Tests.TestUtils;
using EventLogExpert.Eventing.Tests.TestUtils.Constants;
using Microsoft.Data.Sqlite;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.Eventing.Tests.EventResolvers;

public sealed class VersatileEventResolverTests
{
    [Fact]
    public void Constructor_WithCacheAndLogger_ShouldCreateInstance()
    {
        // Arrange
        var cache = new EventResolverCache();
        var logger = Substitute.For<ITraceLogger>();

        // Act
        using var resolver = new VersatileEventResolver(cache: cache, tracer: logger);

        // Assert
        Assert.NotNull(resolver);
    }

    [Fact]
    public void Constructor_WithDatabaseCollection_ShouldUseDatabaseResolver()
    {
        // Arrange
        string dbPath = Path.Combine(Path.GetTempPath(), $"Test_{Guid.NewGuid()}.db");

        try
        {
            using (var context = new EventProviderDbContext(dbPath, false))
            {
                context.SaveChanges();
            }

            var dbCollection = Substitute.For<IDatabaseCollectionProvider>();
            dbCollection.ActiveDatabases.Returns(ImmutableList.Create(dbPath));

            // Act & Assert
            using (var resolver = new VersatileEventResolver(dbCollection))
            {
                Assert.NotNull(resolver);
            }
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                SqliteConnection.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public void Constructor_WithEmptyDatabaseCollection_ShouldUseLocalResolver()
    {
        // Arrange
        var dbCollection = Substitute.For<IDatabaseCollectionProvider>();
        dbCollection.ActiveDatabases.Returns(ImmutableList<string>.Empty);

        // Act
        using var resolver = new VersatileEventResolver(dbCollection);

        // Assert
        Assert.NotNull(resolver);
    }

    [Fact]
    public void Constructor_WithLogger_ShouldLogInstantiation()
    {
        // Arrange
        var logger = Substitute.For<ITraceLogger>();

        // Act
        using var resolver = new VersatileEventResolver(tracer: logger);

        // Assert
        logger.Received().Debug(Arg.Is<DebugLogHandler>(h => h.ToString().Contains("VersatileEventResolver")));
    }

    [Fact]
    public void Constructor_WithNoDatabaseCollection_ShouldUseLocalResolver()
    {
        // Act
        using var resolver = new VersatileEventResolver(null);

        // Assert
        Assert.NotNull(resolver);
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var resolver = new VersatileEventResolver();

        // Act & Assert
        resolver.Dispose();
        resolver.Dispose(); // Second call should not throw
        resolver.Dispose(); // Third call should not throw
    }

    [Fact]
    public void Dispose_ThenResolveEvent_WithDatabaseResolver_ShouldThrowObjectDisposedException()
    {
        // Arrange
        string dbPath = Path.Combine(Path.GetTempPath(), $"Test_{Guid.NewGuid()}.db");

        try
        {
            using (var context = new EventProviderDbContext(dbPath, false))
            {
                context.SaveChanges();
            }

            var dbCollection = Substitute.For<IDatabaseCollectionProvider>();
            dbCollection.ActiveDatabases.Returns(ImmutableList.Create(dbPath));

            var resolver = new VersatileEventResolver(dbCollection);
            var eventRecord = EventUtils.CreateBasicEvent();

            // Act
            resolver.Dispose();

            // Assert
            Assert.Throws<ObjectDisposedException>(() => resolver.ResolveEvent(eventRecord));
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                SqliteConnection.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public void Dispose_ThenResolveEvent_WithLocalResolver_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var resolver = new VersatileEventResolver(); // Uses local resolver
        var eventRecord = EventUtils.CreateBasicEvent();

        // Act
        resolver.Dispose();

        // Assert - Should throw even though local resolver doesn't hold resources
        Assert.Throws<ObjectDisposedException>(() => resolver.ResolveEvent(eventRecord));
    }

    [Fact]
    public void Dispose_ThenResolveProviderDetails_WithDatabaseResolver_ShouldThrowObjectDisposedException()
    {
        // Arrange
        string dbPath = Path.Combine(Path.GetTempPath(), $"Test_{Guid.NewGuid()}.db");

        try
        {
            using (var context = new EventProviderDbContext(dbPath, false))
            {
                context.SaveChanges();
            }

            var dbCollection = Substitute.For<IDatabaseCollectionProvider>();
            dbCollection.ActiveDatabases.Returns(ImmutableList.Create(dbPath));

            var resolver = new VersatileEventResolver(dbCollection);
            var eventRecord = new EventRecord
            {
                ProviderName = Constants.TestProviderName,
                Id = 1000
            };

            // Act
            resolver.Dispose();

            // Assert
            Assert.Throws<ObjectDisposedException>(() => resolver.ResolveProviderDetails(eventRecord));
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                SqliteConnection.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public void Dispose_ThenResolveProviderDetails_WithLocalResolver_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var resolver = new VersatileEventResolver(); // Uses local resolver
        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 1000
        };

        // Act
        resolver.Dispose();

        // Assert - Should throw even though local resolver doesn't hold resources
        Assert.Throws<ObjectDisposedException>(() => resolver.ResolveProviderDetails(eventRecord));
    }

    [Fact]
    public void ResolveEvent_ConcurrentCalls_ShouldHandleThreadSafely()
    {
        // Arrange
        using var resolver = new VersatileEventResolver();
        var exceptions = new Exception?[50];

        // Act
        Parallel.For(0, 50, i =>
            {
                try
                {
                    var eventRecord = new EventRecord
                    {
                        ProviderName = $"Provider{i % 5}",
                        Id = (ushort)(1000 + i),
                        ComputerName = $"Computer{i}",
                        LogName = Constants.ApplicationLogName
                    };

                    var displayEvent = resolver.ResolveEvent(eventRecord);
                    Assert.NotNull(displayEvent);
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
    public void ResolveEvent_WithCache_ShouldUseCachedStrings()
    {
        // Arrange
        var cache = new EventResolverCache();
        using var resolver = new VersatileEventResolver(cache: cache);

        var eventRecord = EventUtils.CreateBasicEvent();

        // Act
        var event1 = resolver.ResolveEvent(eventRecord);
        var event2 = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.Same(event1.ComputerName, event2.ComputerName);
        Assert.Same(event1.LogName, event2.LogName);
    }

    [Fact]
    public void ResolveEvent_WithDatabaseResolver_ShouldResolveEvent()
    {
        // Arrange
        string dbPath = Path.Combine(Path.GetTempPath(), $"Test_{Guid.NewGuid()}.db");

        try
        {
            using (var context = new EventProviderDbContext(dbPath, false))
            {
                context.ProviderDetails.Add(new ProviderDetails
                {
                    ProviderName = "TestProvider",
                    Messages = new List<MessageModel>()
                });

                context.SaveChanges();
            }

            var dbCollection = Substitute.For<IDatabaseCollectionProvider>();
            dbCollection.ActiveDatabases.Returns(ImmutableList.Create(dbPath));

            var eventRecord = new EventRecord
            {
                ProviderName = "TestProvider",
                Id = 1000,
                ComputerName = "TestComputer",
                LogName = Constants.ApplicationLogName,
                TimeCreated = DateTime.UtcNow
            };

            // Act
            DisplayEventModel displayEvent;
            using (var resolver = new VersatileEventResolver(dbCollection))
            {
                displayEvent = resolver.ResolveEvent(eventRecord);
            }

            // Assert
            Assert.NotNull(displayEvent);
            Assert.Equal(eventRecord.Id, displayEvent.Id);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                SqliteConnection.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public void ResolveEvent_WithLocalResolver_ShouldResolveEvent()
    {
        // Arrange
        using var resolver = new VersatileEventResolver();
        var eventRecord = EventUtils.CreateBasicEvent();

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Equal(eventRecord.Id, displayEvent.Id);
        Assert.Equal(eventRecord.ComputerName, displayEvent.ComputerName);
    }

    [Fact]
    public void ResolveEvent_WithMultipleProviders_ShouldResolveAll()
    {
        // Arrange
        using var resolver = new VersatileEventResolver();

        var providers = new[]
        {
            Constants.ApplicationLogName,
            Constants.SystemLogName,
            Constants.TestProviderName
        };

        // Act & Assert
        foreach (var provider in providers)
        {
            var eventRecord = new EventRecord
            {
                ProviderName = provider,
                Id = 1000,
                ComputerName = "TestComputer",
                LogName = Constants.ApplicationLogName
            };

            var displayEvent = resolver.ResolveEvent(eventRecord);
            Assert.NotNull(displayEvent);
            Assert.Equal(provider, displayEvent.Source);
        }
    }

    [Fact]
    public void ResolveEvent_WithNonExistentProvider_ShouldReturnDefaultDescription()
    {
        // Arrange
        using var resolver = new VersatileEventResolver();

        var eventRecord = new EventRecord
        {
            ProviderName = "NonExistent-" + Guid.NewGuid(),
            Id = 1000,
            ComputerName = "TestComputer",
            LogName = Constants.ApplicationLogName
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("No matching", displayEvent.Description);
    }

    [Fact]
    public void ResolveProviderDetails_CalledTwice_ShouldHandleCorrectly()
    {
        // Arrange
        using var resolver = new VersatileEventResolver();

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 1000
        };

        // Act
        resolver.ResolveProviderDetails(eventRecord);
        var exception = Record.Exception(() => resolver.ResolveProviderDetails(eventRecord));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void ResolveProviderDetails_ConcurrentCalls_ShouldHandleThreadSafely()
    {
        // Arrange
        using var resolver = new VersatileEventResolver();
        var exceptions = new Exception?[50];

        // Act
        Parallel.For(0, 50, i =>
            {
                try
                {
                    var eventRecord = new EventRecord
                    {
                        ProviderName = $"Provider{i % 10}",
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
    public void ResolveProviderDetails_WithDatabaseResolver_ShouldResolve()
    {
        // Arrange
        string dbPath = Path.Combine(Path.GetTempPath(), $"Test_{Guid.NewGuid()}.db");

        try
        {
            using (var context = new EventProviderDbContext(dbPath, false))
            {
                context.SaveChanges();
            }

            var dbCollection = Substitute.For<IDatabaseCollectionProvider>();
            dbCollection.ActiveDatabases.Returns(ImmutableList.Create(dbPath));

            var eventRecord = new EventRecord
            {
                ProviderName = "TestProvider",
                Id = 1000
            };

            // Act
            Exception? exception;
            using (var resolver = new VersatileEventResolver(dbCollection))
            {
                exception = Record.Exception(() => resolver.ResolveProviderDetails(eventRecord));
            }

            // Assert
            Assert.Null(exception);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                SqliteConnection.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public void ResolveProviderDetails_WithLocalResolver_ShouldResolve()
    {
        // Arrange
        using var resolver = new VersatileEventResolver();

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 1000
        };

        // Act
        var exception = Record.Exception(() => resolver.ResolveProviderDetails(eventRecord));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void SwitchBetweenResolvers_WithDifferentInstances_ShouldWorkIndependently()
    {
        // Arrange
        string dbPath = Path.Combine(Path.GetTempPath(), $"Test_{Guid.NewGuid()}.db");

        try
        {
            using (var context = new EventProviderDbContext(dbPath, false))
            {
                context.SaveChanges();
            }

            var dbCollection = Substitute.For<IDatabaseCollectionProvider>();
            dbCollection.ActiveDatabases.Returns(ImmutableList.Create(dbPath));

            var eventRecord = EventUtils.CreateBasicEvent();

            // Act
            DisplayEventModel localEvent;
            DisplayEventModel databaseEvent;

            using (var localResolver = new VersatileEventResolver())
            {
                localEvent = localResolver.ResolveEvent(eventRecord);
            }

            using (var databaseResolver = new VersatileEventResolver(dbCollection))
            {
                databaseEvent = databaseResolver.ResolveEvent(eventRecord);
            }

            // Assert
            Assert.NotNull(localEvent);
            Assert.NotNull(databaseEvent);
            Assert.Equal(localEvent.Source, databaseEvent.Source);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                SqliteConnection.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                File.Delete(dbPath);
            }
        }
    }
}
