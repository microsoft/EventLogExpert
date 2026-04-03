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
using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace EventLogExpert.Eventing.Tests.EventResolvers;

public sealed class EventProviderDatabaseEventResolverTests
{
    [Fact]
    public void Constructor_WithCacheAndLogger_ShouldCreateInstance()
    {
        // Arrange
        var dbCollection = Substitute.For<IDatabaseCollectionProvider>();
        dbCollection.ActiveDatabases.Returns([]);
        var cache = Substitute.For<IEventResolverCache>();
        var logger = Substitute.For<ITraceLogger>();

        // Act
        using var resolver = new EventProviderDatabaseEventResolver(dbCollection, cache, logger);

        // Assert
        Assert.NotNull(resolver);
    }

    [Fact]
    public void Constructor_WithLogger_ShouldLogDatabasePaths()
    {
        // Arrange
        var dbCollection = Substitute.For<IDatabaseCollectionProvider>();
        dbCollection.ActiveDatabases.Returns([Constants.NonExistentDatabaseFullPath]);
        var logger = Substitute.For<ITraceLogger>();

        // Act
        // This will throw FileNotFoundException, but we can still check logging
        try
        {
            using var resolver = new EventProviderDatabaseEventResolver(dbCollection, logger: logger);
        }
        catch (FileNotFoundException)
        {
            // Expected
        }

        // Assert
        logger.Received(1).Trace(Arg.Is<string>(s => s.Contains("LoadDatabases") && s.Contains("databasePaths")));
        logger.Received(1).Trace(Arg.Is<string>(s => s.Contains(Constants.NonExistentDatabaseFullPath)));
    }

    [Fact]
    public void Constructor_WithLogger_ShouldLogInstantiation()
    {
        // Arrange
        var dbCollection = Substitute.For<IDatabaseCollectionProvider>();
        dbCollection.ActiveDatabases.Returns([]);
        var logger = Substitute.For<ITraceLogger>();

        // Act
        using var resolver = new EventProviderDatabaseEventResolver(dbCollection, logger: logger);

        // Assert
        logger.Received(1).Trace(Arg.Is<string>(s => s.Contains("EventProviderDatabaseEventResolver")));
    }

    [Fact]
    public void Constructor_WithNonExistentDatabase_ShouldThrowFileNotFoundException()
    {
        // Arrange
        string nonExistentPath = Path.Combine(Path.GetTempPath(), $"NonExistent_{Guid.NewGuid()}.db");
        var dbCollection = Substitute.For<IDatabaseCollectionProvider>();
        dbCollection.ActiveDatabases.Returns(ImmutableList.Create(nonExistentPath));

        // Act
        var exception = Assert.Throws<FileNotFoundException>(() =>
            new EventProviderDatabaseEventResolver(dbCollection));

        // Assert
        Assert.Contains(nonExistentPath, exception.Message);
    }

    [Fact]
    public void Constructor_WithNullCache_ShouldCreateInstance()
    {
        // Arrange
        var dbCollection = Substitute.For<IDatabaseCollectionProvider>();
        dbCollection.ActiveDatabases.Returns([]);

        // Act
        using var resolver = new EventProviderDatabaseEventResolver(dbCollection, null);

        // Assert
        Assert.NotNull(resolver);
    }

    [Fact]
    public void Constructor_WithNullDatabaseCollection_ShouldThrowArgumentNullException()
    {
        // Act
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new EventProviderDatabaseEventResolver(null!));

