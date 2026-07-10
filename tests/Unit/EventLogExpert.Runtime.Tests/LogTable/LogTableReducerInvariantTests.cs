// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Tests.LogTable.TestSupport;
using EventLogExpert.Runtime.Tests.TestUtils;
using Reducers = EventLogExpert.Runtime.LogTable.Reducers;

namespace EventLogExpert.Runtime.Tests.LogTable;

// Named-edge invariants; the differential test is the broad guard. The live path is the columnar view, so parity is by
// value identity against the retained array-of-structs oracle.
public sealed class LogTableReducerInvariantTests
{
    [Fact]
    public void AppendingBatches_WhetherOutrankingOrStraddling_PreserveDisplayOrder()
    {
        // Level descending: Warning > Information > Error > Critical (ordinal). The old segmented list took a fast path
        // or a merge fallback depending on whether the batch outranked or straddled; the columnar view rebuilds the
        // whole order each time, so the only observable invariant is that display order stays exact regardless.
        var info = MakeEvent(0, 1, Time(0, 1), level: "Information");
        var error = MakeEvent(0, 2, Time(0, 2), level: "Error");
        var warning = MakeEvent(0, 3, Time(0, 3), level: "Warning");
        var info2 = MakeEvent(0, 4, Time(0, 4), level: "Information");
        var critical = MakeEvent(0, 5, Time(0, 5), level: "Critical");

        var data = new EventLogData("Log0", LogPathType.Channel);
        var state = Reducers.ReduceAddTable(new LogTableState(), new AddTableAction(data));
        state = Reducers.ReduceSetOrderBy(state, new SetOrderByAction(ColumnName.Level));
        state = Settle(state);

        state = AppendBatch(state, data.Id, info, error);
        AssertDisplayedExactly(state, [info, error]);

        // An outranking batch prepends ahead of the earlier events.
        state = AppendBatch(state, data.Id, warning);
        AssertDisplayedExactly(state, [info, error, warning]);

        // A straddling batch interleaves with the earlier events.
        state = AppendBatch(state, data.Id, info2, critical);
        AssertDisplayedExactly(state, [info, error, warning, info2, critical]);
    }

    [Fact]
    public void CloseLog_DropsExactlyThatLogsEvents_AndLeavesTheRestIntact()
    {
        var log0 = new[] { MakeEvent(0, 1, Time(0, 1)), MakeEvent(0, 2, Time(0, 2)) };
        var log1 = new[] { MakeEvent(1, 1, Time(1, 1)), MakeEvent(1, 2, Time(1, 2)) };
        var log2 = new[] { MakeEvent(2, 1, Time(2, 1)), MakeEvent(2, 2, Time(2, 2)) };
        var (state, ids) = SeedLogs(log0, log1, log2);

        state = Reducers.ReduceCloseLog(state, new CloseLogAction(ids[1]));

        Assert.Equal(0, state.EventsForLog(ids[1]).Count);

        var displayed = state.DisplayedEvents.Slice(0, state.DisplayedEvents.Count);

        foreach (var dropped in log1)
        {
            Assert.DoesNotContain(displayed, row => SameEvent(dropped, row.Lean));
        }

        // Two per-log tables plus a fresh combined table remain.
        Assert.Equal(2, state.EventTables.Count(table => !table.IsCombined));
        Assert.Single(state.EventTables, table => table.IsCombined);
        Assert.DoesNotContain(state.EventTables, table => table.Id == ids[1]);

        // Independent oracle (test-owned refs), so a wipe fails loudly.
        AssertDisplayedExactly(state, [.. log0, .. log2]);
    }

    [Fact]
    public void DefaultOrder_IsRecordIdForOneLog_DateAndTimeForTwo_AndReconcilesAcrossTheBoundary()
    {
        // RecordId and timestamp order deliberately disagree here.
        var a = MakeEvent(0, 1, Time(0, 30));
        var b = MakeEvent(0, 2, Time(0, 10));
        var c = MakeEvent(1, 1, Time(1, 20));
        var d = MakeEvent(1, 2, Time(1, 5));

        var log0 = new EventLogData("Log0", LogPathType.Channel);
        var state = Reducers.ReduceAddTable(new LogTableState(), new AddTableAction(log0));
        state = AppendBatch(state, log0.Id, a, b);

        // One log: natural RecordId order.
        Assert.Null(state.SortContext.OrderBy);
        AssertEveryLogIsOnTheCurrentContext(state);
        AssertDisplayedExactly(state, [a, b]);

        // Opening a second log flips the default to DateAndTime.
        var log1 = new EventLogData("Log1", LogPathType.Channel);
        state = Reducers.ReduceAddTable(state, new AddTableAction(log1));
        state = AppendBatch(state, log1.Id, c, d);

        Assert.Equal(ColumnName.DateAndTime, state.SortContext.OrderBy);
        AssertEveryLogIsOnTheCurrentContext(state);
        AssertDisplayedExactly(state, [a, b, c, d]);

        // Close back to one log: default returns to RecordId.
        state = Reducers.ReduceCloseLog(state, new CloseLogAction(log1.Id));

        Assert.Null(state.SortContext.OrderBy);
        AssertEveryLogIsOnTheCurrentContext(state);
        AssertDisplayedExactly(state, [a, b]);
    }

