// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using CloseLogAction = EventLogExpert.Runtime.LogTable.CloseLogAction;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class RawEventCountReducersTests
{
    [Fact]
    public void CountState_MirrorsStore_AcrossFullLifecycle()
    {
        var store = new RawEventStoreState();
        var count = new RawEventCountState();
        var logA = new EventLogData("LogA", LogPathType.Channel, []);
        var logB = new EventLogData("LogB", LogPathType.Channel, []);

        (store, count) = AddTable(store, count, logA);
        AssertInSync(store, count);

        (store, count) = AddTable(store, count, logB);
        AssertInSync(store, count);

        (store, count) = Ingest(store, count, RawIngestMode.Append, (logA.Id, Events(1, 3)));
        AssertInSync(store, count);

        (store, count) = Ingest(store, count, RawIngestMode.Prepend, (logA.Id, Events(50, 2)));
        AssertInSync(store, count);
        Assert.Equal(5, count.ByLog[logA.Id]);

        (store, count) = Ingest(store, count, RawIngestMode.Append, (logB.Id, Events(10, 4)));
        AssertInSync(store, count);

        (store, count) = Ingest(store, count, RawIngestMode.Append, (logA.Id, Events(0, 0)));
        AssertInSync(store, count);

        (store, count) = Ingest(store, count, RawIngestMode.Append, (EventLogId.Create(), Events(1, 3)));
        AssertInSync(store, count);

        (store, count) = Ingest(store, count, RawIngestMode.Replace, (logA.Id, Events(200, 1)));
        AssertInSync(store, count);
        Assert.Equal(1, count.ByLog[logA.Id]);

        (store, count) = CloseLog(store, count, logB.Id);
        AssertInSync(store, count);
        Assert.False(count.ByLog.ContainsKey(logB.Id));

        (store, count) = CloseAll(store, count);
        AssertInSync(store, count);
        Assert.True(count.ByLog.IsEmpty);
        Assert.Equal(0, count.Total);
    }

    [Fact]
    public void ReduceAddTable_SeedsZeroCount()
    {
        var logData = new EventLogData("LogA", LogPathType.Channel, []);

        var count = RawEventCountReducers.ReduceAddTable(new RawEventCountState(), new AddTableAction(logData));

        Assert.Equal(0, count.ByLog[logData.Id]);
        Assert.Equal(0, count.Total);
    }

    [Fact]
    public void ReduceIngestRawEvents_ReplaceWithSameCountDifferentEvents_KeepsCountAndStaysInSync()
    {
        // Locks the int-vs-reference change-detection edge: the store emits a new RawEventList reference but the
        // count value is unchanged, so the count reducer must not drift from the store.
        var store = new RawEventStoreState();
        var count = new RawEventCountState();
        var logData = new EventLogData("LogA", LogPathType.Channel, []);
        (store, count) = AddTable(store, count, logData);

        (store, count) = Ingest(store, count, RawIngestMode.Replace, (logData.Id, Events(1, 5)));
        AssertInSync(store, count);
        Assert.Equal(5, count.ByLog[logData.Id]);

        (store, count) = Ingest(store, count, RawIngestMode.Replace, (logData.Id, Events(100, 5)));
        AssertInSync(store, count);
        Assert.Equal(5, count.ByLog[logData.Id]);
    }

    private static (RawEventStoreState, RawEventCountState) AddTable(
        RawEventStoreState store,
        RawEventCountState count,
        EventLogData log) =>
        (RawEventStoreReducers.ReduceAddTable(store, new AddTableAction(log)),
            RawEventCountReducers.ReduceAddTable(count, new AddTableAction(log)));

    private static void AssertInSync(RawEventStoreState store, RawEventCountState count)
    {
        Assert.Equal(store.ByLog.Count, count.ByLog.Count);

        foreach (var (id, list) in store.ByLog)
        {
            Assert.True(count.ByLog.ContainsKey(id), $"count missing id {id.Value}");
            Assert.Equal(list.Count, count.ByLog[id]);
        }

        Assert.Equal(store.ByLog.Values.Sum(list => list.Count), count.Total);
    }

    private static (RawEventStoreState, RawEventCountState) CloseAll(
        RawEventStoreState store,
        RawEventCountState count) =>
        (RawEventStoreReducers.ReduceCloseAll(store),
            RawEventCountReducers.ReduceCloseAll(count));

    private static (RawEventStoreState, RawEventCountState) CloseLog(
        RawEventStoreState store,
        RawEventCountState count,
        EventLogId id) =>
        (RawEventStoreReducers.ReduceCloseLog(store, new CloseLogAction(id)),
            RawEventCountReducers.ReduceCloseLog(count, new CloseLogAction(id)));

    private static IReadOnlyList<ResolvedEvent> Events(int firstId, int count) =>
        [.. Enumerable.Range(firstId, count).Select(id => new ResolvedEvent("LogA", LogPathType.Channel) { Id = id })];

    private static (RawEventStoreState, RawEventCountState) Ingest(
        RawEventStoreState store,
        RawEventCountState count,
        RawIngestMode mode,
        params (EventLogId Id, IReadOnlyList<ResolvedEvent> Events)[] perLog)
    {
        var action = new IngestRawEventsAction(
            perLog.ToDictionary(entry => entry.Id, entry => entry.Events),
            mode);

        return (RawEventStoreReducers.ReduceIngestRawEvents(store, action),
            RawEventCountReducers.ReduceIngestRawEvents(count, action));
    }
}
