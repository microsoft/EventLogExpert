// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Eventing.Tests.Common.Events;

/// <summary>
///     Validates the survivor-scoped tick-histogram primitives (
///     <see cref="IEventColumnReader.BucketTimeTicksBySeverity" />,
///     <see cref="IEventColumnReader.BucketTimeTicksByField" />,
///     <see cref="IEventColumnReader.BucketTimeTicksByEventId" />, the <see cref="IEventColumnReader.CountFieldValues" />/
///     <see cref="IEventColumnReader.CountEventIds" /> top-N count passes, and
///     <see cref="IEventColumnReader.TryGetTimeTicksRange" />) over sealed, pending, and seal-boundary-straddling stores.
/// </summary>
public sealed class TimeTicksHistogramTests
{
    private const long ContentVersion = 1;
    private const int Generation = 1;

    private static readonly EventLogId s_logId = EventLogId.Create();
    private static readonly int s_slotCount = LevelSeverity.SlotCount;

    [Fact]
    public void BucketTimeTicksByEventId_AssignsSurvivorsToTargetIdSlotsElseOther()
    {
        var events = new[] { IdEvent(10), IdEvent(10), IdEvent(20), IdEvent(30) };
        var reader = new EventColumnStoreReader(s_logId, EventColumnStore.Build(events, Generation, ContentVersion));
        int[] targets = [10, 20];
        var slotCounts = new int[targets.Length + 1];

        reader.BucketTimeTicksByEventId(AllSurvive(4), 0, 1, 1, targets, slotCounts, CancellationToken.None);

        Assert.Equal(2, slotCounts[0]); // id 10
        Assert.Equal(1, slotCounts[1]); // id 20
        Assert.Equal(1, slotCounts[2]); // Other (id 30)
    }

    [Fact]
    public void BucketTimeTicksByEventId_MatchesANegativeTargetId()
    {
        // A legitimately negative event id must still match its own negative target (the shared SlotForIndex is a pure match;
        // absent pooled targets use int.MinValue, not -1, so this path is unaffected).
        var events = new[] { IdEvent(-1), IdEvent(-1), IdEvent(5) };
        var reader = new EventColumnStoreReader(s_logId, EventColumnStore.Build(events, Generation, ContentVersion));
        int[] targets = [-1];
        var slotCounts = new int[targets.Length + 1];

        reader.BucketTimeTicksByEventId(AllSurvive(3), 0, 1, 1, targets, slotCounts, CancellationToken.None);

        Assert.Equal(2, slotCounts[0]); // both id -1 rows matched target -1
        Assert.Equal(1, slotCounts[1]); // id 5 -> Other
    }

    [Fact]
    public void BucketTimeTicksByField_AssignsSurvivorsToTargetValueSlotsElseOther()
    {
        var events = new[]
        {
            EventWithSource(0, "A"),
            EventWithSource(0, "A"),
            EventWithSource(0, "B"),
            EventWithSource(0, "C")
        };
        var reader = new EventColumnStoreReader(s_logId, EventColumnStore.Build(events, Generation, ContentVersion));
        string[] targets = ["A", "B"];
        var slotCounts = new int[targets.Length + 1];

        reader.BucketTimeTicksByField(AllSurvive(4), 0, 1, 1, EventFieldId.Source, targets, slotCounts, CancellationToken.None);

        Assert.Equal(2, slotCounts[0]); // A
        Assert.Equal(1, slotCounts[1]); // B
        Assert.Equal(1, slotCounts[2]); // Other (C is not a target)
    }

    [Fact]
    public void BucketTimeTicksByField_ClassifiesPendingRowsByValueStraddlingTheSealBoundary()
    {
        EventColumnStore store = EventColumnStore.Build(SourcesAtTick(0, "A", 4096), Generation, ContentVersion)
            .Append([EventWithSource(0, "B"), EventWithSource(0, "Z")]);
        var reader = new EventColumnStoreReader(s_logId, store);
        string[] targets = ["A", "B"];
        var slotCounts = new int[targets.Length + 1];

        reader.BucketTimeTicksByField(AllSurvive(4098), 0, 1, 1, EventFieldId.Source, targets, slotCounts, CancellationToken.None);

        Assert.Equal(4096, slotCounts[0]); // A (sealed)
        Assert.Equal(1, slotCounts[1]);    // B (pending)
        Assert.Equal(1, slotCounts[2]);    // Z -> Other (pending)
    }