    [Fact]
    public void NullRecordIdTies_WithinALog_ResolveToAStableBijection()
    {
        // Null-RecordId, same-timestamp reads tie on every sort key; the physical locator (its Index) is what separates
        // them, so Rank must still map each displayed row to a distinct position.
        var sharedTime = Time(0, 10);
        var (state, _) = SeedLogs(
        [
            MakeEvent(0, 5, Time(0, 1)),
            MakeEvent(0, null, sharedTime, level: "Error"),
            MakeEvent(0, null, sharedTime, level: "Error"),
            MakeEvent(0, null, sharedTime, level: "Error"),
            MakeEvent(0, 6, Time(0, 20))
        ]);

        var displayed = state.DisplayedEvents;
        Assert.Equal(5, displayed.Count);

        var rows = displayed.Slice(0, displayed.Count);
        var positions = new HashSet<int>();

        foreach (var row in rows)
        {
            int index = displayed.Rank(row.Loc);

            Assert.InRange(index, 0, displayed.Count - 1);
            Assert.True(positions.Add(index), $"Index {index} was claimed by two events (not a bijection).");
        }

        Assert.Equal(displayed.Count, positions.Count);
    }

    [Fact]
    public void SetGroupBy_TransitionsEveryLogAtomically_ThenFastPathAppendsStayConsistent()
    {
        var log0 = new[] { MakeEvent(0, 1, Time(0, 5), source: "A"), MakeEvent(0, 2, Time(0, 3), source: "B") };
        var log1 = new[] { MakeEvent(1, 1, Time(1, 9), source: "B"), MakeEvent(1, 2, Time(1, 1), source: "A") };
        var (state, ids) = SeedLogs(log0, log1);

        state = Reducers.ReduceSetGroupBy(state, new SetGroupByAction(ColumnName.Source));
        state = Settle(state);

        AssertEveryLogIsOnTheCurrentContext(state);

        var append0 = MakeEvent(0, 3, Time(0, 7), source: "A");
        var append1 = MakeEvent(1, 3, Time(1, 4), source: "B");
        state = AppendMultiLogBatch(state, (ids[0], [append0]), (ids[1], [append1]));

        AssertEveryLogIsOnTheCurrentContext(state);
        AssertDisplayedExactly(state, [.. log0, .. log1, append0, append1]);
    }

    [Fact]
    public void SetOrderBy_TransitionsEveryLogAtomically_ThenFastPathAppendsStayConsistent()
    {
        var log0 = new[] { MakeEvent(0, 1, Time(0, 5)), MakeEvent(0, 2, Time(0, 3)) };
        var log1 = new[] { MakeEvent(1, 1, Time(1, 9)), MakeEvent(1, 2, Time(1, 1)) };
        var (state, ids) = SeedLogs(log0, log1);

        state = Reducers.ReduceSetOrderBy(state, new SetOrderByAction(ColumnName.Level));
        state = Settle(state);

        // Atomicity: no view may lag the old context.
        AssertEveryLogIsOnTheCurrentContext(state);

        var append0 = MakeEvent(0, 3, Time(0, 7), level: "Critical");
        var append1 = MakeEvent(1, 3, Time(1, 4), level: "Warning");
        state = AppendMultiLogBatch(state, (ids[0], [append0]), (ids[1], [append1]));

        AssertEveryLogIsOnTheCurrentContext(state);

        // Independent oracle: the six refs we created.
        AssertDisplayedExactly(state, [.. log0, .. log1, append0, append1]);
    }

    private static LogTableState AppendBatch(LogTableState state, EventLogId logId, params ResolvedEvent[] events) =>
        AppendMultiLogBatch(state, (logId, events));

