// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;

namespace EventLogExpert.Runtime.Tests.LogTable.OrderedView;

public sealed class OrderedViewReaderLifetimeTests
{
    [Fact]
    public void RetainedSnapshot_StaysValid_AfterReload_AndReusedIndicesDoNotAlias()
    {
        EventLogId logId = EventLogId.Create();
        var generation0 = MakeEvents("Log", firstRecordId: 1, count: 30);
        IEventColumnReader reader0 = EventColumnStore.Build(generation0, generation: 0, contentVersion: 0).CreateReader(logId);

        var context = new SortContext(ColumnName.DateAndTime, false, null, false);
        var state = new OrderedViewState();

        state.ReconcileLog(logId, reader0);

        Rebuild(state, context);
        OrderedViewSnapshot generation0Snapshot = state.Current;
        EventLocator[] generation0Order = Order(generation0Snapshot);

        Assert.Equal(30, generation0Snapshot.Count);

        var generation1 = MakeEvents("Log", firstRecordId: 500, count: 20);
        IEventColumnReader reader1 = EventColumnStore.Build(generation1, generation: 1, contentVersion: 1).CreateReader(logId);

        state.ReconcileLog(logId, reader1);

        state.Publish();
        Assert.Equal(30, state.Current.Count);

        RebuildRequest reset = state.BeginReset(logId, newGeneration: 1);
        Adopt(state, reset);
        OrderedViewSnapshot reloaded = state.Current;

        Assert.Equal(20, reloaded.Count);
        foreach (EventLocator locator in generation0Order) { Assert.False(reloaded.Contains(logId, 0, locator.Index)); }
        for (int i = 0; i < reader1.Count; i++) { Assert.True(reloaded.Contains(logId, 1, i)); }

        Assert.Equal(30, generation0Snapshot.Count);
        Assert.Equal(generation0Order, Order(generation0Snapshot));

        for (int i = 0; i < generation0Snapshot.Count; i++)
        {
            Assert.Equal(i, generation0Snapshot.RankOf(new OrderKey(generation0Order[i])));
            Assert.True(generation0Snapshot.Contains(logId, 0, generation0Order[i].Index));
        }
    }

    [Fact]
    public void RetainedSnapshot_StaysValid_AfterSameGenerationAppend()
    {
        EventLogId logId = EventLogId.Create();
        var initial = MakeEvents("Log", firstRecordId: 1, count: 40);
        var store = EventColumnStore.Build(initial, generation: 0, contentVersion: 0);
        IEventColumnReader reader0 = store.CreateReader(logId);

        var context = new SortContext(ColumnName.Source, false, null, false);
        var state = new OrderedViewState();

        state.ReconcileLog(logId, reader0);

        Rebuild(state, context);
        OrderedViewSnapshot before = state.Current;
        EventLocator[] beforeOrder = Order(before);

        Assert.Equal(40, before.Count);

        var appended = MakeEvents("Log", firstRecordId: 41, count: 25);
        IEventColumnReader reader1 = store.Append(appended).CreateReader(logId);

        Assert.Equal(0, reader1.Generation);
        Assert.Equal(65, reader1.Count);

        state.ReconcileLog(logId, reader1);

        state.Publish();
        OrderedViewSnapshot after = state.Current;

        Assert.NotEqual(before.Version, after.Version);
        Assert.Equal(40, before.Count);
        Assert.Equal(beforeOrder, Order(before));

        for (int i = 0; i < before.Count; i++)
        {
            Assert.Equal(i, before.RankOf(new OrderKey(beforeOrder[i])));
            Assert.True(before.Contains(logId, 0, beforeOrder[i].Index));
        }

        Assert.Equal(65, after.Count);
        foreach (EventLocator locator in beforeOrder) { Assert.True(after.Contains(logId, 0, locator.Index)); }
    }

    private static void Adopt(OrderedViewState state, RebuildRequest request) =>
        Assert.True(state.TryAdoptRebuild(request, OrderedViewState.BuildIndex(request)));

    private static List<ResolvedEvent> MakeEvents(string owningLog, long firstRecordId, int count)
    {
        var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        string[] sources = ["Provider.A", "Provider.B", "Provider.C"];
        var events = new List<ResolvedEvent>(count);

        for (int i = 0; i < count; i++)
        {
            events.Add(new ResolvedEvent(owningLog, LogPathType.Channel)
            {
                RecordId = firstRecordId + i,
                TimeCreated = clock.AddMilliseconds(i),
                Id = 1000 + (i % 4),
                Level = "Information",
                Source = sources[i % sources.Length],
                LogName = "Channel"
            });
        }

        return events;
    }

    private static EventLocator[] Order(OrderedViewSnapshot snapshot)
    {
        var order = new EventLocator[snapshot.Count];

        for (int i = 0; i < order.Length; i++) { order[i] = snapshot.At(i).Locator; }

        return order;
    }

    private static void Rebuild(OrderedViewState state, SortContext context)
    {
        RebuildRequest request = state.BeginRebuild(static (_, _) => true, context);
        Adopt(state, request);
    }
}