    [Fact]
    public void BucketTimeTicksBySeverity_AccumulatesAcrossCallsRatherThanOverwriting()
    {
        IEventColumnReader reader = ReaderFor(0, 0);
        var slotCounts = new int[s_slotCount];

        reader.BucketTimeTicksBySeverity(AllSurvive(2), 0, 1, 1, slotCounts, CancellationToken.None);
        reader.BucketTimeTicksBySeverity(AllSurvive(2), 0, 1, 1, slotCounts, CancellationToken.None);

        Assert.Equal(4, slotCounts[0]);
    }

    [Fact]
    public void BucketTimeTicksBySeverity_AssignsEachSurvivorToItsHalfOpenBucket()
    {
        IEventColumnReader reader = ReaderFor(0, 10, 20, 30);
        var slotCounts = new int[4 * s_slotCount];

        reader.BucketTimeTicksBySeverity(AllSurvive(4), minTicks: 0, bucketSpanTicks: 10, bucketCount: 4, slotCounts, CancellationToken.None);

        // No level is set, so every survivor lands in the unknown slot (0) of its own bucket.
        Assert.Equal(1, slotCounts[0 * s_slotCount]);
        Assert.Equal(1, slotCounts[1 * s_slotCount]);
        Assert.Equal(1, slotCounts[2 * s_slotCount]);
        Assert.Equal(1, slotCounts[3 * s_slotCount]);
    }

    [Fact]
    public void BucketTimeTicksBySeverity_ClampsOutOfRangeTicksToTheEndBuckets()
    {
        // Tick 5 falls below the domain (min 100); tick 1000 falls above the last bucket.
        IEventColumnReader reader = ReaderFor(5, 1000);
        var slotCounts = new int[3 * s_slotCount];

        reader.BucketTimeTicksBySeverity(AllSurvive(2), minTicks: 100, bucketSpanTicks: 10, bucketCount: 3, slotCounts, CancellationToken.None);

        Assert.Equal(1, slotCounts[0 * s_slotCount]);
        Assert.Equal(0, slotCounts[1 * s_slotCount]);
        Assert.Equal(1, slotCounts[2 * s_slotCount]);
    }

    [Fact]
    public void BucketTimeTicksBySeverity_ClassifiesPendingRowsByLevelStraddlingTheSealBoundary()
    {
        EventColumnStore store = EventColumnStore.Build(EventsAtTick(0, 4096), Generation, ContentVersion)
            .Append([EventWith(0, nameof(SeverityLevel.Error)), EventWith(0, nameof(SeverityLevel.Warning))]);

        Assert.Equal(4096, store.SealedCount);
        Assert.Equal(4098, store.Count);

        var reader = new EventColumnStoreReader(s_logId, store);
        var slotCounts = new int[s_slotCount];

        reader.BucketTimeTicksBySeverity(AllSurvive(4098), 0, 1, 1, slotCounts, CancellationToken.None);

        Assert.Equal(4096, slotCounts[0]);
        Assert.Equal(1, slotCounts[(int)SeverityLevel.Error]);
        Assert.Equal(1, slotCounts[(int)SeverityLevel.Warning]);
    }

    [Fact]
    public void BucketTimeTicksBySeverity_CountsOnlySurvivors()
    {
        IEventColumnReader reader = ReaderFor(0, 0, 0, 0);
        var slotCounts = new int[s_slotCount];
        int[] membership = [0, -1, 1, -1];

        reader.BucketTimeTicksBySeverity(membership, 0, 1, 1, slotCounts, CancellationToken.None);

        Assert.Equal(2, slotCounts[0]);
    }