        // Assert
        Assert.Equal("dbCollection", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldCreateInstance()
    {
        // Arrange
        var dbCollection = Substitute.For<IDatabaseCollectionProvider>();
        dbCollection.ActiveDatabases.Returns([]);

        // Act
        using var resolver = new EventProviderDatabaseEventResolver(dbCollection, logger: null);

        // Assert
        Assert.NotNull(resolver);
    }

    [Fact]
    public void Constructor_WithValidDatabase_ShouldLoadDatabase()
    {
        // Arrange
        string dbPath = Path.Combine(Path.GetTempPath(), $"Test_{Guid.NewGuid()}.db");

        try
        {
            using (var context = new EventProviderDbContext(dbPath, false))
            {
                context.ProviderDetails.Add(new ProviderDetails { ProviderName = Constants.TestProviderName });
                context.SaveChanges();
            }

            var dbCollection = Substitute.For<IDatabaseCollectionProvider>();
            dbCollection.ActiveDatabases.Returns(ImmutableList.Create(dbPath));

            // Act
            using var resolver = new EventProviderDatabaseEventResolver(dbCollection);

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
        var dbCollection = Substitute.For<IDatabaseCollectionProvider>();
        dbCollection.ActiveDatabases.Returns([]);
        var resolver = new EventProviderDatabaseEventResolver(dbCollection);

        // Act & Assert
        resolver.Dispose();
        resolver.Dispose(); // Second call should not throw
        resolver.Dispose(); // Third call should not throw
    }

    [Fact]
    public async Task Dispose_ConcurrentWithResolveProviderDetails_ShouldHandleThreadSafely()
    {
        // Arrange
        string dbPath = Path.Combine(Path.GetTempPath(), $"Test_{Guid.NewGuid()}.db");

        try
        {
            await using (var context = new EventProviderDbContext(dbPath, false))
            {
                for (int i = 0; i < 100; i++)
                {
                    context.ProviderDetails.Add(new ProviderDetails
                    {
                        ProviderName = $"Provider{i}",
                        Messages = new List<MessageModel>()
                    });
                }

                await context.SaveChangesAsync();
            }

            var dbCollection = Substitute.For<IDatabaseCollectionProvider>();
            dbCollection.ActiveDatabases.Returns(ImmutableList.Create(dbPath));
            var resolver = new EventProviderDatabaseEventResolver(dbCollection);

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

                            resolver.ResolveProviderDetails(eventRecord);
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
            tasks.Add(Task.Run(() => resolver.Dispose()));

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
        var dbCollection = Substitute.For<IDatabaseCollectionProvider>();
        dbCollection.ActiveDatabases.Returns([]);
        var resolver = new EventProviderDatabaseEventResolver(dbCollection);

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
    public void Dispose_ThenResolveEvent_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var dbCollection = Substitute.For<IDatabaseCollectionProvider>();
        dbCollection.ActiveDatabases.Returns([]);
        var resolver = new EventProviderDatabaseEventResolver(dbCollection);

        var eventRecord = EventUtils.CreateBasicEvent();

        // Act
        resolver.Dispose();

        // Assert
        Assert.Throws<ObjectDisposedException>(() => resolver.ResolveEvent(eventRecord));
    }

    [Fact]
    public void Dispose_ThenResolveEventViaBaseReference_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var dbCollection = Substitute.For<IDatabaseCollectionProvider>();
        dbCollection.ActiveDatabases.Returns([]);
        EventProviderDatabaseEventResolver resolver = new EventProviderDatabaseEventResolver(dbCollection);

        // Type as base class to verify override (not 'new') is used
        EventResolverBase baseResolver = resolver;

        var eventRecord = EventUtils.CreateBasicEvent();

        // Act
        resolver.Dispose();

        // Assert - This should throw because ResolveEvent is overridden, not hidden with 'new'
        Assert.Throws<ObjectDisposedException>(() => baseResolver.ResolveEvent(eventRecord));
    }

    [Fact]
    public void Dispose_ThenResolveProviderDetails_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var dbCollection = Substitute.For<IDatabaseCollectionProvider>();
        dbCollection.ActiveDatabases.Returns([]);
        var resolver = new EventProviderDatabaseEventResolver(dbCollection);

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

    [Fact]
    public void ResolveProviderDetails_CalledTwiceForSameProvider_ShouldResolveOnlyOnce()
    {
        // Arrange
        string dbPath = Path.Combine(Path.GetTempPath(), $"Test_{Guid.NewGuid()}.db");

        try
        {
            using (var context = new EventProviderDbContext(dbPath, false))
            {
                context.ProviderDetails.Add(new ProviderDetails
                {
                    ProviderName = Constants.TestProviderName,
                    Messages = new List<MessageModel>()
                });

                context.SaveChanges();
            }

            var dbCollection = Substitute.For<IDatabaseCollectionProvider>();
            dbCollection.ActiveDatabases.Returns(ImmutableList.Create(dbPath));
            var logger = Substitute.For<ITraceLogger>();

            using var resolver = new EventProviderDatabaseEventResolver(dbCollection, logger: logger);

            var eventRecord = new EventRecord
            {
                ProviderName = Constants.TestProviderName,
                Id = 1000
            };

            // Act
            resolver.ResolveProviderDetails(eventRecord);
            resolver.ResolveProviderDetails(eventRecord);

            // Assert
            logger.Received(1)
                .Trace(Arg.Is<string>(s => s.Contains("Resolved") && s.Contains(Constants.TestProviderName)));
        }
        finally
        {
            DeleteDatabaseFile(dbPath);
        }
    }

    [Fact]
    public void ResolveProviderDetails_ConcurrentCalls_ShouldHandleThreadSafely()
    {
        // Arrange
        string dbPath = Path.Combine(Path.GetTempPath(), $"Test_{Guid.NewGuid()}.db");

        try
        {
            using (var context = new EventProviderDbContext(dbPath, false))
            {
                for (int i = 0; i < 10; i++)
                {
                    context.ProviderDetails.Add(new ProviderDetails
                    {
                        ProviderName = $"Provider{i}",
                        Messages = new List<MessageModel>()
                    });
                }

                context.SaveChanges();
            }

            var dbCollection = Substitute.For<IDatabaseCollectionProvider>();
            dbCollection.ActiveDatabases.Returns(ImmutableList.Create(dbPath));

            using var resolver = new EventProviderDatabaseEventResolver(dbCollection);

            // Act
            var exception = Record.Exception(() =>
                Parallel.For(0, 10, i =>
                {
                    var eventRecord = new EventRecord
                    {
                        ProviderName = $"Provider{i}",
                        Id = (ushort)(1000 + i)
                    };

                    resolver.ResolveProviderDetails(eventRecord);
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
    public void ResolveProviderDetails_MultipleConcurrentProvidersFromDifferentDatabases_ShouldHandleCorrectly()
    {
        // Arrange
        string dbPath1 = Path.Combine(Path.GetTempPath(), $"DB1_{Guid.NewGuid()}.db");
        string dbPath2 = Path.Combine(Path.GetTempPath(), $"DB2_{Guid.NewGuid()}.db");

        try
        {
            using (var context1 = new EventProviderDbContext(dbPath1, false))
            {
                for (int i = 0; i < 5; i++)
                {
                    context1.ProviderDetails.Add(new ProviderDetails
                    {
                        ProviderName = $"DB1Provider{i}",
                        Messages = new List<MessageModel>()
                    });
                }

                context1.SaveChanges();
            }

            using (var context2 = new EventProviderDbContext(dbPath2, false))
            {
                for (int i = 0; i < 5; i++)
                {
                    context2.ProviderDetails.Add(new ProviderDetails
                    {
                        ProviderName = $"DB2Provider{i}",
                        Messages = new List<MessageModel>()
                    });
                }

                context2.SaveChanges();
            }

            var dbCollection = Substitute.For<IDatabaseCollectionProvider>();
            dbCollection.ActiveDatabases.Returns(ImmutableList.Create(dbPath1, dbPath2));

            using var resolver = new EventProviderDatabaseEventResolver(dbCollection);

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

                    resolver.ResolveProviderDetails(eventRecord);
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
    public void ResolveProviderDetails_WithCaseInsensitiveProviderName_ShouldResolve()
    {
        // Arrange
        string dbPath = Path.Combine(Path.GetTempPath(), $"Test_{Guid.NewGuid()}.db");

        try
        {
            using (var context = new EventProviderDbContext(dbPath, false))
            {
                context.ProviderDetails.Add(new ProviderDetails
                {
                    ProviderName = Constants.TestProviderName,
                    Messages = new List<MessageModel>()
                });

                context.SaveChanges();
            }

            var dbCollection = Substitute.For<IDatabaseCollectionProvider>();
            dbCollection.ActiveDatabases.Returns(ImmutableList.Create(dbPath));
            var logger = Substitute.For<ITraceLogger>();

            using var resolver = new EventProviderDatabaseEventResolver(dbCollection, logger: logger);

            var eventRecord = new EventRecord
            {
                ProviderName = Constants.TestProviderName.ToLower(), // Different case
                Id = 1000
            };

            // Act
            resolver.ResolveProviderDetails(eventRecord);

            // Assert
            logger.Received(1)
                .Trace(Arg.Is<string>(s => s.Contains("Resolved") && s.Contains(Constants.TestProviderName.ToLower())));
        }
        finally
        {
            DeleteDatabaseFile(dbPath);
        }
    }

    [Fact]
    public void ResolveProviderDetails_WithFirstDatabaseContainingProvider_ShouldUseFirstDatabase()
    {
        // Arrange
        string dbPath1 = Path.Combine(Path.GetTempPath(), $"Exchange 2019_{Guid.NewGuid()}.db");
        string dbPath2 = Path.Combine(Path.GetTempPath(), $"Windows 2019_{Guid.NewGuid()}.db");

        try
        {
            using (var context1 = new EventProviderDbContext(dbPath1, false))
            {
                context1.ProviderDetails.Add(new ProviderDetails
                {
                    ProviderName = Constants.TestProviderName,
                    Messages = new List<MessageModel>()
                });

                context1.SaveChanges();
            }

            using (var context2 = new EventProviderDbContext(dbPath2, false))
            {
                context2.ProviderDetails.Add(new ProviderDetails
                {
                    ProviderName = Constants.TestProviderName,
                    Messages = new List<MessageModel>()
                });

                context2.SaveChanges();
            }

            var dbCollection = Substitute.For<IDatabaseCollectionProvider>();
            dbCollection.ActiveDatabases.Returns(ImmutableList.Create(dbPath1, dbPath2));
            var logger = Substitute.For<ITraceLogger>();

            using var resolver = new EventProviderDatabaseEventResolver(dbCollection, logger: logger);

            var eventRecord = new EventRecord
            {
                ProviderName = Constants.TestProviderName,
                Id = 1000
            };

            // Act
            resolver.ResolveProviderDetails(eventRecord);

            // Assert
            // Should use Exchange database (first in sorted order)
            logger.Received(1).Trace(Arg.Is<string>(s =>
                s.Contains("Resolved") &&
                s.Contains(Constants.TestProviderName) &&
                s.Contains("Exchange")));
        }
        finally
        {
            DeleteDatabaseFile(dbPath1);
            DeleteDatabaseFile(dbPath2);
        }
    }

    [Fact]
    public void ResolveProviderDetails_WithKnownProvider_ShouldResolveFromDatabase()
    {
        // Arrange
        string dbPath = Path.Combine(Path.GetTempPath(), $"Test_{Guid.NewGuid()}.db");

        try
        {
            using (var context = new EventProviderDbContext(dbPath, false))
            {
                context.ProviderDetails.Add(new ProviderDetails
                {
                    ProviderName = Constants.TestProviderName,
                    Messages = new List<MessageModel>(),
                    Events = new List<EventModel>()
                });

                context.SaveChanges();
            }

            var dbCollection = Substitute.For<IDatabaseCollectionProvider>();
            dbCollection.ActiveDatabases.Returns(ImmutableList.Create(dbPath));
            var logger = Substitute.For<ITraceLogger>();

            using var resolver = new EventProviderDatabaseEventResolver(dbCollection, logger: logger);

            var eventRecord = new EventRecord
            {
                ProviderName = Constants.TestProviderName,
                Id = 1000
            };

            // Act
            resolver.ResolveProviderDetails(eventRecord);

            // Assert
            logger.Received(1).Trace(Arg.Is<string>(s =>
                s.Contains("Resolved") &&
                s.Contains(Constants.TestProviderName) &&
                s.Contains("database")));
        }
        finally
        {
            DeleteDatabaseFile(dbPath);
        }
    }

    [Fact]
    public void ResolveProviderDetails_WithMultipleDatabases_ShouldCheckAllDatabases()
    {
        // Arrange
        string dbPath1 = Path.Combine(Path.GetTempPath(), $"Test1_{Guid.NewGuid()}.db");
        string dbPath2 = Path.Combine(Path.GetTempPath(), $"Test2_{Guid.NewGuid()}.db");

        try
        {
            using (var context1 = new EventProviderDbContext(dbPath1, false))
            {
                context1.ProviderDetails.Add(new ProviderDetails
                {
                    ProviderName = "Provider1",
                    Messages = new List<MessageModel>()
                });

                context1.SaveChanges();
            }

            using (var context2 = new EventProviderDbContext(dbPath2, false))
            {
                context2.ProviderDetails.Add(new ProviderDetails
                {
                    ProviderName = "Provider2",
                    Messages = new List<MessageModel>()
                });

                context2.SaveChanges();
            }

            var dbCollection = Substitute.For<IDatabaseCollectionProvider>();
            dbCollection.ActiveDatabases.Returns(ImmutableList.Create(dbPath1, dbPath2));

            using var resolver = new EventProviderDatabaseEventResolver(dbCollection);

            var eventRecord1 = new EventRecord { ProviderName = "Provider1", Id = 1000 };
            var eventRecord2 = new EventRecord { ProviderName = "Provider2", Id = 2000 };

            // Act
            var exception = Record.Exception(() =>
            {
                resolver.ResolveProviderDetails(eventRecord1);
                resolver.ResolveProviderDetails(eventRecord2);
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
    public void ResolveProviderDetails_WithProviderInLaterDatabase_ShouldFindProvider()
    {
        // Arrange
        string dbPath1 = Path.Combine(Path.GetTempPath(), $"Empty_{Guid.NewGuid()}.db");
        string dbPath2 = Path.Combine(Path.GetTempPath(), $"WithProvider_{Guid.NewGuid()}.db");

        try
        {
            using (var context1 = new EventProviderDbContext(dbPath1, false))
            {
                // Empty database
                context1.SaveChanges();
            }

            using (var context2 = new EventProviderDbContext(dbPath2, false))
            {
                context2.ProviderDetails.Add(new ProviderDetails
                {
                    ProviderName = Constants.TestProviderName,
                    Messages = new List<MessageModel>()
                });

                context2.SaveChanges();
            }

            var dbCollection = Substitute.For<IDatabaseCollectionProvider>();
            dbCollection.ActiveDatabases.Returns(ImmutableList.Create(dbPath1, dbPath2));
            var logger = Substitute.For<ITraceLogger>();

            using var resolver = new EventProviderDatabaseEventResolver(dbCollection, logger: logger);

            var eventRecord = new EventRecord
            {
                ProviderName = Constants.TestProviderName,
                Id = 1000
            };

            // Act
            resolver.ResolveProviderDetails(eventRecord);

            // Assert
            logger.Received(1).Trace(Arg.Is<string>(s =>
                s.Contains("Resolved") &&
                s.Contains(Constants.TestProviderName)));
        }
        finally
        {
            DeleteDatabaseFile(dbPath1);
            DeleteDatabaseFile(dbPath2);
        }
    }

    [Fact]
    public void ResolveProviderDetails_WithUnknownProvider_ShouldAddEmptyDetails()
    {
        // Arrange
        var dbCollection = Substitute.For<IDatabaseCollectionProvider>();
        dbCollection.ActiveDatabases.Returns([]);

        using var resolver = new EventProviderDatabaseEventResolver(dbCollection);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 1000
        };

        // Act
        var exception = Record.Exception(() =>
        {
            resolver.ResolveProviderDetails(eventRecord);

            // Second call should not throw (provider details should be cached)
            resolver.ResolveProviderDetails(eventRecord);
        });

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void SortDatabases_PreservesDirectory_ShouldReturnFullPaths()
    {
        // Arrange
        var databases = new[]
        {
            @"C:\Databases\Test\Windows 2019.db",
            @"C:\Databases\Prod\Exchange 2016.db"
        };

        // Act
        var sorted = EventProviderDatabaseEventResolver.SortDatabases(databases).ToList();

        // Assert
        Assert.All(sorted, path => Assert.True(Path.IsPathRooted(path)));
        Assert.Contains(sorted, s => s.Contains(@"Databases\Test"));
        Assert.Contains(sorted, s => s.Contains(@"Databases\Prod"));
    }

    [Fact]
    public void SortDatabases_WithComplexVersionStrings_ShouldHandleCorrectly()
    {
        // Arrange
        var databases = new[]
        {
            @"C:\Test\Product v2.0.db",
            @"C:\Test\Product v1.5.db",
            @"C:\Test\Product RC1.db"
        };

        // Act
        var sorted = EventProviderDatabaseEventResolver.SortDatabases(databases).ToList();

        // Assert
        Assert.Equal(3, sorted.Count);
        // Should be sorted by second part descending
        Assert.Equal(@"C:\Test\Product v2.0.db", sorted[0]);
        Assert.Equal(@"C:\Test\Product v1.5.db", sorted[1]);
        Assert.Equal(@"C:\Test\Product RC1.db", sorted[2]);
    }

    [Fact]
    public void SortDatabases_WithEmptyList_ShouldReturnEmptyList()
    {
        // Act
        var sorted = EventProviderDatabaseEventResolver.SortDatabases([]);

        // Assert
        Assert.Empty(sorted);
    }

    [Fact]
    public void SortDatabases_WithMixedVersionsAndNoVersions_ShouldSortCorrectly()
    {
        // Arrange
        var databases = new[]
        {
            @"C:\Test\Windows 2019.db",
            @"C:\Test\Exchange.db",
            @"C:\Test\Windows 2016.db",
            @"C:\Test\Azure 2020.db"
        };

        // Act
        var sorted = EventProviderDatabaseEventResolver.SortDatabases(databases).ToList();

        // Assert
        Assert.Equal(4, sorted.Count);
        Assert.Equal(@"C:\Test\Azure 2020.db", sorted[0]);
        Assert.Equal(@"C:\Test\Exchange.db", sorted[1]);
        Assert.Equal(@"C:\Test\Windows 2019.db", sorted[2]);
        Assert.Equal(@"C:\Test\Windows 2016.db", sorted[3]);
    }

    [Fact]
    public void SortDatabases_WithNoVersion_ShouldSortByProductName()
    {
        // Arrange
        var databases = new[]
        {
            @"C:\Test\Windows.db",
            @"C:\Test\Exchange.db",
            @"C:\Test\Azure.db"
        };

        // Act
        var sorted = EventProviderDatabaseEventResolver.SortDatabases(databases).ToList();

        // Assert
        Assert.Equal(3, sorted.Count);
        Assert.Equal(@"C:\Test\Azure.db", sorted[0]);
        Assert.Equal(@"C:\Test\Exchange.db", sorted[1]);
        Assert.Equal(@"C:\Test\Windows.db", sorted[2]);
    }

    [Fact]
    public void SortDatabases_WithNumericVersions_ShouldSortDescending()
    {
        // Arrange
        var databases = new[]
        {
            @"C:\Test\Product 10.db",
            @"C:\Test\Product 2.db",
            @"C:\Test\Product 20.db"
        };

        // Act
        var sorted = EventProviderDatabaseEventResolver.SortDatabases(databases).ToList();

        // Assert
        // Numeric comparison (descending): 20 > 10 > 2
        Assert.Equal(3, sorted.Count);
        Assert.Equal(@"C:\Test\Product 20.db", sorted[0]);
        Assert.Equal(@"C:\Test\Product 10.db", sorted[1]);
        Assert.Equal(@"C:\Test\Product 2.db", sorted[2]);
    }

    [Fact]
    public void SortDatabases_WithProductNameAndVersion_ShouldSortByProductAscendingVersionDescending()
    {
        // Arrange
        var databases = new[]
        {
            @"C:\Test\Windows 2016.db",
            @"C:\Test\Windows 2019.db",
            @"C:\Test\Exchange 2019.db",
            @"C:\Test\Exchange 2016.db"
        };

        // Act
        var sorted = EventProviderDatabaseEventResolver.SortDatabases(databases).ToList();

        // Assert
        Assert.Equal(4, sorted.Count);
        Assert.Equal(@"C:\Test\Exchange 2019.db", sorted[0]);
        Assert.Equal(@"C:\Test\Exchange 2016.db", sorted[1]);
        Assert.Equal(@"C:\Test\Windows 2019.db", sorted[2]);
        Assert.Equal(@"C:\Test\Windows 2016.db", sorted[3]);
    }

    [Fact]
    public void SortDatabases_WithSingleDatabase_ShouldReturnSameDatabase()
    {
        // Arrange
        var databases = new[] { @"C:\Test\Windows 2019.db" };

        // Act
        var sorted = EventProviderDatabaseEventResolver.SortDatabases(databases).ToList();

        // Assert
        Assert.Single(sorted);
        Assert.Equal(@"C:\Test\Windows 2019.db", sorted[0]);
    }

    private static void DeleteDatabaseFile(string path, int maxRetries = 10)
    {
        if (!File.Exists(path)) { return; }

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                // Clear SQLite connection pool
                SqliteConnection.ClearAllPools();

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                File.Delete(path);
                return;
            }
            catch (IOException) when (i < maxRetries - 1)
            {
                Thread.Sleep(200);
            }
            catch (IOException)
            {
                // If we still can't delete after retries, just ignore - OS will clean up temp files
                return;
            }
        }
    }
}
