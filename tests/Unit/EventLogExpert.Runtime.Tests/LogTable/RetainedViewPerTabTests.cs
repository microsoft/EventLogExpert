// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using EventLogExpert.Runtime.Tests.TestUtils;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class RetainedViewPerTabTests
{
    [Fact]
    public void AServedViewWithNoActiveTab_IsNotRecorded_BecauseThereIsNoTabToComeBackTo()
    {
        var tabA = EventLogId.Create();
        var tabB = EventLogId.Create();

        LogTableState onA = TwoTabs(tabA, tabB, active: tabA);
        var servedWithoutTab = ServedFor(onA) with { Identity = null };

        Assert.Empty(onA.RetainOnly(servedWithoutTab));
    }

    [Fact]
    public void AViewRetainedForOneTab_IsNeverServedToAnother()
    {
        var tabA = EventLogId.Create();
        var tabB = EventLogId.Create();

        LogTableState onA = TwoTabs(tabA, tabB, active: tabA);
        LogTableState retainingA = onA with { RetainedOrderedViews = onA.RetainOnly(ServedFor(onA)) };

        LogTableState onBWithOnlyAsView = retainingA with { ActiveEventLogId = tabB };

        Assert.False(onBWithOnlyAsView.IsRetainedViewServable(tabB));
    }

    [Fact]
    public void EachTabKeepsItsOwnLastServedView_SoReturningToOneStillHasSomethingToShow()
    {
        var tabA = EventLogId.Create();
        var tabB = EventLogId.Create();

        LogTableState onA = TwoTabs(tabA, tabB, active: tabA);
        LogTableState retainingA = onA with { RetainedOrderedViews = onA.RetainOnly(ServedFor(onA)) };

        Assert.True(retainingA.IsRetainedViewServable(tabA));

        LogTableState onB = retainingA with { ActiveEventLogId = tabB };
        LogTableState retainingBoth = onB with { RetainedOrderedViews = onB.RetainOnly(ServedFor(onB)) };

        Assert.True(retainingBoth.IsRetainedViewServable(tabB), "the tab just served must be servable");

        LogTableState backOnA = retainingBoth with { ActiveEventLogId = tabA };

        Assert.True(
            backOnA.IsRetainedViewServable(tabA),
            "serving another tab must not evict the view this one was last showing");
    }

    [Fact]
    public void EntriesForTabsThatAreGone_AreDroppedOnTheNextWrite_RatherThanAccumulating()
    {
        var tabA = EventLogId.Create();
        var tabB = EventLogId.Create();

        LogTableState onA = TwoTabs(tabA, tabB, active: tabA);
        LogTableState retainingA = onA with { RetainedOrderedViews = onA.RetainOnly(ServedFor(onA)) };

        Assert.True(retainingA.RetainedOrderedViews.ContainsKey(tabA));

        LogTableState afterClose = retainingA with
        {
            ActiveEventLogId = tabB, EventTables = [new LogView(tabB) { LogName = "B" }]
        };

        LogTableState afterNextWrite = afterClose with
        {
            RetainedOrderedViews = afterClose.RetainOnly(ServedFor(afterClose))
        };

        Assert.False(afterNextWrite.RetainedOrderedViews.ContainsKey(tabA), "a closed tab's view must not be kept");
        Assert.True(afterNextWrite.RetainedOrderedViews.ContainsKey(tabB));
    }

    [Fact]
    public void OpeningAThirdLogWhileTheCombinedTabIsActive_RecordsWhatWasOnScreen()
    {
        var logA = EventLogId.Create();
        var logB = EventLogId.Create();
        var combinedTab = EventLogId.Create();

        var state = new LogTableState
        {
            ActiveEventLogId = combinedTab,
            CommittedEffectiveOrderBy = ColumnName.DateAndTime,
            EventTables =
            [
                new LogView(combinedTab) { GroupId = LogTabGroupId.AllLogs },
                new LogView(logA) { LogName = "A" },
                new LogView(logB) { LogName = "B" }
            ]
        };

        var served = new OrderedViewReady(
            SnapshotVersion: 1,
            Identity: state.ViewIdentity,
            Sequence: state.HighestInvalidationSequence,
            SingleLogId: null,
            InScope: [new LogGeneration(logA, 0), new LogGeneration(logB, 0)],
            View: LogTableState.EmptyView,
            Config: state.CommittedSortContext,
            Filter: state.AppliedFilter);

        LogTableState serving = state with { ActiveOrderedView = served };

        Assert.NotNull(serving.ServingOrderedView);

        LogTableState afterOpen = Reducers.ReduceAddTable(
            serving,
            new AddTableAction(new EventLogData("C", LogPathType.Channel)));

        Assert.Null(afterOpen.ServingOrderedView);
        Assert.Same(served, afterOpen.RetainedFor(served));
    }

    private static OrderedViewReady ServedFor(LogTableState state) =>
        new(
            SnapshotVersion: 1,
            Identity: state.ViewIdentity,
            Sequence: state.HighestInvalidationSequence,
            SingleLogId: state.ActiveEventLogId,
            InScope: [new LogGeneration(state.ActiveEventLogId!.Value, 0)],
            View: LogTableState.EmptyView,
            Config: state.CommittedSortContext,
            Filter: state.AppliedFilter);

    private static LogTableState TwoTabs(EventLogId tabA, EventLogId tabB, EventLogId active) =>
        new()
        {
            ActiveEventLogId = active,
            EventTables = [new LogView(tabA) { LogName = "A" }, new LogView(tabB) { LogName = "B" }]
        };
}