    [Fact]
    public void BucketTimeTicksBySeverity_GroupsSealedSurvivorsIntoTheirLevelSlot()
    {
        var events = new[]
        {
            EventWith(0, nameof(SeverityLevel.Critical)),
            EventWith(0, nameof(SeverityLevel.Error)),
            EventWith(0, nameof(SeverityLevel.Error)),
            EventWith(0, nameof(SeverityLevel.Warning)),
            EventWith(0, nameof(SeverityLevel.Information)),
            EventWith(0, nameof(SeverityLevel.Verbose)),
            EventWith(0, "Custom")
        };

        var reader = new EventColumnStoreReader(s_logId, EventColumnStore.Build(events, Generation, ContentVersion));
        var slotCounts = new int[s_slotCount];

        reader.BucketTimeTicksBySeverity(AllSurvive(events.Length), 0, 1, 1, slotCounts, CancellationToken.None);

        Assert.Equal(1, slotCounts[(int)SeverityLevel.Critical]);
        Assert.Equal(2, slotCounts[(int)SeverityLevel.Error]);
        Assert.Equal(1, slotCounts[(int)SeverityLevel.Warning]);
        Assert.Equal(1, slotCounts[(int)SeverityLevel.Information]);
        Assert.Equal(1, slotCounts[(int)SeverityLevel.Verbose]);
        Assert.Equal(1, slotCounts[0]);
    }

    [Fact]
    public void BucketTimeTicksBySeverity_PlacesABoundaryTickInTheUpperBucket()
    {
        IEventColumnReader reader = ReaderFor(9, 10);
        var slotCounts = new int[2 * s_slotCount];

        reader.BucketTimeTicksBySeverity(AllSurvive(2), 0, 10, 2, slotCounts, CancellationToken.None);

        Assert.Equal(1, slotCounts[0 * s_slotCount]);
        Assert.Equal(1, slotCounts[1 * s_slotCount]);
    }