    // The columnar view is a full snapshot rebuilt over the whole raw store per append, so accumulate the delta onto the
    // log's current events (rehydrated from the stored view) and store the full rebuilt view, mirroring production.
    private static LogTableState AppendMultiLogBatch(
        LogTableState state, params (EventLogId LogId, ResolvedEvent[] Events)[] perLog)
    {
        var views = new Dictionary<EventLogId, EventColumnView>(perLog.Length);

        foreach (var (logId, events) in perLog)
        {
            List<ResolvedEvent> all = state.PerLogEvents.TryGetValue(logId, out var existing)
                ? [.. existing.EnumerateDetail(), .. events]
                : [.. events];

            views[logId] = DisplayViewTestFactory.Build(logId, all);
        }

        return Reducers.ReduceAppendTableEventsBatch(state, new AppendTableEventsBatchAction { ViewsByLog = views });
    }

    // Asserts against test-owned refs, so a dropped event fails loudly. The view rehydrates fresh objects, so compare by
    // value identity rather than reference.
    private static void AssertDisplayedExactly(LogTableState state, IReadOnlyList<ResolvedEvent> expected)
    {
        var oracle = AosReferenceOrdering.OrderedEvents(
            expected,
            ResolvedEventOrdering.ResolveDefaultOrderBy(state.OrderBy, state.GroupBy, state.PerLogEvents.Count),
            state.IsDescending,
            state.GroupBy,
            state.IsGroupDescending);

        var displayed = state.DisplayedEvents;

        Assert.Equal(oracle.Count, displayed.Count);

        var rows = displayed.Slice(0, displayed.Count);

        for (int i = 0; i < oracle.Count; i++)
        {
            Assert.True(SameEvent(oracle[i], rows[i].Lean), $"Order mismatch at index {i}.");
        }
    }

    private static void AssertEveryLogIsOnTheCurrentContext(LogTableState state)
    {
        var displayed = new SortContext(
            ResolvedEventOrdering.ResolveDefaultOrderBy(state.OrderBy, state.GroupBy, state.PerLogEvents.Count),
            state.IsDescending,
            state.GroupBy,
            state.IsGroupDescending);

        foreach (var view in state.PerLogEvents.Values)
        {
            Assert.True(view.HasContext(displayed), "A per-log view lagged the state's displayed context.");
        }
    }

    private static ResolvedEvent MakeEvent(
        int logIndex, long? recordId, DateTime time, string level = "Information", string source = "Provider") =>
        new($"Log{logIndex}", LogPathType.Channel)
        {
            RecordId = recordId,
            TimeCreated = time,
            Level = level,
            Source = source
        };

    private static bool SameEvent(ResolvedEvent expected, ResolvedEvent actual) =>
        expected.RecordId == actual.RecordId
        && expected.Id == actual.Id
        && expected.TimeCreated == actual.TimeCreated
        && string.Equals(expected.Level, actual.Level, StringComparison.Ordinal)
        && string.Equals(expected.Source, actual.Source, StringComparison.Ordinal);

    private static (LogTableState State, EventLogId[] Ids) SeedLogs(params IReadOnlyList<ResolvedEvent>[] perLog)
    {
        var state = new LogTableState();
        var ids = new EventLogId[perLog.Length];
        var batch = new Dictionary<EventLogId, EventColumnView>(perLog.Length);

        for (int i = 0; i < perLog.Length; i++)
        {
            var data = new EventLogData($"Log{i}", LogPathType.Channel);
            ids[i] = data.Id;
            state = Reducers.ReduceAddTable(state, new AddTableAction(data));
            batch[data.Id] = DisplayViewTestFactory.Build(data.Id, perLog[i]);
        }

        state = Reducers.ReduceAppendTableEventsBatch(state, new AppendTableEventsBatchAction { ViewsByLog = batch });

        return (state, ids);
    }

    private static LogTableState Settle(LogTableState state)
    {
        var context = state.SortContext;
        var views = new Dictionary<EventLogId, EventColumnView>(state.PerLogEvents.Count);

        foreach (var (logId, view) in state.PerLogEvents)
        {
            views[logId] = view.WithContext(context);
        }

        return Reducers.ReduceDisplayReady(
            state,
            new DisplayReadyAction { Views = views, Version = state.DisplayListVersion });
    }

    private static DateTime Time(int logIndex, int seconds) =>
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(logIndex).AddSeconds(seconds);
}
