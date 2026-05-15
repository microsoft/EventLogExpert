// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Tests.TestUtils.Constants;
using System.Collections.Immutable;

namespace EventLogExpert.Filtering.Tests;

public sealed class EventPropertyItemsCacheTests : IDisposable
{
    public EventPropertyItemsCacheTests() => EventPropertyItemsCache.Clear();

    public void Dispose() => EventPropertyItemsCache.Clear();

    [Fact]
    public void GetItems_DifferentSnapshot_RecomputesValues()
    {
        // Arrange
        var snapshotA = ImmutableDictionary<string, EventLogData>.Empty.Add(
            Constants.LogNameTestLog,
            new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, [CreateTestEvent(100)]));

        var snapshotB = snapshotA.Add(
            Constants.LogNameLog2,
            new EventLogData(Constants.LogNameLog2, LogPathType.Channel, [CreateTestEvent(200)]));

        // Act
        var idsA = EventPropertyItemsCache.GetItems(snapshotA, EventProperty.Id);
        var idsB = EventPropertyItemsCache.GetItems(snapshotB, EventProperty.Id);

        // Assert
        Assert.Equal(["100"], idsA);
        Assert.Equal(["100", "200"], idsB);
    }

    [Fact]
    public void GetItems_LevelField_ReturnsAllSeverityLevelNames()
    {
        // Arrange
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty;

        // Act
        var items = EventPropertyItemsCache.GetItems(activeLogs, EventProperty.Level);

        // Assert
        Assert.Equal(Enum.GetNames<SeverityLevel>(), items);
    }

    [Fact]
    public void GetItems_LogDerivedField_ReturnsDistinctSortedValues()
    {
        // Arrange
        var events = new List<ResolvedEvent>
        {
            CreateTestEvent(200, "Bravo"),
            CreateTestEvent(100, "Alpha"),
            CreateTestEvent(100, "Alpha"),
            CreateTestEvent(300, "Charlie")
        };

        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, events);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        // Act
        var ids = EventPropertyItemsCache.GetItems(activeLogs, EventProperty.Id);
        var sources = EventPropertyItemsCache.GetItems(activeLogs, EventProperty.Source);

        // Assert
        Assert.Equal(["100", "200", "300"], ids);
        Assert.Equal(["Alpha", "Bravo", "Charlie"], sources);
    }

    [Fact]
    public void GetItems_SameSnapshotAndField_ReturnsCachedInstance()
    {
        // Arrange
        var logData = new EventLogData(
            Constants.LogNameTestLog,
            LogPathType.Channel,
            [CreateTestEvent(100)]);

        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        // Act
        var first = EventPropertyItemsCache.GetItems(activeLogs, EventProperty.Id);
        var second = EventPropertyItemsCache.GetItems(activeLogs, EventProperty.Id);

        // Assert
        Assert.True(first == second, "ImmutableArray instance should be reused for the same snapshot/Property.");
    }

    [Fact]
    public void GetItems_SnapshotMutatedViaWith_ProducesDistinctCacheEntry()
    {
        // Arrange
        var original = new EventLogData(
            Constants.LogNameTestLog,
            LogPathType.Channel,
            [CreateTestEvent(100), CreateTestEvent(200)]);

        var snapshotA = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, original);
        var snapshotB = snapshotA.SetItem(
            Constants.LogNameTestLog,
            original with { Events = [CreateTestEvent(300)] });

        // Act
        var idsA = EventPropertyItemsCache.GetItems(snapshotA, EventProperty.Id);
        var idsB = EventPropertyItemsCache.GetItems(snapshotB, EventProperty.Id);
        var idsASecond = EventPropertyItemsCache.GetItems(snapshotA, EventProperty.Id);

        // Assert
        Assert.Equal(["100", "200"], idsA);
        Assert.Equal(["300"], idsB);
        Assert.True(idsA == idsASecond, "Same snapshot reference must return cached ImmutableArray instance.");
        Assert.False(idsA == idsB, "Distinct snapshot references must yield distinct cache entries.");
    }

    [Fact]
    public void GetItems_UnsupportedField_ReturnsEmpty()
    {
        // Arrange
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(
            Constants.LogNameTestLog,
            new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, [CreateTestEvent()]));

        // Act
        var items = EventPropertyItemsCache.GetItems(activeLogs, EventProperty.Description);

        // Assert
        Assert.Empty(items);
    }

    private static ResolvedEvent CreateTestEvent(int id = 1, string source = "TestSource") =>
        new(Constants.LogNameTestLog, LogPathType.Channel)
        {
            Id = id,
            Source = source,
            Level = "Information",
            Description = "Test description",
            ComputerName = "TestComputer",
            TaskCategory = "TestCategory",
            LogName = "Application",
            TimeCreated = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            Keywords = []
        };
}
