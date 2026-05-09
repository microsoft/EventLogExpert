// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Databases;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Logging;
using EventLogExpert.Eventing.ProviderDatabase;
using EventLogExpert.Eventing.Providers;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Eventing.Tests.TestUtils;
using EventLogExpert.Eventing.Tests.TestUtils.Constants;
using Microsoft.Data.Sqlite;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.Eventing.Tests.Resolvers;

public sealed class EventResolverTests
{
    [Fact]
    public void Constructor_WithCacheAndLogger_ShouldCreateInstance()
    {
        // Arrange
        var cache = new EventResolverCache();
        var logger = Substitute.For<ITraceLogger>();

        // Act
        using var resolver = new EventResolver(cache: cache, logger: logger);

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
            using (var context = new ProviderDbContext(dbPath, false))
            {
                context.SaveChanges();
            }

            var dbCollection = Substitute.For<IActiveDatabasePathsProvider>();
            dbCollection.ActiveDatabases.Returns(ImmutableList.Create(dbPath));

            // Act & Assert
            using (var resolver = new EventResolver(dbCollection))
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
        var dbCollection = Substitute.For<IActiveDatabasePathsProvider>();
        dbCollection.ActiveDatabases.Returns(ImmutableList<string>.Empty);

        // Act
        using var resolver = new EventResolver(dbCollection);

        // Assert
        Assert.NotNull(resolver);
    }

    [Fact]
    public void Constructor_WithLogger_ShouldLogInstantiation()
    {
        // Arrange
        var logger = Substitute.For<ITraceLogger>();

        // Act
        using var resolver = new EventResolver(logger: logger);

        // Assert
        logger.Received().Debug(Arg.Is<DebugLogHandler>(h => h.ToString().Contains("EventResolver")));
    }

    [Fact]
    public void Constructor_WithNoDatabaseCollection_ShouldUseLocalResolver()
    {
        // Act
        using var resolver = new EventResolver(null);

        // Assert
        Assert.NotNull(resolver);
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var resolver = new EventResolver();

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
            using (var context = new ProviderDbContext(dbPath, false))
            {
                context.SaveChanges();
            }

            var dbCollection = Substitute.For<IActiveDatabasePathsProvider>();
            dbCollection.ActiveDatabases.Returns(ImmutableList.Create(dbPath));

            var resolver = new EventResolver(dbCollection);
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
        var resolver = new EventResolver(); // Uses local resolver
        var eventRecord = EventUtils.CreateBasicEvent();

        // Act
        resolver.Dispose();

        // Assert - Should throw even though local resolver doesn't hold resources
        Assert.Throws<ObjectDisposedException>(() => resolver.ResolveEvent(eventRecord));
    }

    [Fact]
    public void Dispose_ThenLoadProviderDetails_WithDatabaseResolver_ShouldThrowObjectDisposedException()
    {
        // Arrange
        string dbPath = Path.Combine(Path.GetTempPath(), $"Test_{Guid.NewGuid()}.db");

        try
        {
            using (var context = new ProviderDbContext(dbPath, false))
            {
                context.SaveChanges();
            }

            var dbCollection = Substitute.For<IActiveDatabasePathsProvider>();
            dbCollection.ActiveDatabases.Returns(ImmutableList.Create(dbPath));

            var resolver = new EventResolver(dbCollection);
            var eventRecord = new EventRecord
            {
                ProviderName = Constants.TestProviderName,
                Id = 1000
            };

            // Act
            resolver.Dispose();

            // Assert
            Assert.Throws<ObjectDisposedException>(() => resolver.LoadProviderDetails(eventRecord));
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
    public void Dispose_ThenLoadProviderDetails_WithLocalResolver_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var resolver = new EventResolver(); // Uses local resolver
        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 1000
        };

        // Act
        resolver.Dispose();

        // Assert - Should throw even though local resolver doesn't hold resources
        Assert.Throws<ObjectDisposedException>(() => resolver.LoadProviderDetails(eventRecord));
    }

    [Fact]
    public void ResolveEvent_ConcurrentCalls_ShouldHandleThreadSafely()
    {
        // Arrange
        using var resolver = new EventResolver();
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
        using var resolver = new EventResolver(cache: cache);

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
            using (var context = new ProviderDbContext(dbPath, false))
            {
                context.ProviderDetails.Add(new ProviderDetails
                {
                    ProviderName = "TestProvider",
                    Messages = new List<MessageModel>()
                });

                context.SaveChanges();
            }

            var dbCollection = Substitute.For<IActiveDatabasePathsProvider>();
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
            using (var resolver = new EventResolver(dbCollection))
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
        using var resolver = new EventResolver();
        var eventRecord = EventUtils.CreateBasicEvent();

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Equal(eventRecord.Id, displayEvent.Id);
        Assert.Equal(eventRecord.ComputerName, displayEvent.ComputerName);
    }

