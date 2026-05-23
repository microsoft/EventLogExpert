// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Eventing.TestUtils;
using EventLogExpert.Eventing.TestUtils.Constants;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using EventLogExpert.Provider.Resolution;
using EventLogExpert.ProviderDatabase.Context;
using NSubstitute;
using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace EventLogExpert.Eventing.IntegrationTests.Resolvers;

public sealed class EventResolverDatabaseTests
{
    [Fact]
    public void Constructor_WithLogger_ShouldLogDatabasePaths()
    {
        // Arrange
        var dbCollection = Substitute.For<IActiveDatabases>();
        dbCollection.Paths.Returns([Constants.NonExistentDatabaseFullPath]);
        var logger = Substitute.For<ITraceLogger>();

        // Act
        // This will throw FileNotFoundException, but we can still check logging
        try
        {
            using var resolver = new EventResolver(dbCollection, logger: logger, factory: new ProviderDbContextFactory());
        }
        catch (FileNotFoundException)
        {
            // Expected
        }

        // Assert
        logger.Received(1).Debug(Arg.Is<DebugLogHandler>(h => h.ToString().Contains("LoadDatabases") && h.ToString().Contains("databasePaths")));
        logger.Received(1).Debug(Arg.Is<DebugLogHandler>(h => h.ToString().Contains(Constants.NonExistentDatabaseFullPath)));
    }

    [Fact]
    public void Constructor_WithLogger_ShouldLogInstantiation()
    {
        // Arrange
        var dbCollection = Substitute.For<IActiveDatabases>();
        dbCollection.Paths.Returns([]);
        var logger = Substitute.For<ITraceLogger>();

        // Act
        using var resolver = new EventResolver(dbCollection, logger: logger, factory: new ProviderDbContextFactory());

        // Assert
        logger.Received(1).Debug(Arg.Is<DebugLogHandler>(h => h.ToString().Contains("EventResolver")));
    }

    [Fact]
    public void Constructor_WithNonExistentDatabase_ShouldThrowFileNotFoundException()
    {
        // Arrange
        string nonExistentPath = Path.Combine(Path.GetTempPath(), $"NonExistent_{Guid.NewGuid()}.db");
        var dbCollection = Substitute.For<IActiveDatabases>();
        dbCollection.Paths.Returns(ImmutableList.Create(nonExistentPath));

        // Act
        var exception = Assert.Throws<FileNotFoundException>(() =>
            new EventResolver(dbCollection, factory: new ProviderDbContextFactory()));

        // Assert
        Assert.Contains(nonExistentPath, exception.Message);
    }

    [Fact]
    public void Constructor_WithValidDatabase_ShouldLoadDatabase()
    {
        // Arrange
        string dbPath = Path.Combine(Path.GetTempPath(), $"Test_{Guid.NewGuid()}.db");

        try
        {
            using (var context = new ProviderDbContext(dbPath, false))
            {
                context.ProviderDetails.Add(EventUtils.CreateProvider(Constants.TestProviderName));
                context.SaveChanges();
            }

            var dbCollection = Substitute.For<IActiveDatabases>();
            dbCollection.Paths.Returns(ImmutableList.Create(dbPath));

            // Act
            using var resolver = new EventResolver(dbCollection, factory: new ProviderDbContextFactory());

            // Assert
            Assert.NotNull(resolver);
        }
        finally
        {
            DeleteDatabaseFile(dbPath);
        }
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var dbCollection = Substitute.For<IActiveDatabases>();
        dbCollection.Paths.Returns([]);
        var resolver = new EventResolver(dbCollection, factory: new ProviderDbContextFactory());

        // Act & Assert
        resolver.Dispose();
        resolver.Dispose(); // Second call should not throw
        resolver.Dispose(); // Third call should not throw
    }

