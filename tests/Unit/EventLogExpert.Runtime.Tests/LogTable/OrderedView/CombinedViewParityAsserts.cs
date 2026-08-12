// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using EventLogExpert.Runtime.Tests.LogTable.TestSupport;

namespace EventLogExpert.Runtime.Tests.LogTable.OrderedView;

internal static class CombinedViewParityAsserts
{
    public static void AssertGroupRunsMatch(IEventColumnView expected, IEventColumnView actual, ColumnName groupBy)
    {
        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(GroupRuns(expected, groupBy), GroupRuns(actual, groupBy));
    }

    public static void AssertOrderMatchesReference(
        IEventColumnView facade,
        IReadOnlyList<(EventLogId LogId, IReadOnlyList<ResolvedEvent> Events)> perLog,
        SortContext context)
    {
        ArgumentNullException.ThrowIfNull(facade);
        ArgumentNullException.ThrowIfNull(perLog);

        var flattened = new List<(EventLogId LogId, int Index, ResolvedEvent Event)>();

        foreach ((EventLogId logId, IReadOnlyList<ResolvedEvent> events) in perLog)
        {
            for (int index = 0; index < events.Count; index++) { flattened.Add((logId, index, events[index])); }
        }

        int[] expected = AosReferenceOrdering.Order(
            [.. flattened.Select(entry => entry.Event)],
            context.OrderBy,
            context.IsDescending,
            context.GroupBy,
            context.IsGroupDescending);

        Assert.Equal(expected.Length, facade.Count);

        for (int position = 0; position < expected.Length; position++)
        {
            (EventLogId logId, int index, _) = flattened[expected[position]];
            EventLocator actual = facade.LocatorAt(position);

            Assert.Equal(logId, actual.LogId);
            Assert.Equal(index, actual.Index);
        }
    }

    public static void AssertSnapshotOrderMatchesReference(
        OrderedViewSnapshot snapshot, EventLogId logId, IReadOnlyList<ResolvedEvent> events, SortContext context)
    {
        int[] expected = AosReferenceOrdering.Order(
            events, context.OrderBy, context.IsDescending, context.GroupBy, context.IsGroupDescending);

        Assert.Equal(expected.Length, snapshot.Count);

        for (int position = 0; position < expected.Length; position++)
        {
            EventLocator locator = snapshot.At(position).Locator;

            Assert.Equal(logId, locator.LogId);
            Assert.Equal(expected[position], locator.Index);
            Assert.Equal(position, snapshot.RankOf(new OrderKey(locator)));
            Assert.True(snapshot.Contains(locator.LogId, locator.Generation, locator.Index));
        }
    }

    public static List<(string Key, int Count)> GroupRuns(IEventColumnView view, ColumnName groupBy)
    {
        var runs = new List<(string Key, int Count)>();

        for (int index = 0; index < view.Count; index++)
        {
            string key = view.GroupKeyAt(view.LocatorAt(index), groupBy);

            if (runs.Count > 0 && string.Equals(runs[^1].Key, key, StringComparison.Ordinal))
            {
                runs[^1] = (key, runs[^1].Count + 1);
            }
            else
            {
                runs.Add((key, 1));
            }
        }

        return runs;
    }
}
