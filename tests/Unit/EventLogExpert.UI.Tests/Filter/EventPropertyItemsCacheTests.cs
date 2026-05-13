// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.UI.EventLog;
using EventLogExpert.UI.Filter;
using EventLogExpert.UI.Tests.TestUtils;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.Filter;

public sealed class EventPropertyItemsCacheTests : IDisposable
{
    public EventPropertyItemsCacheTests() => EventPropertyItemsCache.Clear();

    public void Dispose() => EventPropertyItemsCache.Clear();

    [Fact]
    public void GetItems_DifferentSnapshot_RecomputesValues()
    {
        var snapshotA = ImmutableDictionary<string, EventLogData>.Empty.Add(
            Constants.LogNameTestLog,
            new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, [EventUtils.CreateTestEvent(id: 100)]));

        var snapshotB = snapshotA.Add(
            Constants.LogNameLog2,
            new EventLogData(Constants.LogNameLog2, LogPathType.Channel, [EventUtils.CreateTestEvent(id: 200)]));

        var idsA = EventPropertyItemsCache.GetItems(snapshotA, EventProperty.Id);
        var idsB = EventPropertyItemsCache.GetItems(snapshotB, EventProperty.Id);

        Assert.Equal(["100"], idsA);
        Assert.Equal(["100", "200"], idsB);
    }

    [Fact]
    public void GetItems_LevelField_ReturnsAllSeverityLevelNames()
    {
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty;

        var items = EventPropertyItemsCache.GetItems(activeLogs, EventProperty.Level);

        Assert.Equal(Enum.GetNames<SeverityLevel>(), items);
    }

    [Fact]
    public void GetItems_LogDerivedField_ReturnsDistinctSortedValues()
    {
        var events = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(id: 200, source: "Bravo"),
            EventUtils.CreateTestEvent(id: 100, source: "Alpha"),
            EventUtils.CreateTestEvent(id: 100, source: "Alpha"),
            EventUtils.CreateTestEvent(id: 300, source: "Charlie")
        };

        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, events);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var ids = EventPropertyItemsCache.GetItems(activeLogs, EventProperty.Id);
        var sources = EventPropertyItemsCache.GetItems(activeLogs, EventProperty.Source);

        Assert.Equal(["100", "200", "300"], ids);
        Assert.Equal(["Alpha", "Bravo", "Charlie"], sources);
    }

    [Fact]
    public void GetItems_SameSnapshotAndField_ReturnsCachedInstance()
    {
        var logData = new EventLogData(
            Constants.LogNameTestLog,
            LogPathType.Channel,
            [EventUtils.CreateTestEvent(id: 100)]);

        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var first = EventPropertyItemsCache.GetItems(activeLogs, EventProperty.Id);
        var second = EventPropertyItemsCache.GetItems(activeLogs, EventProperty.Id);

        Assert.True(first == second, "ImmutableArray instance should be reused for the same snapshot/Property.");
    }

    [Fact]
    public void GetItems_UnsupportedField_ReturnsEmpty()
    {
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(
            Constants.LogNameTestLog,
            new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, [EventUtils.CreateTestEvent()]));

        var items = EventPropertyItemsCache.GetItems(activeLogs, EventProperty.Description);

        Assert.Empty(items);
    }
}