    [Fact]
    public async Task Dispose_ConcurrentWithLoadProviderDetails_ShouldHandleThreadSafely()
    {
        // Arrange
        string dbPath = Path.Combine(Path.GetTempPath(), $"Test_{Guid.NewGuid()}.db");

        try
        {
            await using (var context = new ProviderDbContext(dbPath, false))
            {
                for (int i = 0; i < 100; i++)
                {
                    context.ProviderDetails.Add(EventUtils.CreateProvider($"Provider{i}"));
                }

                await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            var dbCollection = Substitute.For<IActiveDatabases>();
            dbCollection.Paths.Returns(ImmutableList.Create(dbPath));
            var resolver = new EventResolver(dbCollection, factory: new ProviderDbContextFactory());

            var exceptions = new ConcurrentBag<Exception>();

            // Act - Start concurrent operations and dispose while they're running
            var tasks = Enumerable.Range(0, 10).Select(i =>
                Task.Run(() =>
                {
                    try
                    {
                        for (int j = 0; j < 100; j++)
                        {
                            var eventRecord = new EventRecord
                            {
                                ProviderName = $"Provider{i % 100}",
                                Id = 1000
                            };

                            resolver.LoadProviderDetails(eventRecord);
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        // Expected - operations may fail if Dispose completes first
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                })).ToList();

            // Call Dispose while operations are running - it should just work
            tasks.Add(Task.Run(() => resolver.Dispose(), TestContext.Current.CancellationToken));

            await Task.WhenAll(tasks);

            // Assert - Should not have any unexpected exceptions
            Assert.Empty(exceptions);
        }
        finally
        {
            DeleteDatabaseFile(dbPath);
        }
    }

    [Fact]
    public void Dispose_MultipleConcurrentDisposeCalls_ShouldHandleThreadSafely()
    {
        // Arrange
        var dbCollection = Substitute.For<IActiveDatabases>();
        dbCollection.Paths.Returns([]);
        var resolver = new EventResolver(dbCollection, factory: new ProviderDbContextFactory());

        var exceptions = new ConcurrentBag<Exception>();

        // Act - Multiple threads trying to dispose simultaneously
        Parallel.For(0, 10, i =>
            {
                try
                {
                    resolver.Dispose();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

        // Assert - Should not throw any exceptions
        Assert.Empty(exceptions);
    }

    [Fact]
    public void Dispose_ThenLoadProviderDetails_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var dbCollection = Substitute.For<IActiveDatabases>();
        dbCollection.Paths.Returns([]);
        var resolver = new EventResolver(dbCollection, factory: new ProviderDbContextFactory());

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

    [Fact]
    public void Dispose_ThenResolveEventViaBaseReference_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var dbCollection = Substitute.For<IActiveDatabases>();
        dbCollection.Paths.Returns([]);
        EventResolver resolver = new(dbCollection);

        // Type as base class to verify override (not 'new') is used
        EventResolverBase baseResolver = resolver;

        var eventRecord = EventUtils.CreateBasicEvent();

        // Act
        resolver.Dispose();

        // Assert - This should throw because ResolveEvent is overridden, not hidden with 'new'
        Assert.Throws<ObjectDisposedException>(() => baseResolver.ResolveEvent(eventRecord));
    }

    [Fact]
    public void Dispose_ThenResolveEvent_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var dbCollection = Substitute.For<IActiveDatabases>();
        dbCollection.Paths.Returns([]);
        var resolver = new EventResolver(dbCollection, factory: new ProviderDbContextFactory());

        var eventRecord = EventUtils.CreateBasicEvent();

        // Act
        resolver.Dispose();

        // Assert
        Assert.Throws<ObjectDisposedException>(() => resolver.ResolveEvent(eventRecord));
    }

    [Fact]
    public void LoadProviderDetails_CalledTwiceForSameProvider_ShouldResolveOnlyOnce()
    {
        // Arrange
        string dbPath = Path.Combine(Path.GetTempPath(), $"Test_{Guid.NewGuid()}.db");

        try
        {
            using (var context = new ProviderDbContext(dbPath, false))
            {
                context.ProviderDetails.Add(EventUtils.CreateProvider(Constants.TestProviderName));

                context.SaveChanges();
            }

            var dbCollection = Substitute.For<IActiveDatabases>();
            dbCollection.Paths.Returns(ImmutableList.Create(dbPath));
            var logger = Substitute.For<ITraceLogger>();

            using var resolver = new EventResolver(dbCollection, logger: logger, factory: new ProviderDbContextFactory());

            var eventRecord = new EventRecord
            {
                ProviderName = Constants.TestProviderName,
                Id = 1000
            };

            // Act
            resolver.LoadProviderDetails(eventRecord);
            resolver.LoadProviderDetails(eventRecord);

            // Assert
            logger.Received(1)
                .Debug(Arg.Is<DebugLogHandler>(h => h.ToString().Contains("Resolved") && h.ToString().Contains(Constants.TestProviderName)));
        }
        finally
        {
            DeleteDatabaseFile(dbPath);
        }
    }

    [Fact]
    public void LoadProviderDetails_ConcurrentCalls_ShouldHandleThreadSafely()
    {
        // Arrange
        string dbPath = Path.Combine(Path.GetTempPath(), $"Test_{Guid.NewGuid()}.db");

        try
        {
            using (var context = new ProviderDbContext(dbPath, false))
            {
                for (int i = 0; i < 10; i++)
                {
                    context.ProviderDetails.Add(EventUtils.CreateProvider($"Provider{i}"));
                }

                context.SaveChanges();
            }

            var dbCollection = Substitute.For<IActiveDatabases>();
            dbCollection.Paths.Returns(ImmutableList.Create(dbPath));

            using var resolver = new EventResolver(dbCollection, factory: new ProviderDbContextFactory());

            // Act
            var exception = Record.Exception(() =>
                Parallel.For(0, 10, i =>
                {
                    var eventRecord = new EventRecord
                    {
                        ProviderName = $"Provider{i}",
                        Id = (ushort)(1000 + i)
                    };

                    resolver.LoadProviderDetails(eventRecord);
                }));

            // Assert
            Assert.Null(exception);
        }
        finally
        {
            DeleteDatabaseFile(dbPath);
        }
    }

    [Fact]
    public void LoadProviderDetails_MultipleConcurrentProvidersFromDifferentDatabases_ShouldHandleCorrectly()
    {
        // Arrange
        string dbPath1 = Path.Combine(Path.GetTempPath(), $"DB1_{Guid.NewGuid()}.db");
        string dbPath2 = Path.Combine(Path.GetTempPath(), $"DB2_{Guid.NewGuid()}.db");

        try
        {
            using (var context1 = new ProviderDbContext(dbPath1, false))
            {
                for (int i = 0; i < 5; i++)
                {
                    context1.ProviderDetails.Add(EventUtils.CreateProvider($"DB1Provider{i}"));
                }

                context1.SaveChanges();
            }

            using (var context2 = new ProviderDbContext(dbPath2, false))
            {
                for (int i = 0; i < 5; i++)
                {
                    context2.ProviderDetails.Add(EventUtils.CreateProvider($"DB2Provider{i}"));
                }

                context2.SaveChanges();
            }

            var dbCollection = Substitute.For<IActiveDatabases>();
            dbCollection.Paths.Returns(ImmutableList.Create(dbPath1, dbPath2));

            using var resolver = new EventResolver(dbCollection, factory: new ProviderDbContextFactory());

            // Act
            var exception = Record.Exception(() =>
                Parallel.For(0, 10, i =>
                {
                    var dbPrefix = i < 5 ? "DB1" : "DB2";
                    var providerIndex = i % 5;

                    var eventRecord = new EventRecord
                    {
                        ProviderName = $"{dbPrefix}Provider{providerIndex}",
                        Id = (ushort)(1000 + i)
                    };

                    resolver.LoadProviderDetails(eventRecord);
                }));

            // Assert
            Assert.Null(exception);
        }
        finally
        {
            DeleteDatabaseFile(dbPath1);
            DeleteDatabaseFile(dbPath2);
        }
    }

    [Fact]
    public void LoadProviderDetails_WithCaseInsensitiveProviderName_ShouldResolve()
    {
        // Arrange
        string dbPath = Path.Combine(Path.GetTempPath(), $"Test_{Guid.NewGuid()}.db");

        try
        {
            using (var context = new ProviderDbContext(dbPath, false))
            {
                context.ProviderDetails.Add(EventUtils.CreateProvider(Constants.TestProviderName));

                context.SaveChanges();
            }

            var dbCollection = Substitute.For<IActiveDatabases>();
            dbCollection.Paths.Returns(ImmutableList.Create(dbPath));
            var logger = Substitute.For<ITraceLogger>();

            using var resolver = new EventResolver(dbCollection, logger: logger, factory: new ProviderDbContextFactory());

            var eventRecord = new EventRecord
            {
                ProviderName = Constants.TestProviderName.ToLower(), // Different case
                Id = 1000
            };

            // Act
            resolver.LoadProviderDetails(eventRecord);

            // Assert
            logger.Received(1)
                .Debug(Arg.Is<DebugLogHandler>(h => h.ToString().Contains("Resolved") && h.ToString().Contains(Constants.TestProviderName.ToLower())));
        }
        finally
        {
            DeleteDatabaseFile(dbPath);
        }
    }

    [Fact]
    public void LoadProviderDetails_WithFirstDatabaseContainingProvider_ShouldUseFirstDatabase()
    {
        // Arrange
        string dbPath1 = Path.Combine(Path.GetTempPath(), $"Exchange 2019_{Guid.NewGuid()}.db");
        string dbPath2 = Path.Combine(Path.GetTempPath(), $"Windows 2019_{Guid.NewGuid()}.db");

        try
        {
            using (var context1 = new ProviderDbContext(dbPath1, false))
            {
                context1.ProviderDetails.Add(EventUtils.CreateProvider(Constants.TestProviderName));

                context1.SaveChanges();
            }

            using (var context2 = new ProviderDbContext(dbPath2, false))
            {
                context2.ProviderDetails.Add(EventUtils.CreateProvider(Constants.TestProviderName));

                context2.SaveChanges();
            }

            var dbCollection = Substitute.For<IActiveDatabases>();
            dbCollection.Paths.Returns(ImmutableList.Create(dbPath1, dbPath2));
            var logger = Substitute.For<ITraceLogger>();

            using var resolver = new EventResolver(dbCollection, logger: logger, factory: new ProviderDbContextFactory());

            var eventRecord = new EventRecord
            {
                ProviderName = Constants.TestProviderName,
                Id = 1000
            };

            // Act
            resolver.LoadProviderDetails(eventRecord);

            // Assert
            // Should use Exchange database (first in sorted order)
            logger.Received(1).Debug(Arg.Is<DebugLogHandler>(h =>
                h.ToString().Contains("Resolved") &&
                h.ToString().Contains(Constants.TestProviderName) &&
                h.ToString().Contains("Exchange")));
        }
        finally
        {
            DeleteDatabaseFile(dbPath1);
            DeleteDatabaseFile(dbPath2);
        }
    }

    [Fact]
    public void LoadProviderDetails_WithKnownProvider_ShouldResolveFromDatabase()
    {
        // Arrange
        string dbPath = Path.Combine(Path.GetTempPath(), $"Test_{Guid.NewGuid()}.db");

        try
        {
            using (var context = new ProviderDbContext(dbPath, false))
            {
                context.ProviderDetails.Add(EventUtils.CreateProvider(Constants.TestProviderName));

                context.SaveChanges();
            }

            var dbCollection = Substitute.For<IActiveDatabases>();
            dbCollection.Paths.Returns(ImmutableList.Create(dbPath));
            var logger = Substitute.For<ITraceLogger>();

            using var resolver = new EventResolver(dbCollection, logger: logger, factory: new ProviderDbContextFactory());

            var eventRecord = new EventRecord
            {
                ProviderName = Constants.TestProviderName,
                Id = 1000
            };

            // Act
            resolver.LoadProviderDetails(eventRecord);

            // Assert
            logger.Received(1).Debug(Arg.Is<DebugLogHandler>(h =>
                h.ToString().Contains("Resolved") &&
                h.ToString().Contains(Constants.TestProviderName) &&
                h.ToString().Contains("database")));
        }
        finally
        {
            DeleteDatabaseFile(dbPath);
        }
    }

    [Fact]
    public void LoadProviderDetails_WithMultipleDatabases_ShouldCheckAllDatabases()
    {
        // Arrange
        string dbPath1 = Path.Combine(Path.GetTempPath(), $"Test1_{Guid.NewGuid()}.db");
        string dbPath2 = Path.Combine(Path.GetTempPath(), $"Test2_{Guid.NewGuid()}.db");

        try
        {
            using (var context1 = new ProviderDbContext(dbPath1, false))
            {
                context1.ProviderDetails.Add(EventUtils.CreateProvider("Provider1"));

                context1.SaveChanges();
            }

            using (var context2 = new ProviderDbContext(dbPath2, false))
            {
                context2.ProviderDetails.Add(EventUtils.CreateProvider("Provider2"));

                context2.SaveChanges();
            }

            var dbCollection = Substitute.For<IActiveDatabases>();
            dbCollection.Paths.Returns(ImmutableList.Create(dbPath1, dbPath2));

            using var resolver = new EventResolver(dbCollection, factory: new ProviderDbContextFactory());

            var eventRecord1 = new EventRecord { ProviderName = "Provider1", Id = 1000 };
            var eventRecord2 = new EventRecord { ProviderName = "Provider2", Id = 2000 };

            // Act
            var exception = Record.Exception(() =>
            {
                resolver.LoadProviderDetails(eventRecord1);
                resolver.LoadProviderDetails(eventRecord2);
            });

            // Assert
            Assert.Null(exception);
        }
        finally
        {
            DeleteDatabaseFile(dbPath1);
            DeleteDatabaseFile(dbPath2);
        }
    }

    [Fact]
    public void LoadProviderDetails_WithProviderInLaterDatabase_ShouldFindProvider()
    {
        // Arrange
        string dbPath1 = Path.Combine(Path.GetTempPath(), $"Empty_{Guid.NewGuid()}.db");
        string dbPath2 = Path.Combine(Path.GetTempPath(), $"WithProvider_{Guid.NewGuid()}.db");

        try
        {
            using (var context1 = new ProviderDbContext(dbPath1, false))
            {
                // Empty database
                context1.SaveChanges();
            }

            using (var context2 = new ProviderDbContext(dbPath2, false))
            {
                context2.ProviderDetails.Add(EventUtils.CreateProvider(Constants.TestProviderName));

                context2.SaveChanges();
            }

            var dbCollection = Substitute.For<IActiveDatabases>();
            dbCollection.Paths.Returns(ImmutableList.Create(dbPath1, dbPath2));
            var logger = Substitute.For<ITraceLogger>();

            using var resolver = new EventResolver(dbCollection, logger: logger, factory: new ProviderDbContextFactory());

            var eventRecord = new EventRecord
            {
                ProviderName = Constants.TestProviderName,
                Id = 1000
            };

            // Act
            resolver.LoadProviderDetails(eventRecord);

            // Assert
            logger.Received(1).Debug(Arg.Is<DebugLogHandler>(h =>
                h.ToString().Contains("Resolved") &&
                h.ToString().Contains(Constants.TestProviderName)));
        }
        finally
        {
            DeleteDatabaseFile(dbPath1);
            DeleteDatabaseFile(dbPath2);
        }
    }

    [Fact]
    public void LoadProviderDetails_WithUnknownProvider_ShouldAddEmptyDetails()
    {
        // Arrange
        var dbCollection = Substitute.For<IActiveDatabases>();
        dbCollection.Paths.Returns([]);

        using var resolver = new EventResolver(dbCollection, factory: new ProviderDbContextFactory());

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 1000
        };

        // Act
        var exception = Record.Exception(() =>
        {
            resolver.LoadProviderDetails(eventRecord);

            // Second call should not throw (provider details should be cached)
            resolver.LoadProviderDetails(eventRecord);
        });

        // Assert
        Assert.Null(exception);
    }

    private static void DeleteDatabaseFile(string path) => SqliteTestDb.Delete(path);
}
