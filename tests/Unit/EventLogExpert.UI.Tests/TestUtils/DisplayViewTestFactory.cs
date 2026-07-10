// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;

namespace EventLogExpert.UI.Tests.TestUtils;

internal static class DisplayViewTestFactory
{
    // Identity-ordered, unfiltered view over the given events (generation 0, physical index == list position).
    internal static IEventColumnView Identity(IReadOnlyList<ResolvedEvent> events, ColumnName? groupBy = null)
    {
        var reader = EventColumnStore.Build(events, 0, 0).CreateReader(EventLogId.Create());
        int[] survivors = new int[reader.Count];

        for (int i = 0; i < survivors.Length; i++) { survivors[i] = i; }

        return EventColumnView.Create(reader, survivors, orderBy: null, isDescending: false, groupBy: groupBy, isGroupDescending: false);
    }
}
