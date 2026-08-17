// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using System.Security.Principal;

namespace EventLogExpert.Runtime.Tests.LogTable.OrderedView;

public sealed class OrderedViewBulkBuildTests
{
    private static readonly ColumnName[] s_everyGroupColumn =
    [
        ColumnName.RecordId,
        ColumnName.Level,
        ColumnName.DateAndTime,
        ColumnName.ActivityId,
        ColumnName.Log,
        ColumnName.ComputerName,
        ColumnName.Source,
        ColumnName.EventId,
        ColumnName.TaskCategory,
        ColumnName.Keywords,
        ColumnName.ProcessId,
        ColumnName.ThreadId,
        ColumnName.User,
        ColumnName.Opcode
    ];
    private static readonly ColumnName?[] s_groupBys =
    [
        null,
        ColumnName.Source,
        ColumnName.Level,
        ColumnName.User,
        ColumnName.EventId,
        ColumnName.Keywords
    ];
    private static readonly ColumnName?[] s_orderBys =
    [
        null,
        ColumnName.RecordId,
        ColumnName.DateAndTime,
        ColumnName.EventId,
        ColumnName.Level,
        ColumnName.Source,
        ColumnName.TaskCategory,
        ColumnName.ComputerName,
        ColumnName.ActivityId,
        ColumnName.ProcessId,
        ColumnName.ThreadId,
        ColumnName.Log,
        ColumnName.User,
        ColumnName.Keywords
    ];

    [Fact]
    public void BulkAdopt_ReplaysTailArrivingBeforeAdopt_MatchesIncremental()
    {
        var sample = new OrderedViewSample(seed: 7788, logCount: 1);
        sample.SeedInterleaved(totalEvents: 3000);
        EventLogId logId = sample.LogId(0);

        foreach (SortContext context in new[] { ByDate(), Physical(), Grouped(), ByDate(descending: true) })
        {
            IEventColumnReader seedReader = sample.PrefixReader(0, 2000);
            IEventColumnReader grownReader = sample.PrefixReader(0, 3000);

            OrderedViewSnapshot bulk = AdoptWithTailReplay(logId, seedReader, grownReader, context, bulkThreshold: 0);
            OrderedViewSnapshot incremental = AdoptWithTailReplay(logId, seedReader, grownReader, context, bulkThreshold: int.MaxValue);

            AssertSnapshotsEqual(incremental, bulk, $"tail-replay {Describe(context)}");
        }
    }

    [Fact]
    public void BulkAdopt_ThenLiveTailGrowth_MatchesIncrementalGrowth()
    {
        var sample = new OrderedViewSample(seed: 4242, logCount: 1);
        sample.SeedInterleaved(totalEvents: 3000);
        EventLogId logId = sample.LogId(0);

        foreach (SortContext context in new[] { ByDate(), Physical(), Grouped(), ByDate(descending: true) })
        {
            IEventColumnReader seedReader = sample.PrefixReader(0, 2000);
            IEventColumnReader grownReader = sample.PrefixReader(0, 3000);

            OrderedViewSnapshot bulk = GrowAndPublish(logId, seedReader, grownReader, context, bulkThreshold: 0);
            OrderedViewSnapshot incremental = GrowAndPublish(logId, seedReader, grownReader, context, bulkThreshold: int.MaxValue);

            AssertSnapshotsEqual(incremental, bulk, $"grown {Describe(context)}");
        }
    }

    [Fact]
    public void BulkPath_IsReachedAndAgreesWithIncremental_AtForcedThreshold()
    {
        var sample = new OrderedViewSample(seed: 555, logCount: 1);
        sample.SeedInterleaved(totalEvents: 300);
        IEventColumnReader reader = sample.Reader(0);
        EventLogId logId = sample.LogId(0);

        var state = new OrderedViewState();
        state.ReconcileLog(logId, reader);
        RebuildRequest request = state.BeginRebuild(static (_, _) => true, Grouped());

        ChunkedOrderIndex bulk = OrderedViewState.BuildIndex(request, CancellationToken.None, bulkThreshold: 0);
        ChunkedOrderIndex incremental = OrderedViewState.BuildIndex(request, CancellationToken.None, int.MaxValue);

        Assert.Equal(incremental.Count, bulk.Count);
        Assert.True(bulk.Count > 0);
    }

