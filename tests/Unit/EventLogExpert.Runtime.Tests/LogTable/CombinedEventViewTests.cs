// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using System.Diagnostics;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class CombinedEventViewTests
{
    private static readonly ColumnName?[] s_groupBys = [null, ColumnName.Source, ColumnName.Level];
    private static readonly ColumnName?[] s_orderBys =
        [null, ColumnName.DateAndTime, ColumnName.Source, ColumnName.Level, ColumnName.EventId];

    [Fact]
    public void CopyTo_ValidatesDestinationBeforeWriting()
    {
        var (facade, oracle) = Build([MakeLog("Log0", 1, 100)], ColumnName.DateAndTime, true, null);

        var tooSmall = new ResolvedEvent[facade.Count - 1];
        Assert.Throws<ArgumentException>(() => facade.CopyTo(tooSmall, 0));
        Assert.All(tooSmall, e => Assert.Null(e)); // not partially mutated

        var destination = new ResolvedEvent[facade.Count + 2];
        facade.CopyTo(destination, 1);
        Assert.Null(destination[0]);

        for (int i = 0; i < facade.Count; i++) { Assert.Same(oracle[i], destination[i + 1]); }
    }

    [Fact]
    public void Constructor_DuplicateOwningLog_Throws()
    {
        var context = new SortContext(ColumnName.DateAndTime, true, null, false);
        var first = SegmentedSortedList.CreateSorted(MakeLog("Shared", 1, 5), context);
        var second = SegmentedSortedList.CreateSorted(MakeLog("Shared", 100, 5), context);

        Assert.Throws<UnreachableException>(() => new CombinedEventView([first, second], context));
    }

    [Fact]
    public void EmptyFacade_HasNoRowsAndResolvesNothing()
    {
        var facade = new CombinedEventView([], new SortContext(null, true, null, false));

        Assert.Empty(facade);
        Assert.Throws<ArgumentOutOfRangeException>(() => facade[0]);
        Assert.Equal(-1, facade.Rank(MakeEvent("Log0", 1, DateTime.UtcNow, 1000, "S", "Information")));
        Assert.Empty(facade.Slice(0, 10));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(101)]
    [InlineData(2026)]
    [InlineData(31337)]
    [InlineData(8675309)]
    public void Facade_MatchesMaterializedOracle_OverRandomContexts(int seed)
    {
        var rng = new Random(seed);
        int logs = 1 + rng.Next(5);
        var perLog = GenerateLogs(rng, logs, minPerLog: 50, maxPerLog: 900);

        foreach (var orderBy in s_orderBys)
        {
            foreach (var groupBy in s_groupBys)
            {
                bool descending = rng.Next(2) == 0;
                var (facade, oracle) = Build(perLog, orderBy, descending, groupBy);

                Assert.Equal(oracle.Count, facade.Count);

                var enumerated = facade.ToList();
                Assert.Equal(oracle.Count, enumerated.Count);

                for (int i = 0; i < oracle.Count; i++) { Assert.Same(oracle[i], enumerated[i]); }

                foreach (int i in IndicesToProbe(oracle.Count, rng)) { Assert.Same(oracle[i], facade[i]); }

                for (int i = 0; i < oracle.Count; i++) { Assert.Equal(i, facade.Rank(oracle[i])); }

                foreach (int start in IndicesToProbe(oracle.Count, rng))
                {
                    int count = Math.Min(73, oracle.Count - start);
                    var slice = facade.Slice(start, count);
                    Assert.Equal(count, slice.Count);

                    for (int j = 0; j < count; j++) { Assert.Same(oracle[start + j], slice[j]); }
                }
            }
        }
    }

    [Fact]
    public void Mutators_Throw_BecauseTheViewIsReadOnly()
    {
        var (facade, _) = Build([MakeLog("Log0", 1, 10)], ColumnName.DateAndTime, true, null);
        var sample = facade[0];

        Assert.True(facade.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => facade.Add(sample));
        Assert.Throws<NotSupportedException>(() => facade.Insert(0, sample));
        Assert.Throws<NotSupportedException>(() => facade.Remove(sample));
        Assert.Throws<NotSupportedException>(() => facade.RemoveAt(0));
        Assert.Throws<NotSupportedException>(facade.Clear);
        Assert.Throws<NotSupportedException>(() => facade[0] = sample);
    }

    [Fact]
    public void NullRecordIdErrorReads_AreInternallyConsistent()
    {
        var rng = new Random(123);
        var log = new List<ResolvedEvent>();
        var time = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Error reads share a null RecordId (the only intra-log tie).
        for (int i = 0; i < 40; i++)
        {
            time = time.AddSeconds(1);
            log.Add(MakeEvent("Log0", recordId: null, time, id: 1000, source: "SameSource", level: "Error"));
        }

        var (facade, _) = Build([log], ColumnName.Source, false, null);

        var ranks = new HashSet<int>();

        foreach (var resolved in log)
        {
            int rank = facade.Rank(resolved);
            Assert.InRange(rank, 0, facade.Count - 1);
            Assert.True(ranks.Add(rank), "each error read must map to a distinct rank");
            Assert.Same(resolved, facade[rank]);
        }
    }

    [Fact]
    public void Rank_ForEventFromClosedOrAbsentLog_ReturnsMinusOne()
    {
        var perLog = new List<List<ResolvedEvent>> { MakeLog("Log0", 1, 200) };
        var (facade, _) = Build(perLog, ColumnName.DateAndTime, true, null);

        var foreign = MakeEvent("LogClosed", 5, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 1000, "S", "Information");

        Assert.Equal(-1, facade.Rank(foreign));
        Assert.Null(facade.ResolveByKey(foreign));
    }

    [Fact]
    public void ResolveByKey_FindsByReferenceAndByEquivalentKey()
    {
        var log = MakeLog("Log0", 1, 300);
        var perLog = new List<List<ResolvedEvent>> { log };
        var (facade, _) = Build(perLog, ColumnName.Source, false, null);

        var original = log[150];
        Assert.Same(original, facade.ResolveByKey(original));

        var equivalent = MakeEvent(original.OwningLog, original.RecordId, original.TimeCreated, original.Id, original.Source, original.Level);
        Assert.Same(original, facade.ResolveByKey(equivalent));

        Assert.Null(facade.ResolveByKey(null));
    }

    [Fact]
    public void Slice_HandlesBoundariesAndOverflowWithoutWrongEmpty()
    {
        var (facade, oracle) = Build([MakeLog("Log0", 1, 1000)], ColumnName.DateAndTime, true, null);

        Assert.Empty(facade.Slice(facade.Count, 10));
        Assert.Empty(facade.Slice(0, 0));

        // Overflowing start+count returns the tail, not empty.
        var tail = facade.Slice(5, int.MaxValue);
        Assert.Equal(oracle.Count - 5, tail.Count);
        Assert.Same(oracle[5], tail[0]);
        Assert.Same(oracle[^1], tail[^1]);
    }

    private static (CombinedEventView Facade, IReadOnlyList<ResolvedEvent> Oracle) Build(
        List<List<ResolvedEvent>> perLog, ColumnName? orderBy, bool descending, ColumnName? groupBy)
    {
        var context = new SortContext(orderBy, descending, groupBy, false);
        var lists = perLog.Select(events => SegmentedSortedList.CreateSorted(events, context)).ToList();
        var facade = new CombinedEventView(lists, context);
        var oracle = perLog.SelectMany(events => events).SortEvents(orderBy, descending, groupBy);

        return (facade, oracle);
    }

    private static List<List<ResolvedEvent>> GenerateLogs(Random rng, int logs, int minPerLog, int maxPerLog)
    {
        string[] sources = ["Provider.A", "Provider.B", "Provider.C"];
        string[] levels = ["Information", "Warning", "Error"];
        var result = new List<List<ResolvedEvent>>(logs);

        for (int k = 0; k < logs; k++)
        {
            var time = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(rng.Next(0, 120));
            int count = minPerLog + rng.Next(maxPerLog - minPerLog);
            var events = new List<ResolvedEvent>(count);

            for (long record = 1; record <= count; record++)
            {
                time = time.AddMilliseconds(1 + rng.Next(1000));
                events.Add(MakeEvent($"Log{k}", record, time, 1000 + rng.Next(8),
                    sources[rng.Next(sources.Length)], levels[rng.Next(levels.Length)]));
            }

            result.Add(events);
        }

        return result;
    }

    private static IEnumerable<int> IndicesToProbe(int count, Random rng)
    {
        if (count == 0) { yield break; }

        foreach (int candidate in new[] { 0, 1, 63, 64, 65, 127, 128, 129, 255, 256, 511, 512, count / 2, count - 1 })
        {
            if (candidate >= 0 && candidate < count) { yield return candidate; }
        }

        for (int i = 0; i < 20; i++) { yield return rng.Next(count); }
    }

    private static ResolvedEvent MakeEvent(string owningLog, long? recordId, DateTime time, int id, string source, string level) =>
        new(owningLog, LogPathType.Channel)
        {
            RecordId = recordId,
            TimeCreated = time,
            Id = id,
            Source = source,
            Level = level
        };

    private static List<ResolvedEvent> MakeLog(string owningLog, long firstRecord, int count)
    {
        var events = new List<ResolvedEvent>(count);
        var time = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        for (int i = 0; i < count; i++)
        {
            time = time.AddMilliseconds(1 + i);
            events.Add(MakeEvent(owningLog, firstRecord + i, time, 1000 + (i % 8), "Provider.A", "Information"));
        }

        return events;
    }
}
