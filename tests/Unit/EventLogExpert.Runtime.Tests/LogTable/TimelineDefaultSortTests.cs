// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.Runtime.LogTable;
using Reducers = EventLogExpert.Runtime.LogTable.Reducers;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class TimelineDefaultSortTests
{
    private static readonly DateTime s_baseTime = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ResolveDefaultOrderBy_WhenGrouped_IgnoresTimeline()
    {
        // Grouping short-circuits the default, so the timeline flag cannot force a column sort.
        Assert.Null(
            ResolvedEventOrdering.ResolveDefaultOrderBy(orderBy: null, ColumnName.Source, logCount: 1, timelineVisible: true));
    }

    [Fact]
    public void ResolveDefaultOrderBy_WithExplicitSort_IgnoresTimeline()
    {
        // An explicit column sort always wins over the timeline-driven default, even for a single log.
        Assert.Equal(
            ColumnName.EventId,
            ResolvedEventOrdering.ResolveDefaultOrderBy(ColumnName.EventId, groupBy: null, logCount: 1, timelineVisible: true));
    }

    [Theory]
    [InlineData(1, false, null)]                        // one log, timeline hidden -> Record ID (null)
    [InlineData(1, true, ColumnName.DateAndTime)]       // one log, timeline shown -> Date/Time
    [InlineData(2, false, ColumnName.DateAndTime)]      // combined view -> Date/Time regardless of the timeline
    [InlineData(2, true, ColumnName.DateAndTime)]
    public void ResolveDefaultOrderBy_WithoutExplicitSort_FollowsLogCountAndTimeline(
        int logCount,
        bool timelineVisible,
        ColumnName? expected)
    {
        var resolved = ResolvedEventOrdering.ResolveDefaultOrderBy(
            orderBy: null,
            groupBy: null,
            logCount,
            timelineVisible);

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void SetHistogramVisible_OnCombinedView_FlipsFlagWithoutBumpingDisplayVersion()
    {
        var log1 = EventLogId.Create();
        var log2 = EventLogId.Create();
        var state = new LogTableState()
            .WithLogEvents(log1, LogEvents("Log1"))
            .WithLogEvents(log2, LogEvents("Log2"));
        int before = state.DisplayListVersion;

        var shown = Reducers.ReduceSetHistogramVisible(state, new SetHistogramVisibleAction(true));

        // A combined view is Date/Time either way, so no republish follows and the version must not move (it would strand
        // an in-flight republish carrying the pre-bump version).
        Assert.True(shown.TimelineVisible);
        Assert.Equal(before, shown.DisplayListVersion);
    }

    [Fact]
    public void SetHistogramVisible_OnSingleDefaultLog_FlipsFlagAndBumpsDisplayVersion()
    {
        var logId = EventLogId.Create();
        var state = new LogTableState().WithLogEvents(logId, OutOfRecordOrderEvents());
        int before = state.DisplayListVersion;

        var shown = Reducers.ReduceSetHistogramVisible(state, new SetHistogramVisibleAction(true));

        Assert.True(shown.TimelineVisible);
        Assert.Equal(before + 1, shown.DisplayListVersion);
    }

    [Fact]
    public void SetHistogramVisible_WhenUnchanged_ReturnsSameState()
    {
        var logId = EventLogId.Create();
        var state = new LogTableState { TimelineVisible = true }.WithLogEvents(logId, OutOfRecordOrderEvents());

        var result = Reducers.ReduceSetHistogramVisible(state, new SetHistogramVisibleAction(true));

        Assert.Same(state, result);
    }

    [Fact]
    public void SetHistogramVisible_WithExplicitSort_FlipsFlagWithoutBumpingDisplayVersion()
    {
        var logId = EventLogId.Create();
        var state = new LogTableState { RequestedOrderBy = ColumnName.EventId }
            .WithLogEvents(logId, OutOfRecordOrderEvents());
        int before = state.DisplayListVersion;

        var shown = Reducers.ReduceSetHistogramVisible(state, new SetHistogramVisibleAction(true));

        Assert.True(shown.TimelineVisible);
        Assert.Equal(before, shown.DisplayListVersion);
    }

    [Fact]
    public void SingleLog_WithTimelineHidden_OrdersByRecordId()
    {
        var logId = EventLogId.Create();
        var state = new LogTableState { ActiveEventLogId = logId, TimelineVisible = false }
            .WithLogEvents(logId, OutOfRecordOrderEvents());

        Assert.Null(state.SortContext.OrderBy);
        Assert.Equal(new long?[] { 3, 2, 1 }, DisplayedRecordIds(state));
    }

    [Fact]
    public void SingleLog_WithTimelineVisible_OrdersByDateAndTime()
    {
        var logId = EventLogId.Create();
        var state = new LogTableState { ActiveEventLogId = logId, TimelineVisible = true }
            .WithLogEvents(logId, OutOfRecordOrderEvents());

        Assert.Equal(ColumnName.DateAndTime, state.SortContext.OrderBy);

        // Record IDs 1/2/3 carry times +3/+1/+2, so Date/Time descending is 1, 3, 2 (not the Record ID order 3, 2, 1).
        Assert.Equal(new long?[] { 1, 3, 2 }, DisplayedRecordIds(state));
    }

    private static long?[] DisplayedRecordIds(LogTableState state) =>
        [.. state.DisplayedEvents.EnumerateDetail().Select(resolved => resolved.RecordId)];

    private static ResolvedEvent[] LogEvents(string owningLog) =>
    [
        new(owningLog, LogPathType.Channel) { Id = 10, RecordId = 1, TimeCreated = s_baseTime.AddMinutes(3) },
        new(owningLog, LogPathType.Channel) { Id = 20, RecordId = 2, TimeCreated = s_baseTime.AddMinutes(1) },
        new(owningLog, LogPathType.Channel) { Id = 30, RecordId = 3, TimeCreated = s_baseTime.AddMinutes(2) }
    ];

    private static ResolvedEvent[] OutOfRecordOrderEvents() => LogEvents("TestLog");
}