    [Theory]
    [InlineData(101)]
    [InlineData(2027)]
    public void Bulk_MatchesIncremental_AcrossTheSortCrossProduct(int seed)
    {
        var sample = new OrderedViewSample(seed, logCount: 1);
        sample.SeedInterleaved(totalEvents: 400);
        IEventColumnReader reader = sample.Reader(0);
        EventLogId logId = sample.LogId(0);

        foreach (SortContext context in AllContexts())
        {
            AssertBulkMatchesIncremental(reader, logId, static (_, _) => true, context, $"seed={seed} {Describe(context)}");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(1023)]
    [InlineData(1024)]
    [InlineData(1025)]
    [InlineData(2049)]
    public void Bulk_MatchesIncremental_AtChunkBoundaries(int rows)
    {
        var sample = new OrderedViewSample(seed: 71, logCount: 1);
        sample.SeedInterleaved(rows);
        IEventColumnReader reader = sample.Reader(0);
        EventLogId logId = sample.LogId(0);

        AssertBulkMatchesIncremental(reader, logId, static (_, _) => true, ByDate(), $"rows={rows} ByDate");
        AssertBulkMatchesIncremental(reader, logId, static (_, _) => true, Grouped(), $"rows={rows} Grouped");
    }

    [Fact]
    public void Bulk_MatchesIncremental_EqualTicksAcrossDateTimeKind()
    {
        var shared = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        var events = new List<ResolvedEvent>();

        for (int i = 0; i < 60; i++)
        {
            DateTimeKind kind = i % 3 == 0 ? DateTimeKind.Unspecified : i % 3 == 1 ? DateTimeKind.Local : DateTimeKind.Utc;

            events.Add(new ResolvedEvent("KindLog", LogPathType.Channel)
            {
                RecordId = i % 5 == 0 ? null : i,
                TimeCreated = DateTime.SpecifyKind(shared, kind),
                Id = 1000 + (i % 4),
                Level = i % 2 == 0 ? "" : "Error",
                Source = $"Provider.{i % 3}"
            });
        }

        (IEventColumnReader reader, EventLogId logId) = BuildReader(events);

        foreach (SortContext context in new[] { ByDate(), ByDate(descending: true), Grouped() })
        {
            AssertBulkMatchesIncremental(reader, logId, static (_, _) => true, context, $"mixed-kind {Describe(context)}");
        }
    }

    [Theory]
    [InlineData(3110)]
    public void Bulk_MatchesIncremental_ForEveryGroupColumn(int seed)
    {
        var sample = new OrderedViewSample(seed, logCount: 1);
        sample.SeedInterleaved(totalEvents: 400);
        IEventColumnReader reader = sample.Reader(0);
        EventLogId logId = sample.LogId(0);

        foreach (ColumnName groupBy in s_everyGroupColumn)
        {
            foreach (bool groupDescending in new[] { false, true })
            {
                var context = new SortContext(null, false, groupBy, groupDescending);
                AssertBulkMatchesIncremental(reader, logId, static (_, _) => true, context, $"everyGroup {Describe(context)}");
            }
        }
    }

    [Fact]
    public void Bulk_MatchesIncremental_MixedNullEmptyAndValueInGroupColumn()
    {
        var events = new List<ResolvedEvent>();

        for (int i = 0; i < 200; i++)
        {
            string level = (i % 3) switch { 0 => "", 1 => "Warning", _ => "Error" };

            events.Add(new ResolvedEvent("MixLog", LogPathType.Channel)
            {
                RecordId = i % 4 == 0 ? null : i,
                TimeCreated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(i % 17),
                Id = 1000 + (i % 6),
                Level = level,
                Source = i % 5 == 0 ? "" : $"Src.{i % 7}",
                TaskCategory = i % 2 == 0 ? "" : "Logon",
                Opcode = (i % 3) switch { 0 => "", 1 => "Start", _ => "Stop" }
            });
        }

        (IEventColumnReader reader, EventLogId logId) = BuildReader(events);

        foreach (SortContext context in AllContexts())
        {
            AssertBulkMatchesIncremental(reader, logId, static (_, _) => true, context, $"mixed-null {Describe(context)}");
        }
    }

    [Fact]
    public void Bulk_MatchesIncremental_WithGenuineNullPooledStringAndDuplicatePools()
    {
        var first = new List<ResolvedEvent>();
        var second = new List<ResolvedEvent>();
        var sid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

        for (int i = 0; i < 130; i++)
        {
            (i < 65 ? first : second).Add(new ResolvedEvent("DupLog", LogPathType.Channel)
            {
                RecordId = i % 6 == 0 ? null : i,
                TimeCreated = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc).AddSeconds(i % 23),
                Id = 1000 + (i % 5),
                Level = i % 3 == 0 ? "" : "Error",
                Source = $"Shared.{i % 4}",
                UserId = i % 2 == 0 ? null : sid
            });
        }

        EventLogId logId = EventLogId.Create();
        IEventColumnReader reader = EventColumnStore.Build(first, 0, 0).Append(second).CreateReader(logId);

        foreach (SortContext context in AllContexts())
        {
            AssertBulkMatchesIncremental(reader, logId, static (_, _) => true, context, $"dup-pool {Describe(context)}");
        }
    }

