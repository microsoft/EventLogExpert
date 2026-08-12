// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Tests.LogTable.TestSupport;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class ColumnDirectSortTieBreakTests
{
    [Fact]
    public void HeavyTies_ColumnDirectSort_TieBreaksByPhysicalIndex()
    {
        long?[] recordIds = [5, null, 5, 2, null, 5, 2, 8, null, 2];
        ResolvedEvent[] events = [.. recordIds.Select(BuildTiedEvent)];

        EventLogId logId = EventLogId.Create();
        IEventColumnReader reader = EventColumnStore.Build(events, generation: 0, contentVersion: 0).CreateReader(logId);
        int[] survivors = [.. Enumerable.Range(0, events.Length)];

        AssertTiedOrder(reader, events, survivors, new SortContext(ColumnName.Source, false, null, false),
            [1, 4, 8, 3, 6, 9, 0, 2, 5, 7]);
        AssertTiedOrder(reader, events, survivors, new SortContext(ColumnName.Source, true, null, false),
            [7, 0, 2, 5, 3, 6, 9, 1, 4, 8]);
    }

    private static void AssertTiedOrder(
        IEventColumnReader reader,
        IReadOnlyList<ResolvedEvent> events,
        int[] survivors,
        SortContext context,
        int[] expectedPhysical)
    {
        int[] production = ColumnDirectSort.SortColumnDirect(
            reader, survivors, context.OrderBy, context.IsDescending, context.GroupBy, context.IsGroupDescending);

        Assert.Equal(expectedPhysical, production);

        int[] oracle = AosReferenceOrdering.Order(
            events, context.OrderBy, context.IsDescending, context.GroupBy, context.IsGroupDescending);

        Assert.Equal(expectedPhysical, oracle);
    }

    private static ResolvedEvent BuildTiedEvent(long? recordId) =>
        new("HeavyTies", LogPathType.Channel)
        {
            RecordId = recordId,
            TimeCreated = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc),
            Id = 1000,
            Level = "Information",
            Source = "Provider.A",
            TaskCategory = "Logon",
            ComputerName = "HOST-1",
            LogName = "Channel0"
        };
}