    [Fact]
    public void BucketTimeTicksBySeverity_ThrowsWhenMembershipLengthDoesNotMatchCount()
    {
        IEventColumnReader reader = ReaderFor(1, 2);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => reader.BucketTimeTicksBySeverity(AllSurvive(1), 0, 1, 1, new int[s_slotCount], CancellationToken.None));
    }

    [Fact]
    public void CountEventIds_TalliesBySurvivorId()
    {
        var events = new[] { IdEvent(10), IdEvent(10), IdEvent(20) };
        var reader = new EventColumnStoreReader(s_logId, EventColumnStore.Build(events, Generation, ContentVersion));
        var counts = new Dictionary<int, int>();

        reader.CountEventIds(AllSurvive(3), counts, CancellationToken.None);

        Assert.Equal(2, counts[10]);
        Assert.Equal(1, counts[20]);
    }

    [Fact]
    public void CountFieldValues_CountsOnlySurvivorsAndAccumulates()
    {
        var events = new[] { EventWithSource(0, "A"), EventWithSource(0, "A"), EventWithSource(0, "A") };
        var reader = new EventColumnStoreReader(s_logId, EventColumnStore.Build(events, Generation, ContentVersion));
        int[] membership = [0, -1, 1];
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        reader.CountFieldValues(membership, EventFieldId.Source, counts, CancellationToken.None);
        reader.CountFieldValues(membership, EventFieldId.Source, counts, CancellationToken.None);

        Assert.Equal(4, counts["A"]); // 2 survivors, tallied twice
    }

    [Fact]
    public void CountFieldValues_TalliesNonEmptyValuesAndSkipsEmpty()
    {
        var events = new[]
        {
            EventWithSource(0, "A"),
            EventWithSource(0, "A"),
            EventWithSource(0, "B"),
            EventWithSource(0, string.Empty)
        };
        var reader = new EventColumnStoreReader(s_logId, EventColumnStore.Build(events, Generation, ContentVersion));
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        reader.CountFieldValues(AllSurvive(4), EventFieldId.Source, counts, CancellationToken.None);

        Assert.Equal(2, counts["A"]);
        Assert.Equal(1, counts["B"]);
        Assert.False(counts.ContainsKey(string.Empty));
    }

    [Fact]
    public void GetTimeTicks_ReturnsTheRowTimestampWithoutRehydrate()
    {
        IEventColumnReader reader = ReaderFor(500, 1500, 2500);

        Assert.Equal(1500, reader.GetTimeTicks(reader.LocatorAt(1)));
        Assert.Equal(2500, reader.GetTimeTicks(reader.LocatorAt(2)));
    }

    [Fact]
    public void TryGetTimeTicksRange_IgnoresFilteredRows()
    {
        IEventColumnReader reader = ReaderFor(50, 10, 90, 30);
        int[] membership = [0, -1, -1, 1];

        bool any = reader.TryGetTimeTicksRange(membership, out long min, out long max, CancellationToken.None);

        Assert.True(any);
        Assert.Equal(30, min);
        Assert.Equal(50, max);
    }

    [Fact]
    public void TryGetTimeTicksRange_ReflectsAnOutOfOrderOlderPendingAppend()
    {
        EventColumnStore store = EventColumnStore.Build(EventsAt(100, 200), Generation, ContentVersion)
            .Append(EventsAt(50));
        var reader = new EventColumnStoreReader(s_logId, store);

        reader.TryGetTimeTicksRange(AllSurvive(3), out long min, out long max, CancellationToken.None);

        Assert.Equal(50, min);
        Assert.Equal(200, max);
    }

    [Fact]
    public void TryGetTimeTicksRange_ReturnsFalseWhenNoRowSurvives()
    {
        IEventColumnReader reader = ReaderFor(1, 2, 3);
        int[] membership = [-1, -1, -1];

        bool any = reader.TryGetTimeTicksRange(membership, out long min, out long max, CancellationToken.None);

        Assert.False(any);
        Assert.Equal(0, min);
        Assert.Equal(0, max);
    }

    [Fact]
    public void TryGetTimeTicksRange_ReturnsSurvivorMinAndMax()
    {
        IEventColumnReader reader = ReaderFor(50, 10, 90, 30);

        bool any = reader.TryGetTimeTicksRange(AllSurvive(4), out long min, out long max, CancellationToken.None);

        Assert.True(any);
        Assert.Equal(10, min);
        Assert.Equal(90, max);
    }

    private static int[] AllSurvive(int count) => Enumerable.Range(0, count).ToArray();

    private static ResolvedEvent[] EventsAt(params long[] ticks)
    {
        var events = new ResolvedEvent[ticks.Length];

        for (int index = 0; index < ticks.Length; index++)
        {
            events[index] = new ResolvedEvent("TestLog", LogPathType.Channel)
            {
                Id = index,
                TimeCreated = new DateTime(ticks[index], DateTimeKind.Utc)
            };
        }

        return events;
    }

    private static ResolvedEvent[] EventsAtTick(long ticks, int count)
    {
        var events = new ResolvedEvent[count];

        for (int index = 0; index < count; index++)
        {
            events[index] = new ResolvedEvent("TestLog", LogPathType.Channel)
            {
                Id = index,
                TimeCreated = new DateTime(ticks, DateTimeKind.Utc)
            };
        }

        return events;
    }

    private static ResolvedEvent EventWith(long ticks, string level) =>
        new("TestLog", LogPathType.Channel)
        {
            Id = 0,
            TimeCreated = new DateTime(ticks, DateTimeKind.Utc),
            Level = level
        };

    private static ResolvedEvent EventWithSource(long ticks, string source) =>
        new("TestLog", LogPathType.Channel)
        {
            Id = 0,
            TimeCreated = new DateTime(ticks, DateTimeKind.Utc),
            Source = source
        };

    private static ResolvedEvent IdEvent(int id) =>
        new("TestLog", LogPathType.Channel)
        {
            Id = id,
            TimeCreated = new DateTime(0, DateTimeKind.Utc)
        };

    private static IEventColumnReader ReaderFor(params long[] ticks) =>
        new EventColumnStoreReader(s_logId, EventColumnStore.Build(EventsAt(ticks), Generation, ContentVersion));

    private static ResolvedEvent[] SourcesAtTick(long ticks, string source, int count)
    {
        var events = new ResolvedEvent[count];

        for (int index = 0; index < count; index++) { events[index] = EventWithSource(ticks, source); }

        return events;
    }
}
