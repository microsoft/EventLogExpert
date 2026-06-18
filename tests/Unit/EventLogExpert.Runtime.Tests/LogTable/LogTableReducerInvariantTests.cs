// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using Reducers = EventLogExpert.Runtime.LogTable.Reducers;

namespace EventLogExpert.Runtime.Tests.LogTable;

// Named-edge invariants; the differential test is the broad guard.
public sealed class LogTableReducerInvariantTests
{
    [Fact]
    public void AppendingAStraddlingBatch_TakesTheMergeFallback_WhileOutrankingBatchesStayOnTheFastPath()
    {
        // Level descending: Warning > Information > Error > Critical (ordinal).
        var info = MakeEvent(0, 1, Time(0, 1), level: "Information");
        var error = MakeEvent(0, 2, Time(0, 2), level: "Error");
        var warning = MakeEvent(0, 3, Time(0, 3), level: "Warning");
        var info2 = MakeEvent(0, 4, Time(0, 4), level: "Information");
        var critical = MakeEvent(0, 5, Time(0, 5), level: "Critical");

        var data = new EventLogData("Log0", LogPathType.Channel);
        var state = Reducers.ReduceAddTable(new LogTableState(), new AddTableAction(data));
        state = Reducers.ReduceSetOrderBy(state, new SetOrderByAction(ColumnName.Level));

        state = AppendBatch(state, data.Id, info, error);
        Assert.Equal(1, SegmentCountOf(state, data.Id));

        // An outranking batch prepends (the fast path adds a segment).
        state = AppendBatch(state, data.Id, warning);
        Assert.Equal(2, SegmentCountOf(state, data.Id));

        // A straddling batch forces the full merge to one segment.
        state = AppendBatch(state, data.Id, info2, critical);
        Assert.Equal(1, SegmentCountOf(state, data.Id));

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

        Assert.Empty(state.EventsForLog(ids[1]));

        foreach (var dropped in log1)
        {
            Assert.DoesNotContain(dropped, state.DisplayedEvents);
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
        // Null-RecordId, same-timestamp reads tie; reference identity separates them.
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

        var positions = new HashSet<int>();

        foreach (var resolved in displayed)
        {
            int index = ResolvedEventIndex.IndexOf(
                displayed, resolved, state.OrderBy, state.IsDescending, state.GroupBy, state.IsGroupDescending);

            Assert.InRange(index, 0, displayed.Count - 1);
            Assert.True(ReferenceEquals(displayed[index], resolved), "IndexOf resolved to a different reference.");
            Assert.True(positions.Add(index), $"Index {index} was claimed by two events (not a bijection).");
        }
    }

    [Fact]
    public void SetGroupBy_TransitionsEveryLogAtomically_ThenFastPathAppendsStayConsistent()
    {
        var log0 = new[] { MakeEvent(0, 1, Time(0, 5), source: "A"), MakeEvent(0, 2, Time(0, 3), source: "B") };
        var log1 = new[] { MakeEvent(1, 1, Time(1, 9), source: "B"), MakeEvent(1, 2, Time(1, 1), source: "A") };
        var (state, ids) = SeedLogs(log0, log1);

        state = Reducers.ReduceSetGroupBy(state, new SetGroupByAction(ColumnName.Source));

        AssertEveryLogIsOnTheCurrentContext(state);

        var append0 = MakeEvent(0, 3, Time(0, 7), source: "A");
        var append1 = MakeEvent(1, 3, Time(1, 4), source: "B");
        state = Reducers.ReduceAppendTableEventsBatch(state, new AppendTableEventsBatchAction(
            new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>
            {
                [ids[0]] = [append0],
                [ids[1]] = [append1]
            }));

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

        // Atomicity: no list may lag the old context.
        AssertEveryLogIsOnTheCurrentContext(state);

        var append0 = MakeEvent(0, 3, Time(0, 7), level: "Critical");
        var append1 = MakeEvent(1, 3, Time(1, 4), level: "Warning");
        state = Reducers.ReduceAppendTableEventsBatch(state, new AppendTableEventsBatchAction(
            new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>
            {
                [ids[0]] = [append0],
                [ids[1]] = [append1]
            }));

        AssertEveryLogIsOnTheCurrentContext(state);

        // Independent oracle: the six refs we created.
        AssertDisplayedExactly(state, [.. log0, .. log1, append0, append1]);
    }

    private static LogTableState AppendBatch(LogTableState state, EventLogId logId, params ResolvedEvent[] events) =>
        Reducers.ReduceAppendTableEventsBatch(state, new AppendTableEventsBatchAction(
            new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>> { [logId] = events }));

    // Asserts against test-owned refs, so a dropped event fails loudly.
    private static void AssertDisplayedExactly(LogTableState state, IReadOnlyList<ResolvedEvent> expected)
    {
        var oracle = expected.SortEvents(
            ResolvedEventOrdering.ResolveDefaultOrderBy(state.OrderBy, state.GroupBy, state.PerLogEvents.Count),
            state.IsDescending,
            state.GroupBy,
            state.IsGroupDescending);

        var displayed = state.DisplayedEvents;

        Assert.Equal(oracle.Count, displayed.Count);

        for (int i = 0; i < oracle.Count; i++)
        {
            Assert.True(ReferenceEquals(oracle[i], displayed[i]), $"Order mismatch at index {i}.");
        }
    }

    private static void AssertEveryLogIsOnTheCurrentContext(LogTableState state)
    {
        foreach (var list in state.PerLogEvents.Values)
        {
            Assert.True(list.HasContext(state.SortContext), "A per-log list lagged the state's sort context.");
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

    private static (LogTableState State, EventLogId[] Ids) SeedLogs(params IReadOnlyList<ResolvedEvent>[] perLog)
    {
        var state = new LogTableState();
        var ids = new EventLogId[perLog.Length];
        var batch = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>(perLog.Length);

        for (int i = 0; i < perLog.Length; i++)
        {
            var data = new EventLogData($"Log{i}", LogPathType.Channel);
            ids[i] = data.Id;
            state = Reducers.ReduceAddTable(state, new AddTableAction(data));
            batch[data.Id] = perLog[i];
        }

        state = Reducers.ReduceAppendTableEventsBatch(state, new AppendTableEventsBatchAction(batch));

        return (state, ids);
    }

    private static int SegmentCountOf(LogTableState state, EventLogId logId) => state.PerLogEvents[logId].SegmentCount;

    private static DateTime Time(int logIndex, int seconds) =>
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(logIndex).AddSeconds(seconds);
}
