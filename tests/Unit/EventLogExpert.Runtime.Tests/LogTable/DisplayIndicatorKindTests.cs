// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Tests.TestUtils;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class DisplayIndicatorKindTests
{
    [Fact]
    public void CurrentWithNoRows_OwesNothing_BecauseTheEmptinessIsTheAnswer()
    {
        Assert.Equal(DisplayIndicatorKind.None, Presentation(rows: 0, PresentationState.Current).IndicatorKind);
    }

    [Fact]
    public void CurrentWithRows_OwesNothing()
    {
        Assert.Equal(DisplayIndicatorKind.None, Presentation(rows: 3, PresentationState.Current).IndicatorKind);
    }

    [Fact]
    public void FaultedWithNoRows_OwesTheFault()
    {
        Assert.Equal(DisplayIndicatorKind.Fault, Presentation(rows: 0, PresentationState.Faulted).IndicatorKind);
    }

    [Fact]
    public void FaultedWithRows_OwesTheFault_NotTheReorderItAlsoSatisfies()
    {
        var faultedMidSort = Presentation(rows: 3, PresentationState.Faulted, orderingIsStale: true);

        Assert.Equal(DisplayIndicatorKind.Fault, faultedMidSort.IndicatorKind);
    }

    [Fact]
    public void UpdatingWithNoRows_OwesTheEmptyExplanation_EvenWhenASortIsAlsoPending()
    {
        var emptyAndReordering = Presentation(rows: 0, PresentationState.Updating, orderingIsStale: true);

        Assert.Equal(DisplayIndicatorKind.EmptyPending, emptyAndReordering.IndicatorKind);
    }

    [Fact]
    public void UpdatingWithRowsAndAPendingReorder_OwesTheReorder()
    {
        var reordering = Presentation(rows: 3, PresentationState.Updating, orderingIsStale: true);

        Assert.Equal(DisplayIndicatorKind.ReorderPending, reordering.IndicatorKind);
    }

    [Fact]
    public void UpdatingWithRowsAndNoPendingReorder_OwesNothing_SoOrdinaryLiveTailDoesNotNag()
    {
        Assert.Equal(DisplayIndicatorKind.None, Presentation(rows: 3, PresentationState.Updating).IndicatorKind);
    }

    private static ResolvedEvent Event(int recordId) =>
        new("TestLog", LogPathType.Channel)
        {
            RecordId = recordId,
            TimeCreated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(recordId),
            Id = 1000,
            Level = "Information",
            Source = "Alpha",
            LogName = "TestLog"
        };

    private static OrderedViewPresentation Presentation(
        int rows,
        PresentationState state,
        bool orderingIsStale = false)
    {
        var logId = EventLogId.Create();

        IEventColumnView view = rows == 0 ?
            LogTableState.EmptyView :
            DisplayViewTestFactory.Build(logId, [.. Enumerable.Range(1, rows).Select(Event)]);

        Assert.Equal(rows, view.Count);

        return new OrderedViewPresentation(
            view,
            logId,
            default,
            state,
            Revision: 1,
            FaultCause: state == PresentationState.Faulted ? "InvalidOperationException: bad predicate" : null,
            OrderingIsStale: orderingIsStale);
    }
}
