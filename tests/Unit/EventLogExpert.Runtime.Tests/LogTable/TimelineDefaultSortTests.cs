// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using EventLogExpert.Runtime.Tests.LogTable.TestSupport;
using EventLogExpert.Runtime.Tests.TestUtils;
using Reducers = EventLogExpert.Runtime.LogTable.Reducers;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class TimelineDefaultSortTests
{
    private static readonly DateTime s_baseTime = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ACombinedViewWhoseMembersDisagree_ReportsTheOrderingStale()
    {
        var settled = new SortContext(ColumnName.DateAndTime, true, null, false);
        var stale = new SortContext(ColumnName.EventId, true, null, false);

        var fresh = ViewOver(settled);
        var behind = ViewOver(stale);

        Assert.True(new AosReferenceCombinedView([fresh], settled).HasContext(settled));
        Assert.False(new AosReferenceCombinedView([fresh, behind], settled).HasContext(settled));

        Assert.False(new AosReferenceCombinedView([fresh], stale).HasContext(settled));
    }

    [Fact]
    public void ClearingASortThatResolvesToTheSameOrder_ReportsNothingStale()
    {
        var logId = EventLogId.Create();

        var state = new LogTableState
        {
            ActiveEventLogId = logId,
            EventTables = [new LogView(logId)],
            TimelineVisible = true,
            OrderBy = ColumnName.DateAndTime,
            CommittedEffectiveOrderBy = ColumnName.DateAndTime,
            RequestedOrderBy = ColumnName.DateAndTime
        };

        (LogTableState serving, IEventColumnView served) = ServingRetained(state);
        var cleared = serving with { RequestedOrderBy = null };

        Assert.Same(served, cleared.GetActiveDisplayedEvents());
        Assert.True(cleared.GetActiveDisplayedEvents().Count > 0);

        Assert.True(cleared.IsRetainedViewServable(logId));
        Assert.True(cleared.HasPendingSortChange);
        Assert.False(cleared.OrderingIsStale);
    }

    [Fact]
    public void ClearingASortThatResolvesToTheSameOrder_StillCommitsTheRawSelection()
    {
        var logId = EventLogId.Create();

        var state = new LogTableState
        {
            ActiveEventLogId = logId,
            EventTables = [new LogView(logId)],
            TimelineVisible = true,
            OrderBy = ColumnName.DateAndTime,
            CommittedEffectiveOrderBy = ColumnName.DateAndTime,
            RequestedOrderBy = ColumnName.DateAndTime
        };

        var cleared = state with { RequestedOrderBy = null };

        Assert.True(cleared.HasPendingSortChange);
        Assert.Equal(cleared.SortContext, cleared.CommittedSortContext);

        var adopted = Reducers.ReduceOrderedViewUpdated(
            cleared,
            new OrderedViewUpdatedAction(
                new OrderedViewReady(
                    SnapshotVersion: cleared.LastPublishedSnapshotVersion + 1,
                    Identity: cleared.ViewIdentity,
                    Sequence: cleared.HighestInvalidationSequence,
                    SingleLogId: logId,
                    InScope: [new LogGeneration(logId, 0)],
                    View: LogTableState.EmptyView,
                    Config: cleared.SortContext,
                    Filter: cleared.AppliedFilter)));

        Assert.False(adopted.HasPendingSortChange);
        Assert.Null(adopted.OrderBy);
        Assert.Equal(ColumnName.DateAndTime, adopted.CommittedEffectiveOrderBy);
    }

    [Fact]
    public void RequestingASortTheRowsAreNotInYet_ReportsTheOrderingStale()
    {
        var logId = EventLogId.Create();
        var state = new LogTableState { ActiveEventLogId = logId, EventTables = [new LogView(logId)] };

        (LogTableState serving, IEventColumnView served) = ServingRetained(state);
        var sorted = serving with { RequestedOrderBy = ColumnName.EventId };

        Assert.Same(served, sorted.GetActiveDisplayedEvents());
        Assert.True(sorted.GetActiveDisplayedEvents().Count > 0);

        Assert.True(sorted.OrderingIsStale);
    }

    [Fact]
    public void ResolveDefaultOrderBy_WhenGrouped_IgnoresTimeline()
    {
        Assert.Null(
            ResolvedEventOrdering.ResolveDefaultOrderBy(orderBy: null, groupBy: ColumnName.Source, logCount: 1, timelineVisible: true));
    }

    [Fact]
    public void ResolveDefaultOrderBy_WithExplicitSort_IgnoresTimeline()
    {
        Assert.Equal(
            ColumnName.EventId,
            ResolvedEventOrdering.ResolveDefaultOrderBy(orderBy: ColumnName.EventId, groupBy: null, logCount: 1, timelineVisible: true));
    }

    [Theory]
    [InlineData(1, false, null)]
    [InlineData(1, true, ColumnName.DateAndTime)]
    [InlineData(2, false, ColumnName.DateAndTime)]
    [InlineData(2, true, ColumnName.DateAndTime)]
    public void ResolveDefaultOrderBy_WithoutExplicitSort_FollowsLogCountAndTimeline(
        int logCount,
        bool timelineVisible,
        ColumnName? expected)
    {
        var resolved = ResolvedEventOrdering.ResolveDefaultOrderBy(null, null, logCount, timelineVisible);

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void RevealingTheTimeline_LeavesTheRowsBehindTheOrderTheyAreNowAskedFor()
    {
        var logId = EventLogId.Create();
        (LogTableState state, IEventColumnView served) = ServingRetained(
            new LogTableState { ActiveEventLogId = logId, EventTables = [new LogView(logId)] });

        Assert.False(state.OrderingIsStale);

        var shown = Reducers.ReduceSetHistogramVisible(state, new SetHistogramVisibleAction(true));

        Assert.Same(served, shown.GetActiveDisplayedEvents());
        Assert.True(shown.GetActiveDisplayedEvents().Count > 0);

        Assert.False(shown.HasPendingSortChange);
        Assert.NotEqual(shown.SortContext, shown.CommittedSortContext);
        Assert.True(shown.OrderingIsStale);
    }

    [Fact]
    public void RevealingTheTimeline_ThenAdoptingTheRebuild_AdvancesTheFrozenOrder()
    {
        var logId = EventLogId.Create();
        var state = new LogTableState { ActiveEventLogId = logId, EventTables = [new LogView(logId)] };

        var shown = Reducers.ReduceSetHistogramVisible(state, new SetHistogramVisibleAction(true));

        Assert.False(shown.HasPendingSortChange);
        Assert.Null(shown.CommittedEffectiveOrderBy);

        var adopted = Reducers.ReduceOrderedViewUpdated(
            shown,
            new OrderedViewUpdatedAction(
                new OrderedViewReady(
                    SnapshotVersion: shown.LastPublishedSnapshotVersion + 1,
                    Identity: shown.ViewIdentity,
                    Sequence: shown.HighestInvalidationSequence,
                    SingleLogId: logId,
                    InScope: [new LogGeneration(logId, 0)],
                    View: LogTableState.EmptyView,
                    Config: shown.SortContext,
                    Filter: shown.AppliedFilter)));

        Assert.Equal(ColumnName.DateAndTime, adopted.CommittedEffectiveOrderBy);
        Assert.Equal(adopted.SortContext, adopted.CommittedSortContext);
        Assert.False(adopted.OrderingIsStale);
    }

    [Fact]
    public void SetHistogramVisible_OnCombinedView_LeavesTheEffectiveOrderAtDateAndTime()
    {
        var log1 = EventLogId.Create();
        var log2 = EventLogId.Create();
        var state = new LogTableState { EventTables = [new LogView(log1), new LogView(log2)] };

        Assert.Equal(ColumnName.DateAndTime, state.SortContext.OrderBy);

        var shown = Reducers.ReduceSetHistogramVisible(state, new SetHistogramVisibleAction(true));

        Assert.True(shown.TimelineVisible);
        Assert.Equal(ColumnName.DateAndTime, shown.SortContext.OrderBy);
    }

    [Fact]
    public void SetHistogramVisible_OnSingleDefaultLog_FlipsTheEffectiveOrderToDateAndTime()
    {
        var logId = EventLogId.Create();
        var state = new LogTableState { EventTables = [new LogView(logId)] };

        Assert.Null(state.SortContext.OrderBy);

        var shown = Reducers.ReduceSetHistogramVisible(state, new SetHistogramVisibleAction(true));

        Assert.True(shown.TimelineVisible);
        Assert.Equal(ColumnName.DateAndTime, shown.SortContext.OrderBy);
    }

    [Fact]
    public void SetHistogramVisible_WhenUnchanged_ReturnsSameState()
    {
        var state = new LogTableState { TimelineVisible = true };

        var result = Reducers.ReduceSetHistogramVisible(state, new SetHistogramVisibleAction(true));

        Assert.Same(state, result);
    }

    [Fact]
    public void SetHistogramVisible_WithExplicitSort_LeavesTheEffectiveOrderOnTheExplicitColumn()
    {
        var logId = EventLogId.Create();
        var state = new LogTableState { EventTables = [new LogView(logId)], RequestedOrderBy = ColumnName.EventId };

        Assert.Equal(ColumnName.EventId, state.SortContext.OrderBy);

        var shown = Reducers.ReduceSetHistogramVisible(state, new SetHistogramVisibleAction(true));

        Assert.True(shown.TimelineVisible);
        Assert.Equal(ColumnName.EventId, shown.SortContext.OrderBy);
    }

    [Fact]
    public void SingleLog_WithTimelineHidden_OrdersByRecordId()
    {
        var logId = EventLogId.Create();
        var state = new LogTableState { ActiveEventLogId = logId, EventTables = [new LogView(logId)], TimelineVisible = false };

        Assert.Null(state.SortContext.OrderBy);
        Assert.Equal(new long?[] { 3, 2, 1 }, DisplayedRecordIds(state));
    }

    [Fact]
    public void SingleLog_WithTimelineVisible_OrdersByDateAndTime()
    {
        var logId = EventLogId.Create();
        var state = new LogTableState { ActiveEventLogId = logId, EventTables = [new LogView(logId)], TimelineVisible = true };

        Assert.Equal(ColumnName.DateAndTime, state.SortContext.OrderBy);

        Assert.Equal(new long?[] { 1, 3, 2 }, DisplayedRecordIds(state));
    }

    private static long?[] DisplayedRecordIds(LogTableState state) =>
        [.. ViewOver(state.SortContext).EnumerateDetail().Select(resolved => resolved.RecordId)];

    private static ResolvedEvent[] LogEvents(string owningLog) =>
    [
        new(owningLog, LogPathType.Channel) { Id = 10, RecordId = 1, TimeCreated = s_baseTime.AddMinutes(3) },
        new(owningLog, LogPathType.Channel) { Id = 20, RecordId = 2, TimeCreated = s_baseTime.AddMinutes(1) },
        new(owningLog, LogPathType.Channel) { Id = 30, RecordId = 3, TimeCreated = s_baseTime.AddMinutes(2) }
    ];

    private static ResolvedEvent[] OutOfRecordOrderEvents() => LogEvents("TestLog");

    private static (LogTableState State, IEventColumnView Served) ServingRetained(LogTableState state)
    {
        EventLogId activeLogId = state.ActiveEventLogId!.Value;
        AosReferenceView servedView = ViewOver(state.CommittedSortContext);

        var served = new OrderedViewReady(
            SnapshotVersion: 1,
            Identity: state.ViewIdentity,
            Sequence: state.HighestInvalidationSequence,
            SingleLogId: activeLogId,
            InScope: [new LogGeneration(activeLogId, 0)],
            View: servedView,
            Config: state.CommittedSortContext,
            Filter: state.AppliedFilter);

        EventLogId decoyLogId = EventLogId.Create();
        LogTableState decoyState = state with { ActiveEventLogId = decoyLogId, EventTables = [new LogView(decoyLogId)] };
        var decoy = new OrderedViewReady(
            SnapshotVersion: 1,
            Identity: decoyState.ViewIdentity,
            Sequence: decoyState.HighestInvalidationSequence,
            SingleLogId: decoyLogId,
            InScope: [new LogGeneration(decoyLogId, 0)],
            View: ViewOver(decoyState.CommittedSortContext),
            Config: decoyState.CommittedSortContext,
            Filter: decoyState.AppliedFilter);

        return (
            state with { RetainedOrderedViews = RetainedViewTestFactory.RetainedMap(served, decoy) },
            servedView);
    }

    private static AosReferenceView ViewOver(SortContext context)
    {
        ResolvedEvent[] events = [.. OutOfRecordOrderEvents()];
        var reader = EventColumnStore.Build(events, generation: 0, contentVersion: 0).CreateReader(EventLogId.Create());

        return AosReferenceView.Create(reader, [.. Enumerable.Range(0, events.Length)], context);
    }
}