    [Fact]
    public void ResolveEvent_WithMixedDbAndUnknownProviders_ShouldOnlyApplyDatabaseTextToDbProvider()
    {
        const string DBProviderName = "MixedScenarioDbProvider";
        const string DBMessageText = "Resolved from test database for mixed scenario";
        const ushort EventId = 1000;

        string unknownProviderName = $"NotInDb-{Guid.NewGuid()}";
        string dbPath = Path.Combine(Path.GetTempPath(), $"Test_{Guid.NewGuid()}.db");

        try
        {
            using (var context = new ProviderDbContext(dbPath, false))
            {
                context.ProviderDetails.Add(new ProviderDetails
                {
                    ProviderName = DBProviderName,
                    Messages =
                    [
                        new MessageModel
                        {
                            ProviderName = DBProviderName,
                            ShortId = (short)EventId,
                            Text = DBMessageText
                        }
                    ]
                });

                context.SaveChanges();
            }

            var dbCollection = Substitute.For<IActiveDatabasePathsProvider>();
            dbCollection.ActiveDatabases.Returns(ImmutableList.Create(dbPath));

            DisplayEventModel dbDisplayEvent;
            DisplayEventModel unknownDisplayEvent;

            using (var resolver = new EventResolver(dbCollection))
            {
                var dbEventRecord = new EventRecord
                {
                    ProviderName = DBProviderName,
                    Id = EventId,
                    ComputerName = "TestComputer",
                    LogName = Constants.ApplicationLogName
                };

                var unknownEventRecord = new EventRecord
                {
                    ProviderName = unknownProviderName,
                    Id = EventId,
                    ComputerName = "TestComputer",
                    LogName = Constants.ApplicationLogName
                };

                // Act
                dbDisplayEvent = resolver.ResolveEvent(dbEventRecord);
                unknownDisplayEvent = resolver.ResolveEvent(unknownEventRecord);
            }

            // Assert
            Assert.NotNull(dbDisplayEvent);
            Assert.Equal(DBProviderName, dbDisplayEvent.Source);
            Assert.Contains(DBMessageText, dbDisplayEvent.Description);

            Assert.NotNull(unknownDisplayEvent);
            Assert.Equal(unknownProviderName, unknownDisplayEvent.Source);
            Assert.DoesNotContain(DBMessageText, unknownDisplayEvent.Description);
            Assert.Contains("No matching", unknownDisplayEvent.Description);
            Assert.DoesNotContain("Failed to resolve", unknownDisplayEvent.Description);
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
    public void ResolveEvent_WithNonExistentProvider_ShouldReturnDefaultDescription()
    {
        // Arrange
        using var resolver = new EventResolver();

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

    [Theory]
    [InlineData(Constants.ApplicationLogName)]
    [InlineData(Constants.SystemLogName)]
    [InlineData(Constants.TestProviderName)]
    public void ResolveEvent_WithProvider_ShouldReturnDisplayEventWithMatchingFields(string providerName)
    {
        // Theory verifies CreateEventModel propagates per-record fields and ProviderName.
        // Arrange
        using var resolver = new EventResolver();

        var eventRecord = new EventRecord
        {
            ProviderName = providerName,
            Id = 1000,
            ComputerName = "TestComputer",
            LogName = Constants.ApplicationLogName
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Equal(providerName, displayEvent.Source);
        Assert.Equal(eventRecord.Id, displayEvent.Id);
        Assert.Equal(eventRecord.ComputerName, displayEvent.ComputerName);
    }

    [Fact]
    public void LoadProviderDetails_CalledTwice_ShouldHandleCorrectly()
    {
        // Arrange
        using var resolver = new EventResolver();

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 1000
        };

        // Act
        resolver.LoadProviderDetails(eventRecord);
        var exception = Record.Exception(() => resolver.LoadProviderDetails(eventRecord));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void LoadProviderDetails_ConcurrentCalls_ShouldHandleThreadSafely()
    {
        // Arrange
        using var resolver = new EventResolver();
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

                    resolver.LoadProviderDetails(eventRecord);
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
    public void LoadProviderDetails_WithDatabaseResolver_ShouldResolve()
    {
        // Arrange
        string dbPath = Path.Combine(Path.GetTempPath(), $"Test_{Guid.NewGuid()}.db");

        try
        {
            using (var context = new ProviderDbContext(dbPath, false))
            {
                context.SaveChanges();
            }

            var dbCollection = Substitute.For<IActiveDatabasePathsProvider>();
            dbCollection.ActiveDatabases.Returns(ImmutableList.Create(dbPath));

            var eventRecord = new EventRecord
            {
                ProviderName = "TestProvider",
                Id = 1000
            };

            // Act
            Exception? exception;
            using (var resolver = new EventResolver(dbCollection))
            {
                exception = Record.Exception(() => resolver.LoadProviderDetails(eventRecord));
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
    public void LoadProviderDetails_WithLocalResolver_ShouldResolve()
    {
        // Arrange
        using var resolver = new EventResolver();

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 1000
        };

        // Act
        var exception = Record.Exception(() => resolver.LoadProviderDetails(eventRecord));

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
            using (var context = new ProviderDbContext(dbPath, false))
            {
                context.SaveChanges();
            }

            var dbCollection = Substitute.For<IActiveDatabasePathsProvider>();
            dbCollection.ActiveDatabases.Returns(ImmutableList.Create(dbPath));

            var eventRecord = EventUtils.CreateBasicEvent();

            // Act
            DisplayEventModel localEvent;
            DisplayEventModel databaseEvent;

            using (var localResolver = new EventResolver())
            {
                localEvent = localResolver.ResolveEvent(eventRecord);
            }

            using (var databaseResolver = new EventResolver(dbCollection))
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
