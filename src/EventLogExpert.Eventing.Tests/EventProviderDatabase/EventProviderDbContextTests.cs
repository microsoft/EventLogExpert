// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventProviderDatabase;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Providers;
using Microsoft.Data.Sqlite;
using NSubstitute;

namespace EventLogExpert.Eventing.Tests.EventProviderDatabase;

public sealed class EventProviderDbContextTests : IDisposable
{
    private readonly List<string> _tempDatabases = [];

    [Fact]
    public void Constructor_ShouldCreateNewDatabase()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();

        // Act
        using var context = new EventProviderDbContext(dbPath, false);

        // Assert
        Assert.True(File.Exists(dbPath));
    }

    [Fact]
    public void Constructor_WithLogger_ShouldLogInstantiation()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();
        var logger = Substitute.For<ITraceLogger>();

        // Act
        using var context = new EventProviderDbContext(dbPath, false, logger);

        // Assert
        logger.Received(1).Trace(Arg.Is<string>(s => s.Contains("Instantiating EventProviderDbContext")));
    }

    [Fact]
    public void Constructor_WithReadOnly_ShouldOpenDatabaseInReadOnlyMode()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();

        using (var context = new EventProviderDbContext(dbPath, false))
        {
            context.SaveChanges();
        }

        // Act
        using var readOnlyContext = new EventProviderDbContext(dbPath, true);

        // Assert
        Assert.NotNull(readOnlyContext.ProviderDetails);

        var exception = Record.Exception(() =>
            readOnlyContext.ProviderDetails.Add(new ProviderDetails { ProviderName = "Test" }));

        Assert.Null(exception);

        exception = Record.Exception(() => readOnlyContext.SaveChanges());
        Assert.NotNull(exception);
    }

    [Fact]
    public void Database_ShouldSupportConcurrentReads()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();

        using (var context = new EventProviderDbContext(dbPath, false))
        {
            for (int i = 0; i < 10; i++)
            {
                context.ProviderDetails.Add(new ProviderDetails
                {
                    ProviderName = $"Provider{i}",
                    Messages = [],
                    Parameters = [],
                    Events = [],
                    Keywords = new Dictionary<long, string>(),
                    Opcodes = new Dictionary<int, string>(),
                    Tasks = new Dictionary<int, string>()
                });
            }

            context.SaveChanges();
        }

        var exceptions = new Exception?[10];
        var results = new int[10];

        // Act
        Parallel.For(0, 10, i =>
            {
                try
                {
                    using var context = new EventProviderDbContext(dbPath, true);
                    results[i] = context.ProviderDetails.Count();
                }
                catch (Exception ex)
                {
                    exceptions[i] = ex;
                }
            });

        // Assert
        Assert.All(exceptions, ex => Assert.Null(ex));
        Assert.All(results, count => Assert.Equal(10, count));
    }

    public void Dispose()
    {
        foreach (var dbPath in _tempDatabases)
        {
            DeleteDatabaseFile(dbPath);
        }
    }

    [Fact]
    public void IsUpgradeNeeded_ShouldLogResult()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();
        var logger = Substitute.For<ITraceLogger>();

        using var context = new EventProviderDbContext(dbPath, false, logger);

        // Act
        var result = context.IsUpgradeNeeded();

        // Assert
        logger.Received(1).Trace(Arg.Is<string>(s =>
            s.Contains(nameof(EventProviderDbContext.IsUpgradeNeeded)) &&
            s.Contains("needsV2Upgrade") &&
            s.Contains("needsV3Upgrade")));
    }

    [Fact]
    public void IsUpgradeNeeded_WithNewDatabase_ShouldReturnFalseFalse()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();

        using var context = new EventProviderDbContext(dbPath, false);

        // Act
        var (needsV2, needsV3) = context.IsUpgradeNeeded();

        // Assert
        Assert.False(needsV2);
        Assert.False(needsV3);
    }

    [Fact]
    public void Name_ShouldNotIncludeFileExtension()
    {
        // Arrange
        var dbPath = Path.Combine(Path.GetTempPath(), "Database.With.Multiple.Dots.db");

        _tempDatabases.Add(dbPath);

        // Act
        using var context = new EventProviderDbContext(dbPath, false);

        // Assert
        Assert.Equal("Database.With.Multiple.Dots", context.Name);
        Assert.DoesNotContain(".db", context.Name);
    }

    [Fact]
    public void PerformUpgradeIfNeeded_WithNewDatabase_ShouldDoNothing()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();
        using var context = new EventProviderDbContext(dbPath, false);
        var initialSize = new FileInfo(dbPath).Length;

        // Act
        context.PerformUpgradeIfNeeded();

        // Assert
        var finalSize = new FileInfo(dbPath).Length;
        Assert.Equal(initialSize, finalSize);
    }

    [Fact]
    public void ProviderDetails_Delete_ShouldRemoveRecord()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();
        var providerName = "DeletableProvider";

        using (var context = new EventProviderDbContext(dbPath, false))
        {
            context.ProviderDetails.Add(new ProviderDetails
            {
                ProviderName = providerName,
                Messages = [],
                Parameters = [],
                Events = [],
                Keywords = new Dictionary<long, string>(),
                Opcodes = new Dictionary<int, string>(),
                Tasks = new Dictionary<int, string>()
            });

            context.SaveChanges();
        }

        // Act
        using (var context = new EventProviderDbContext(dbPath, false))
        {
            var provider = context.ProviderDetails.First(p => p.ProviderName == providerName);
            context.ProviderDetails.Remove(provider);
            context.SaveChanges();
        }

        // Assert
        using (var context = new EventProviderDbContext(dbPath, true))
        {
            Assert.Empty(context.ProviderDetails.Where(p => p.ProviderName == providerName));
        }
    }

    [Fact]
    public void ProviderDetails_ShouldBeAccessible()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();

        // Act
        using var context = new EventProviderDbContext(dbPath, false);

        // Assert
        Assert.NotNull(context.ProviderDetails);
        Assert.Empty(context.ProviderDetails);
    }

    [Fact]
    public void ProviderDetails_ShouldCompressLargeData()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();

        var largeMessages = Enumerable.Range(1, 100)
            .Select(i => new MessageModel { RawId = i, Text = new string('A', 1000) })
            .ToList();

        var provider = new ProviderDetails
        {
            ProviderName = "LargeProvider",
            Messages = largeMessages,
            Parameters = [],
            Events = [],
            Keywords = new Dictionary<long, string>(),
            Opcodes = new Dictionary<int, string>(),
            Tasks = new Dictionary<int, string>()
        };

        // Act
        using (var context = new EventProviderDbContext(dbPath, false))
        {
            context.ProviderDetails.Add(provider);
            context.SaveChanges();
        }

        // Assert
        var fileSize = new FileInfo(dbPath).Length;
        Assert.True(fileSize < 100000);

        using (var context = new EventProviderDbContext(dbPath, true))
        {
            var retrieved = context.ProviderDetails.FirstOrDefault(p => p.ProviderName == "LargeProvider");
            Assert.NotNull(retrieved);
            Assert.Equal(100, retrieved.Messages.Count());
        }
    }

    [Fact]
    public void ProviderDetails_Update_ShouldModifyExistingRecord()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();
        var providerName = "UpdateableProvider";

        using (var context = new EventProviderDbContext(dbPath, false))
        {
            context.ProviderDetails.Add(new ProviderDetails
            {
                ProviderName = providerName,
                Messages = [new MessageModel { RawId = 1, Text = "Original" }],
                Parameters = [],
                Events = [],
                Keywords = new Dictionary<long, string>(),
                Opcodes = new Dictionary<int, string>(),
                Tasks = new Dictionary<int, string>()
            });

            context.SaveChanges();
        }

        // Act
        using (var context = new EventProviderDbContext(dbPath, false))
        {
            var provider = context.ProviderDetails.First(p => p.ProviderName == providerName);
            provider.Messages = [new MessageModel { RawId = 1, Text = "Updated" }];
            context.SaveChanges();
        }

        // Assert
        using (var context = new EventProviderDbContext(dbPath, true))
        {
            var provider = context.ProviderDetails.First(p => p.ProviderName == providerName);
            Assert.Equal("Updated", provider.Messages.First().Text);
        }
    }

    [Fact]
    public void ProviderDetails_WithComplexData_ShouldPreserveAllFields()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();

        var provider = new ProviderDetails
        {
            ProviderName = "ComplexProvider",
            Messages =
            [
                new MessageModel { RawId = 1, Text = "Message1" },
                new MessageModel { RawId = 2, Text = "Message2" },
                new MessageModel { RawId = 3, Text = "Message3" }
            ],
            Parameters =
            [
                new MessageModel { RawId = 10, Text = "Param1" },
                new MessageModel { RawId = 11, Text = "Param2" }
            ],
            Events =
            [
                new EventModel { Id = 100, Description = "Event1" },
                new EventModel { Id = 101, Description = "Event2" }
            ],
            Keywords = new Dictionary<long, string>
            {
                { 1L, "Keyword1" },
                { 2L, "Keyword2" },
                { 4L, "Keyword3" }
            },
            Opcodes = new Dictionary<int, string>
            {
                { 1, "Opcode1" },
                { 2, "Opcode2" }
            },
            Tasks = new Dictionary<int, string>
            {
                { 1, "Task1" },
                { 2, "Task2" },
                { 3, "Task3" }
            }
        };

        // Act
        using (var context = new EventProviderDbContext(dbPath, false))
        {
            context.ProviderDetails.Add(provider);
            context.SaveChanges();
        }

        // Assert
        using (var context = new EventProviderDbContext(dbPath, true))
        {
            var retrieved = context.ProviderDetails.First(p => p.ProviderName == "ComplexProvider");
            Assert.Equal(3, retrieved.Messages.Count());
            Assert.Equal(2, retrieved.Parameters.Count());
            Assert.Equal(2, retrieved.Events.Count());
            Assert.Equal(3, retrieved.Keywords.Count);
            Assert.Equal(2, retrieved.Opcodes.Count);
            Assert.Equal(3, retrieved.Tasks.Count);
            Assert.Equal("Message1", retrieved.Messages.First().Text);
            Assert.Equal("Param1", retrieved.Parameters.First().Text);
            Assert.Equal("Event1", retrieved.Events.First().Description);
            Assert.Equal("Keyword1", retrieved.Keywords[1L]);
            Assert.Equal("Opcode1", retrieved.Opcodes[1]);
            Assert.Equal("Task1", retrieved.Tasks[1]);
        }
    }

    [Fact]
    public void ProviderDetails_WithEmptyCollections_ShouldPersist()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();

        var provider = new ProviderDetails
        {
            ProviderName = "EmptyProvider",
            Messages = [],
            Parameters = [],
            Events = [],
            Keywords = new Dictionary<long, string>(),
            Opcodes = new Dictionary<int, string>(),
            Tasks = new Dictionary<int, string>()
        };

        // Act
        using (var context = new EventProviderDbContext(dbPath, false))
        {
            context.ProviderDetails.Add(provider);
            context.SaveChanges();
        }

        // Assert
        using (var context = new EventProviderDbContext(dbPath, true))
        {
            var retrieved = context.ProviderDetails.First(p => p.ProviderName == "EmptyProvider");
            Assert.NotNull(retrieved);
            Assert.Empty(retrieved.Messages);
            Assert.Empty(retrieved.Parameters);
            Assert.Empty(retrieved.Events);
            Assert.Empty(retrieved.Keywords);
            Assert.Empty(retrieved.Opcodes);
            Assert.Empty(retrieved.Tasks);
        }
    }

    [Fact]
    public void ProviderDetails_WithMultipleProviders_ShouldRetrieveAll()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();

        var providers = Enumerable.Range(1, 5)
            .Select(i => new ProviderDetails
            {
                ProviderName = $"Provider{i}",
                Messages = [],
                Parameters = [],
                Events = [],
                Keywords = new Dictionary<long, string>(),
                Opcodes = new Dictionary<int, string>(),
                Tasks = new Dictionary<int, string>()
            })
            .ToList();

        // Act
        using (var context = new EventProviderDbContext(dbPath, false))
        {
            foreach (var provider in providers)
            {
                context.ProviderDetails.Add(provider);
            }

            context.SaveChanges();
        }

        // Assert
        using (var context = new EventProviderDbContext(dbPath, true))
        {
            Assert.Equal(5, context.ProviderDetails.Count());
        }
    }

    [Fact]
    public void ProviderDetails_WithSpecialCharacters_ShouldPersist()
    {
        // Arrange
        var dbPath = CreateTempDatabasePath();

        var provider = new ProviderDetails
        {
            ProviderName = "Special\"Provider'With<>Chars",
            Messages = [new MessageModel { RawId = 1, Text = "Message with \"quotes\" and 'apostrophes'" }],
            Parameters = [],
            Events = [],
            Keywords = new Dictionary<long, string>(),
            Opcodes = new Dictionary<int, string>(),
            Tasks = new Dictionary<int, string>()
        };

        // Act
        using (var context = new EventProviderDbContext(dbPath, false))
        {
            context.ProviderDetails.Add(provider);
            context.SaveChanges();
        }

        // Assert
        using (var context = new EventProviderDbContext(dbPath, true))
        {
            var retrieved = context.ProviderDetails.First(p => p.ProviderName == "Special\"Provider'With<>Chars");
            Assert.NotNull(retrieved);
            Assert.Equal("Message with \"quotes\" and 'apostrophes'", retrieved.Messages.First().Text);
        }
    }

    private static void DeleteDatabaseFile(string path)
    {
        try
        {
            if (!File.Exists(path)) { return; }

            SqliteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            for (int i = 0; i < 10; i++)
            {
                try
                {
                    File.Delete(path);
                    break;
                }
                catch (IOException)
                {
                    Thread.Sleep(200);
                }
            }
        }
        catch
        {
            // Cleanup is best effort
        }
    }

    private string CreateTempDatabasePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_db_{Guid.NewGuid()}.db");
        _tempDatabases.Add(path);
        return path;
    }
}
