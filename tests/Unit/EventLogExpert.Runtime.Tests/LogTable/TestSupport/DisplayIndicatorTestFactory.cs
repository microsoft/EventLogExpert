// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Tests.TestUtils;

namespace EventLogExpert.Runtime.Tests.LogTable.TestSupport;

internal static class DisplayIndicatorTestFactory
{
    internal static ResolvedEvent Event(int recordId) =>
        new("TestLog", LogPathType.Channel)
        {
            RecordId = recordId,
            TimeCreated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(recordId),
            Id = 1000,
            Level = "Information",
            Source = "Alpha",
            LogName = "TestLog"
        };

    internal static OrderedViewPresentation PresentationFor(DisplayIndicatorKind kind, long revision)
    {
        var logId = EventLogId.Create();

        (PresentationState state, int rows, bool stale) = kind switch
        {
            DisplayIndicatorKind.None => (PresentationState.Current, 1, false),
            DisplayIndicatorKind.EmptyPending => (PresentationState.Updating, 0, false),
            DisplayIndicatorKind.ReorderPending => (PresentationState.Updating, 1, true),
            _ => (PresentationState.Faulted, 1, false)
        };

        IEventColumnView view = rows == 0 ?
            LogTableState.EmptyView :
            DisplayViewTestFactory.Build(logId, [Event(1)]);

        var presentation = new OrderedViewPresentation(
            view,
            logId,
            default,
            state,
            revision,
            FaultCause: state == PresentationState.Faulted ? "InvalidOperationException: bad predicate" : null,
            OrderingIsStale: stale);

        Assert.Equal(kind, presentation.IndicatorKind);

        return presentation;
    }
}
