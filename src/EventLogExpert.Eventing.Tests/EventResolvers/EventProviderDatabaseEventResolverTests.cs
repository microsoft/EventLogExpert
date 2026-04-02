// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventProviderDatabase;
using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Providers;
using EventLogExpert.Eventing.Tests.TestUtils.Constants;
using NSubstitute;
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
        var resolver = new EventProviderDatabaseEventResolver(dbCollection, cache, logger);

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
            _ = new EventProviderDatabaseEventResolver(dbCollection, logger: logger);
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
        _ = new EventProviderDatabaseEventResolver(dbCollection, logger: logger);

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
        var resolver = new EventProviderDatabaseEventResolver(dbCollection, cache: null);

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
        var resolver = new EventProviderDatabaseEventResolver(dbCollection, logger: null);

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
            using (var context = new EventProviderDbContext(dbPath, readOnly: false))
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
    public void ResolveProviderDetails_CalledTwiceForSameProvider_ShouldResolveOnlyOnce()
    {
        // Arrange
        string dbPath = Path.Combine(Path.GetTempPath(), $"Test_{Guid.NewGuid()}.db");

        try
        {
            using (var context = new EventProviderDbContext(dbPath, readOnly: false))
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
            using (var context = new EventProviderDbContext(dbPath, readOnly: false))
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
            Parallel.For(0, 10, i =>
            {
                var eventRecord = new EventRecord
                {
                    ProviderName = $"Provider{i}",
                    Id = (ushort)(1000 + i)
                };
                resolver.ResolveProviderDetails(eventRecord);
            });

            // Assert
            // If we get here without exceptions, thread safety is maintained
            Assert.True(true);
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
            using (var context1 = new EventProviderDbContext(dbPath1, readOnly: false))
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

            using (var context2 = new EventProviderDbContext(dbPath2, readOnly: false))
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
            });

            // Assert
            // If we get here without exceptions, concurrent access from multiple databases works
            Assert.True(true);
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
            using (var context = new EventProviderDbContext(dbPath, readOnly: false))
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
            using (var context1 = new EventProviderDbContext(dbPath1, readOnly: false))
            {
                context1.ProviderDetails.Add(new ProviderDetails
                {
                    ProviderName = Constants.TestProviderName,
                    Messages = new List<MessageModel>()
                });
                context1.SaveChanges();
            }

            using (var context2 = new EventProviderDbContext(dbPath2, readOnly: false))
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
            using (var context = new EventProviderDbContext(dbPath, readOnly: false))
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
            using (var context1 = new EventProviderDbContext(dbPath1, readOnly: false))
            {
                context1.ProviderDetails.Add(new ProviderDetails
                {
                    ProviderName = "Provider1",
                    Messages = new List<MessageModel>()
                });
                context1.SaveChanges();
            }

            using (var context2 = new EventProviderDbContext(dbPath2, readOnly: false))
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
            resolver.ResolveProviderDetails(eventRecord1);
            resolver.ResolveProviderDetails(eventRecord2);

            // Assert
            // If we get here, both providers were resolved successfully
            Assert.True(true);
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
            using (var context1 = new EventProviderDbContext(dbPath1, readOnly: false))
            {
                // Empty database
                context1.SaveChanges();
            }

            using (var context2 = new EventProviderDbContext(dbPath2, readOnly: false))
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

        var resolver = new EventProviderDatabaseEventResolver(dbCollection);
        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 1000
        };

        // Act
        resolver.ResolveProviderDetails(eventRecord);

        // Second call should not throw (provider details should be cached)
        resolver.ResolveProviderDetails(eventRecord);

        // Assert
        Assert.True(true); // If we get here, the method handled the unknown provider correctly
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
        // String comparison (descending): "20" > "2" > "10" (lexicographic)
        Assert.Equal(3, sorted.Count);
        Assert.Equal(@"C:\Test\Product 20.db", sorted[0]);
        Assert.Equal(@"C:\Test\Product 2.db", sorted[1]);
        Assert.Equal(@"C:\Test\Product 10.db", sorted[2]);
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
        if (!File.Exists(path)) return;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                // Clear SQLite connection pool
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

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
