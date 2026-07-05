// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Tests.TestUtils.Constants;

namespace EventLogExpert.Filtering.Tests.EventData;

[Collection("EventPropertyValuesCache")]
public sealed class EventPropertyValuesCacheTests : IDisposable
{
    public EventPropertyValuesCacheTests() => EventPropertyValuesCache.Clear();

    public void Dispose() => EventPropertyValuesCache.Clear();

    [Fact]
    public void GetValues_DifferentSnapshotKey_RecomputesValues()
    {
        var snapshotA = new object();
        var snapshotB = new object();

        var idsA = EventPropertyValuesCache.GetValues(snapshotA, [CreateTestEvent(100)], EventProperty.Id);
        var idsB = EventPropertyValuesCache.GetValues(
            snapshotB,
            [CreateTestEvent(100), CreateTestEvent(200)],
            EventProperty.Id);

        Assert.Equal(["100"], idsA);
        Assert.Equal(["100", "200"], idsB);
    }

    [Fact]
    public void GetValues_DistinctSnapshotKeys_ProduceDistinctCacheEntries()
    {
        var snapshotA = new object();
        var snapshotB = new object();
        var eventsA = new List<ResolvedEvent> { CreateTestEvent(100), CreateTestEvent(200) };
        var eventsB = new List<ResolvedEvent> { CreateTestEvent(300) };

        var idsA = EventPropertyValuesCache.GetValues(snapshotA, eventsA, EventProperty.Id);
        var idsB = EventPropertyValuesCache.GetValues(snapshotB, eventsB, EventProperty.Id);
        var idsASecond = EventPropertyValuesCache.GetValues(snapshotA, eventsA, EventProperty.Id);

        Assert.Equal(["100", "200"], idsA);
        Assert.Equal(["300"], idsB);
        Assert.True(idsA == idsASecond, "Same snapshot key must return the cached ImmutableArray instance.");
        Assert.False(idsA == idsB, "Distinct snapshot keys must yield distinct cache entries.");
    }

    [Fact]
    public void GetValues_LevelField_ReturnsAllSeverityLevelNames()
    {
        var items = EventPropertyValuesCache.GetValues(new object(), [], EventProperty.Level);

        Assert.Equal(Enum.GetNames<SeverityLevel>(), items);
    }

    [Fact]
    public void GetValues_LogDerivedField_ReturnsDistinctSortedValues()
    {
        var snapshot = new object();
        var events = new List<ResolvedEvent>
        {
            CreateTestEvent(200, "Bravo"),
            CreateTestEvent(100, "Alpha"),
            CreateTestEvent(100, "Alpha"),
            CreateTestEvent(300, "Charlie")
        };

        var ids = EventPropertyValuesCache.GetValues(snapshot, events, EventProperty.Id);
        var sources = EventPropertyValuesCache.GetValues(snapshot, events, EventProperty.Source);

        Assert.Equal(["100", "200", "300"], ids);
        Assert.Equal(["Alpha", "Bravo", "Charlie"], sources);
    }

    [Fact]
    public void GetValues_LogNameField_ReturnsDistinctLoadedChannelsAcrossLogs()
    {
        // The LogName value dropdown aggregates each event's channel, distinct + sorted, across all open events.
        var events = new List<ResolvedEvent>
        {
            CreateTestEvent(100, logName: "Application"),
            CreateTestEvent(200, logName: "Application"),
            CreateTestEvent(4624, logName: "Security", owningLog: Constants.LogNameLog2)
        };

        var logNames = EventPropertyValuesCache.GetValues(new object(), events, EventProperty.LogName);

        Assert.Equal(["Application", "Security"], logNames);
    }

    [Fact]
    public void GetValues_SameSnapshotKeyAndField_ReturnsCachedInstance()
    {
        var snapshot = new object();
        var events = new List<ResolvedEvent> { CreateTestEvent(100) };

        var first = EventPropertyValuesCache.GetValues(snapshot, events, EventProperty.Id);
        var second = EventPropertyValuesCache.GetValues(snapshot, events, EventProperty.Id);

        Assert.True(first == second, "ImmutableArray instance should be reused for the same snapshot key/Property.");
    }

    [Fact]
    public void GetValues_UnsupportedField_ReturnsEmpty()
    {
        var items = EventPropertyValuesCache.GetValues(new object(), [CreateTestEvent()], EventProperty.Description);

        Assert.Empty(items);
    }

    private static ResolvedEvent CreateTestEvent(
        int id = 1,
        string source = "TestSource",
        string logName = "Application",
        string owningLog = Constants.LogNameTestLog) =>
        new(owningLog, LogPathType.Channel)
        {
            Id = id,
            Source = source,
            Level = "Information",
            Description = "Test description",
            ComputerName = "TestComputer",
            TaskCategory = "TestCategory",
            LogName = logName,
            TimeCreated = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            Keywords = []
        };
}
