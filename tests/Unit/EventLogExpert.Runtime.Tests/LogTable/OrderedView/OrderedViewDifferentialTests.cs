// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using EventLogExpert.Runtime.Tests.LogTable.TestSupport;

namespace EventLogExpert.Runtime.Tests.LogTable.OrderedView;

public sealed class OrderedViewDifferentialTests
{
    private static readonly ColumnName[] s_allGroupColumns =
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
        ColumnName.EventId,
        ColumnName.ComputerName,
        ColumnName.TaskCategory
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
        ColumnName.Keywords,
        ColumnName.Opcode
    ];

    [Theory]
    [InlineData(9753)]
    public void Combined_EveryGroupColumn_ProducesOneContiguousRunPerKeyAcrossLogs(int seed)
    {
        var sample = new OrderedViewSample(seed, logCount: 3);
        sample.SeedInterleaved(totalEvents: 180);

        var readers = new IEventColumnReader[sample.LogCount];
        for (int k = 0; k < sample.LogCount; k++) { readers[k] = sample.Reader(k); }

        foreach (ColumnName groupBy in s_allGroupColumns)
        {
            foreach (bool groupDescending in new[] { false, true })
            {
                var context = new SortContext(null, true, groupBy, groupDescending);
                var state = new OrderedViewState();
                ReconcileInterleaved(state, sample, readers);
                RebuildTo(state, static (_, _) => true, context);

                var view = new CombinedOrderedColumnView(state.Current, state.AdoptedInScope);

                AssertOneRunPerKey(view, groupBy, $"seed={seed} combined groupBy={groupBy} groupDescending={groupDescending}");
            }
        }
    }

    [Theory]
    [InlineData(999)]
    [InlineData(4242)]
    public void Combined_MatchesOrderingOracle_AcrossContexts(int seed)
    {
        var sample = new OrderedViewSample(seed, logCount: 3);
        sample.SeedInterleaved(totalEvents: 180);

        foreach (SortContext context in Contexts())
        {
            AssertCombinedParity(sample, context, $"seed={seed} {Describe(context)}");
        }
    }

    [Theory]
    [InlineData(8642)]
    public void SingleLog_EveryGroupColumn_ProducesOneContiguousRunPerKey(int seed)
    {
        var sample = new OrderedViewSample(seed, logCount: 1);
        sample.SeedInterleaved(totalEvents: 160);
        IEventColumnReader reader = sample.Reader(0);
        EventLogId logId = sample.LogId(0);

        foreach (ColumnName groupBy in s_allGroupColumns)
        {
            foreach (bool groupDescending in new[] { false, true })
            {
                var context = new SortContext(null, true, groupBy, groupDescending);
                OrderedViewState state = DriveReconcileThenRebuild(logId, reader, static (_, _) => true, context);
                var view = new OrderedColumnView(state.Current, reader);

                AssertOneRunPerKey(view, groupBy, $"seed={seed} groupBy={groupBy} groupDescending={groupDescending}");
            }
        }
    }

    [Theory]
    [InlineData(31337)]
    public void SingleLog_FilterSurvivingSubset_MatchesOrderingOracle(int seed)
    {
        var sample = new OrderedViewSample(seed, logCount: 1);
        sample.SeedInterleaved(totalEvents: 150);

        var survivors = new HashSet<int>(Enumerable.Range(0, sample.Events(0).Count).Where(static i => i % 3 != 0));

        foreach (SortContext context in Contexts())
        {
            AssertSingleLogParity(sample, context, survivors, driveWithRebuild: true, $"seed={seed} filtered {Describe(context)}");
        }
    }

    [Theory]
    [InlineData(13579)]
    public void SingleLog_LiveInsertUnderContext_MatchesOrderingOracle(int seed)
    {
        var sample = new OrderedViewSample(seed, logCount: 1);
        sample.SeedInterleaved(totalEvents: 140);

        foreach (SortContext context in Contexts())
        {
            AssertSingleLogParity(sample, context, survivorSet: null, driveWithRebuild: false, $"seed={seed} live-insert {Describe(context)}");
        }
    }

    [Theory]
    [InlineData(12345)]
    [InlineData(24680)]
    public void SingleLog_MatchesOrderingOracle_AcrossContexts(int seed)
    {
        var sample = new OrderedViewSample(seed, logCount: 1);
        sample.SeedInterleaved(totalEvents: 160);

        foreach (SortContext context in Contexts())
        {
            AssertSingleLogParity(sample, context, survivorSet: null, driveWithRebuild: true, $"seed={seed} {Describe(context)}");
        }
    }

    private static void AssertCombinedParity(OrderedViewSample sample, SortContext context, string label)
    {
        int logCount = sample.LogCount;
        var readers = new IEventColumnReader[logCount];
        var flatLocators = new List<EventLocator>();
        var flatEvents = new List<ResolvedEvent>();

        for (int k = 0; k < logCount; k++)
        {
            readers[k] = sample.Reader(k);
            int count = readers[k].Count;

            for (int i = 0; i < count; i++)
            {
                flatLocators.Add(new EventLocator(sample.LogId(k), readers[k].Generation, i));
                flatEvents.Add(sample.Events(k)[i]);
            }
        }

        int[] aosOrder = AosReferenceOrdering.Order(
            flatEvents, context.OrderBy, context.IsDescending, context.GroupBy, context.IsGroupDescending);

        var state = new OrderedViewState();
        ReconcileInterleaved(state, sample, readers);
        RebuildTo(state, static (_, _) => true, context);
        OrderedViewSnapshot snap = state.Current;

        Assert.Equal(flatLocators.Count, snap.Count);

        for (int i = 0; i < snap.Count; i++)
        {
            EventLocator engineLocator = snap.At(i).Locator;

            Assert.Equal(flatLocators[aosOrder[i]], engineLocator);
            Assert.Equal(i, snap.RankOf(new OrderKey(engineLocator)));
            Assert.True(snap.Contains(engineLocator.LogId, engineLocator.Generation, engineLocator.Index), $"membership {label}[{i}]");
        }

        AssertSliceMatchesSequential(snap, label);
        Assert.Equal(-1, snap.RankOf(new OrderKey(new EventLocator(EventLogId.Create(), 0, 0))));
    }

    private static void AssertOneRunPerKey(IEventColumnView view, ColumnName groupBy, string label)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? currentKey = null;
        int runs = 0;

        for (int index = 0; index < view.Count; index++)
        {
            string key = view.GroupKeyAt(view.LocatorAt(index), groupBy);

            if (currentKey is not null && string.Equals(key, currentKey, StringComparison.Ordinal)) { continue; }

            Assert.True(seen.Add(key), $"group key '{key}' recurs in a later run ({label})");

            currentKey = key;
            runs++;
        }

        Assert.Equal(seen.Count, runs);
    }

    private static void AssertSingleLogParity(
        OrderedViewSample sample, SortContext context, HashSet<int>? survivorSet, bool driveWithRebuild, string label)
    {
        IEventColumnReader reader = sample.Reader(0);
        EventLogId logId = sample.LogId(0);
        int count = reader.Count;

        int[] survivors = (survivorSet is null ? Enumerable.Range(0, count) : survivorSet.OrderBy(static i => i)).ToArray();

        var survivingEvents = new ResolvedEvent[survivors.Length];
        for (int i = 0; i < survivors.Length; i++) { survivingEvents[i] = sample.Events(0)[survivors[i]]; }

        int[] aosOrder = AosReferenceOrdering.Order(
            survivingEvents, context.OrderBy, context.IsDescending, context.GroupBy, context.IsGroupDescending);

        Func<EventLocator, IEventColumnReader, bool> predicate = survivorSet is null ? static (_, _) => true : (locator, _) => survivorSet.Contains(locator.Index);
        OrderedViewState state = driveWithRebuild
            ? DriveReconcileThenRebuild(logId, reader, predicate, context)
            : DriveRebuildThenReconcile(logId, reader, predicate, context);

        OrderedViewSnapshot snap = state.Current;

        Assert.Equal(survivors.Length, snap.Count);

        for (int i = 0; i < snap.Count; i++)
        {
            EventLocator engineLocator = snap.At(i).Locator;

            Assert.Equal(survivors[aosOrder[i]], engineLocator.Index);
            Assert.Equal(i, snap.RankOf(new OrderKey(engineLocator)));
            Assert.True(snap.Contains(engineLocator.LogId, engineLocator.Generation, engineLocator.Index), $"membership {label}[{i}]");
        }

        AssertSliceMatchesSequential(snap, label);

        for (int i = 0; i < count; i++)
        {
            if (survivorSet is null || survivorSet.Contains(i)) { continue; }

            var absent = new EventLocator(logId, reader.Generation, i);
            Assert.False(snap.Contains(absent.LogId, absent.Generation, absent.Index), $"filtered membership {label}[{i}]");
            Assert.Equal(-1, snap.RankOf(new OrderKey(absent)));
        }

        Assert.Equal(-1, snap.RankOf(new OrderKey(new EventLocator(EventLogId.Create(), 0, 0))));
    }

    private static void AssertSliceMatchesSequential(OrderedViewSnapshot snap, string label)
    {
        if (snap.Count == 0) { return; }

        int offset = snap.Count / 3;
        int width = Math.Min(50, snap.Count - offset);
        var buffer = new OrderKey[width];
        int written = snap.SliceInto(offset, width, buffer);

        Assert.Equal(width, written);

        for (int i = 0; i < written; i++)
        {
            Assert.Equal(snap.At(offset + i).Locator, buffer[i].Locator);
        }
    }

    private static IEnumerable<SortContext> Contexts()
    {
        foreach (ColumnName? orderBy in s_orderBys)
        {
            foreach (ColumnName? groupBy in s_groupBys)
            {
                foreach (bool descending in new[] { false, true })
                {
                    if (groupBy is null)
                    {
                        yield return new SortContext(orderBy, descending, null, false);

                        continue;
                    }

                    foreach (bool groupDescending in new[] { false, true })
                    {
                        yield return new SortContext(orderBy, descending, groupBy, groupDescending);
                    }
                }
            }
        }
    }

    private static string Describe(SortContext context) =>
        $"orderBy={context.OrderBy} desc={context.IsDescending} groupBy={context.GroupBy} groupDesc={context.IsGroupDescending}";

    private static OrderedViewState DriveRebuildThenReconcile(
        EventLogId logId, IEventColumnReader reader, Func<EventLocator, IEventColumnReader, bool> predicate, SortContext context)
    {
        var state = new OrderedViewState();
        RebuildTo(state, predicate, context);
        state.ReconcileLog(logId, reader);
        state.Publish();

        return state;
    }

    private static OrderedViewState DriveReconcileThenRebuild(
        EventLogId logId, IEventColumnReader reader, Func<EventLocator, IEventColumnReader, bool> predicate, SortContext context)
    {
        var state = new OrderedViewState();
        state.ReconcileLog(logId, reader);
        RebuildTo(state, predicate, context);

        return state;
    }

    private static void RebuildTo(OrderedViewState state, Func<EventLocator, IEventColumnReader, bool> predicate, SortContext context)
    {
        RebuildRequest request = state.BeginRebuild(predicate, context);
        Assert.True(state.TryAdoptRebuild(request, OrderedViewState.BuildIndex(request)));
    }

    private static void ReconcileInterleaved(OrderedViewState state, OrderedViewSample sample, IEventColumnReader[] readers)
    {
        int max = 0;
        for (int k = 0; k < readers.Length; k++) { max = Math.Max(max, readers[k].Count); }

        for (int i = 0; i < max; i++)
        {
            for (int k = 0; k < readers.Length; k++)
            {
                if (i < readers[k].Count) { state.ReconcileLog(sample.LogId(k), sample.PrefixReader(k, i + 1)); }
            }
        }
    }
}