    [Fact]
    public void Bulk_MatchesIncremental_WithSparseFilteredSurvivors()
    {
        var sample = new OrderedViewSample(seed: 9021, logCount: 1);
        sample.SeedInterleaved(totalEvents: 900);
        IEventColumnReader reader = sample.Reader(0);
        EventLogId logId = sample.LogId(0);

        bool Predicate(EventLocator locator, IEventColumnReader _) => locator.Index % 7 == 0 || locator.Index > 850;

        foreach (SortContext context in new[] { Physical(), ByDate(), Grouped() })
        {
            AssertBulkMatchesIncremental(reader, logId, Predicate, context, $"sparse {Describe(context)}");
        }
    }

    [Fact]
    public void CombinedBulkAdopt_ReplaysMultiLogTail_MatchesIncremental()
    {
        var sample = new OrderedViewSample(seed: 6161, logCount: 3);
        sample.SeedInterleaved(totalEvents: 2400);

        foreach (SortContext context in new[] { ByDate(), Physical(), Grouped() })
        {
            OrderedViewSnapshot bulk = AdoptCombinedWithTailReplay(sample, context, bulkThreshold: 0);
            OrderedViewSnapshot incremental = AdoptCombinedWithTailReplay(sample, context, bulkThreshold: int.MaxValue);

            AssertSnapshotsEqual(incremental, bulk, $"combined tail-replay {Describe(context)}");
        }
    }

