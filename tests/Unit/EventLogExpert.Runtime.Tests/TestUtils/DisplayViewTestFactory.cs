// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Tests.LogTable.TestSupport;

namespace EventLogExpert.Runtime.Tests.TestUtils;

internal static class DisplayViewTestFactory
{
    internal static AosReferenceView Build(EventLogId logId, IReadOnlyList<ResolvedEvent> events) =>
        AosReferenceView.Create(
            Reader(logId, events),
            Survivors(events.Count),
            orderBy: null,
            isDescending: false,
            groupBy: null,
            isGroupDescending: false);

    internal static AosReferenceView Build(EventLogId logId, IReadOnlyList<ResolvedEvent> events, SortContext context) =>
        AosReferenceView.Create(Reader(logId, events), Survivors(events.Count), context);

    private static IEventColumnReader Reader(EventLogId logId, IReadOnlyList<ResolvedEvent> events) =>
        EventColumnStore.Build(events, generation: 0, contentVersion: 0).CreateReader(logId);

    private static int[] Survivors(int count) => [.. Enumerable.Range(0, count)];
}