    [Fact]
    public void CombinedBulk_HonorsCancellation()
    {
        var sample = new OrderedViewSample(seed: 3737, logCount: 12);
        sample.SeedInterleaved(totalEvents: 6000);

        var state = new OrderedViewState();
        for (int k = 0; k < sample.LogCount; k++) { state.ReconcileLog(sample.LogId(k), sample.Reader(k)); }

        RebuildRequest request = state.BeginRebuild(static (_, _) => true, ByDate());

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => OrderedViewState.BuildIndex(request, cancelled.Token, bulkThreshold: 0));
    }

    [Theory]
    [InlineData(2, 909)]
    [InlineData(3, 606)]
    [InlineData(4, 4004)]
    public void CombinedBulk_MatchesIncremental_AcrossCrossProduct(int logCount, int seed)
    {
        var sample = new OrderedViewSample(seed, logCount);
        sample.SeedInterleaved(totalEvents: 500);
        var logs = ReadersOf(sample);

        foreach (SortContext context in AllContexts())
        {
            AssertCombinedBulkMatchesIncremental(logs, static (_, _) => true, context, $"combined k={logCount} {Describe(context)}");
        }
    }

    [Fact]
    public void CombinedBulk_MatchesIncremental_SameOwningLogDistinctLogId()
    {
        var first = new List<ResolvedEvent>();
        var second = new List<ResolvedEvent>();
        var shared = new DateTime(2026, 5, 5, 0, 0, 0, DateTimeKind.Utc);

        for (int i = 0; i < 120; i++)
        {
            var row = new ResolvedEvent("SharedName", LogPathType.Channel)
            {
                RecordId = null,
                TimeCreated = shared,
                Id = 1000,
                Level = "Error",
                Source = "Provider.X"
            };
            first.Add(row);
            second.Add(row);
        }

        EventLogId logIdA = EventLogId.Create();
        EventLogId logIdB = EventLogId.Create();
        var logs = new[]
        {
            (logIdA, EventColumnStore.Build(first, 0, 0).CreateReader(logIdA)),
            (logIdB, EventColumnStore.Build(second, 0, 0).CreateReader(logIdB))
        };

        foreach (SortContext context in new[] { ByDate(), Physical(), Grouped(), ByDate(descending: true) })
        {
            AssertCombinedBulkMatchesIncremental(logs, static (_, _) => true, context, $"same-owninglog {Describe(context)}");
        }
    }

    [Fact]
    public void CombinedBulk_MatchesIncremental_WithSparseFiltersAndAnEmptyAndSingleSurvivorLog()
    {
        var sample = new OrderedViewSample(seed: 8123, logCount: 3);
        sample.SeedInterleaved(totalEvents: 600);
        var logs = ReadersOf(sample);
        EventLogId log0 = logs[0].LogId;
        EventLogId log1 = logs[1].LogId;

        bool Predicate(EventLocator locator, IEventColumnReader _) =>
            (locator.LogId == log0 && (locator.Index % 5 == 0 || locator.Index > 180)) ||
            (locator.LogId == log1 && locator.Index == 3);

        foreach (SortContext context in new[] { ByDate(), Grouped() })
        {
            AssertCombinedBulkMatchesIncremental(logs, Predicate, context, $"combined-sparse {Describe(context)}");
        }
    }

    [Theory]
    [InlineData(303)]
    [InlineData(1717)]
    public void MemoryBudget_ForcesDelegatingFallback_WithIdenticalOrder(int seed)
    {
        // A tiny budget makes EstimateBulkPeakBytes exceed it, so TryBuildBulk returns null and BuildIndex falls back
        // to the bounded-memory delegating path. That fallback must be order-identical to the (huge-budget) bulk path.
        var sample = new OrderedViewSample(seed, logCount: 1);
        sample.SeedInterleaved(totalEvents: 400);
        IEventColumnReader reader = sample.Reader(0);
        EventLogId logId = sample.LogId(0);

        foreach (SortContext context in AllContexts())
        {
            var state = new OrderedViewState();
            state.ReconcileLog(logId, reader);
            RebuildRequest request = state.BeginRebuild(static (_, _) => true, context);

            ChunkedOrderIndex bulk = OrderedViewState.BuildIndex(
                request, CancellationToken.None, bulkThreshold: 0, memoryBudgetBytes: long.MaxValue);
            ChunkedOrderIndex fallback = OrderedViewState.BuildIndex(
                request, CancellationToken.None, bulkThreshold: 0, memoryBudgetBytes: 1);

            IComparer<OrderKey> comparer = OrderKeyComparerFactory.Create(context, request.BeginResolver);
            AssertSnapshotsEqual(bulk.Publish(comparer, 0), fallback.Publish(comparer, 0), $"seed={seed} {Describe(context)}");
        }
    }

    private static OrderedViewSnapshot AdoptCombinedWithTailReplay(OrderedViewSample sample, SortContext context, int bulkThreshold)
    {
        var state = new OrderedViewState();

        for (int k = 0; k < sample.LogCount; k++)
        {
            state.ReconcileLog(sample.LogId(k), sample.PrefixReader(k, sample.Events(k).Count - 200));
        }

        RebuildRequest request = state.BeginRebuild(static (_, _) => true, context);

        for (int k = 0; k < sample.LogCount; k++) { state.ReconcileLog(sample.LogId(k), sample.Reader(k)); }

        Assert.True(state.TryAdoptRebuild(request, OrderedViewState.BuildIndex(request, CancellationToken.None, bulkThreshold)));

        return state.Publish();
    }

    private static OrderedViewSnapshot AdoptWithTailReplay(
        EventLogId logId,
        IEventColumnReader seedReader,
        IEventColumnReader grownReader,
        SortContext context,
        int bulkThreshold)
    {
        var state = new OrderedViewState();
        state.ReconcileLog(logId, seedReader);
        RebuildRequest request = state.BeginRebuild(static (_, _) => true, context);
        state.ReconcileLog(logId, grownReader);
        Assert.True(state.TryAdoptRebuild(request, OrderedViewState.BuildIndex(request, CancellationToken.None, bulkThreshold)));

        return state.Publish();
    }

    private static IEnumerable<SortContext> AllContexts()
    {
        foreach (ColumnName? groupBy in s_groupBys)
        {
            foreach (ColumnName? orderBy in s_orderBys)
            {
                foreach (bool descending in new[] { false, true })
                {
                    if (groupBy is null)
                    {
                        yield return new SortContext(orderBy, descending, null, false);
                    }
                    else
                    {
                        foreach (bool groupDescending in new[] { false, true })
                        {
                            yield return new SortContext(orderBy, descending, groupBy, groupDescending);
                        }
                    }
                }
            }
        }
    }

    private static void AssertBulkMatchesIncremental(
        IEventColumnReader reader,
        EventLogId logId,
        Func<EventLocator, IEventColumnReader, bool> predicate,
        SortContext context,
        string label)
    {
        var state = new OrderedViewState();
        state.ReconcileLog(logId, reader);
        RebuildRequest request = state.BeginRebuild(predicate, context);

        ChunkedOrderIndex incremental = OrderedViewState.BuildIndex(request, CancellationToken.None, int.MaxValue);
        ChunkedOrderIndex bulk = OrderedViewState.BuildIndex(request, CancellationToken.None, bulkThreshold: 0);

        IComparer<OrderKey> comparer = OrderKeyComparerFactory.Create(context, request.BeginResolver);
        OrderedViewSnapshot incrementalSnapshot = incremental.Publish(comparer, 0);
        OrderedViewSnapshot bulkSnapshot = bulk.Publish(comparer, 0);
        AssertSnapshotsEqual(incrementalSnapshot, bulkSnapshot, label);

        for (int index = 0; index < reader.Count; index++)
        {
            Assert.Equal(
                incrementalSnapshot.Contains(logId, reader.Generation, index),
                bulkSnapshot.Contains(logId, reader.Generation, index));
        }
    }

    private static void AssertCombinedBulkMatchesIncremental(
        IReadOnlyList<(EventLogId LogId, IEventColumnReader Reader)> logs,
        Func<EventLocator, IEventColumnReader, bool> predicate,
        SortContext context,
        string label)
    {
        var state = new OrderedViewState();
        foreach ((EventLogId logId, IEventColumnReader reader) in logs) { state.ReconcileLog(logId, reader); }

        RebuildRequest request = state.BeginRebuild(predicate, context);

        ChunkedOrderIndex incremental = OrderedViewState.BuildIndex(request, CancellationToken.None, int.MaxValue);
        ChunkedOrderIndex bulk = OrderedViewState.BuildIndex(request, CancellationToken.None, bulkThreshold: 0);

        IComparer<OrderKey> comparer = OrderKeyComparerFactory.Create(context, request.BeginResolver);
        OrderedViewSnapshot incrementalSnapshot = incremental.Publish(comparer, 0);
        OrderedViewSnapshot bulkSnapshot = bulk.Publish(comparer, 0);
        AssertSnapshotsEqual(incrementalSnapshot, bulkSnapshot, label);

        foreach ((EventLogId logId, IEventColumnReader reader) in logs)
        {
            for (int index = 0; index < reader.Count; index++)
            {
                Assert.Equal(
                    incrementalSnapshot.Contains(logId, reader.Generation, index),
                    bulkSnapshot.Contains(logId, reader.Generation, index));
            }
        }
    }

    private static void AssertSnapshotsEqual(OrderedViewSnapshot expected, OrderedViewSnapshot actual, string label)
    {
        Assert.Equal(expected.Count, actual.Count);

        for (int display = 0; display < expected.Count; display++)
        {
            EventLocator expectedLocator = expected.At(display).Locator;
            EventLocator actualLocator = actual.At(display).Locator;

            Assert.True(
                expectedLocator == actualLocator,
                $"{label}: display[{display}] expected {expectedLocator} but bulk gave {actualLocator}");

            Assert.Equal(display, actual.RankOf(new OrderKey(expectedLocator)));
            Assert.True(actual.Contains(expectedLocator.LogId, expectedLocator.Generation, expectedLocator.Index), label);
        }

        Assert.Equal(-1, actual.RankOf(new OrderKey(new EventLocator(EventLogId.Create(), 0, 0))));
    }

    private static (IEventColumnReader Reader, EventLogId LogId) BuildReader(IReadOnlyList<ResolvedEvent> events)
    {
        EventLogId logId = EventLogId.Create();

        return (EventColumnStore.Build(events, 0, 0).CreateReader(logId), logId);
    }

    private static SortContext ByDate(bool descending = false) => new(ColumnName.DateAndTime, descending, null, false);

    private static string Describe(SortContext context) =>
        $"order={context.OrderBy?.ToString() ?? "none"}{(context.IsDescending ? " desc" : "")} " +
        $"group={context.GroupBy?.ToString() ?? "none"}{(context.IsGroupDescending ? " desc" : "")}";

    private static SortContext Grouped() => new(ColumnName.DateAndTime, false, ColumnName.Source, false);

    private static OrderedViewSnapshot GrowAndPublish(
        EventLogId logId,
        IEventColumnReader seedReader,
        IEventColumnReader grownReader,
        SortContext context,
        int bulkThreshold)
    {
        var state = new OrderedViewState();
        state.ReconcileLog(logId, seedReader);
        RebuildRequest request = state.BeginRebuild(static (_, _) => true, context);
        Assert.True(state.TryAdoptRebuild(request, OrderedViewState.BuildIndex(request, CancellationToken.None, bulkThreshold)));
        state.Publish();

        state.ReconcileLog(logId, grownReader);

        return state.Publish();
    }

    private static SortContext Physical() => new(null, false, null, false);

    private static (EventLogId LogId, IEventColumnReader Reader)[] ReadersOf(OrderedViewSample sample)
    {
        var logs = new (EventLogId, IEventColumnReader)[sample.LogCount];

        for (int k = 0; k < sample.LogCount; k++) { logs[k] = (sample.LogId(k), sample.Reader(k)); }

        return logs;
    }
}
