// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using CloseLogAction = EventLogExpert.Runtime.LogTable.CloseLogAction;
using LoadEventsAction = EventLogExpert.Runtime.EventLog.LoadEventsAction;
using LoadEventsPartialAction = EventLogExpert.Runtime.EventLog.LoadEventsPartialAction;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class RawEventCountReducersTests
{
    [Fact]
    public void CountState_MirrorsStore_AcrossFullLifecycle()
    {
        var store = new RawEventStoreState();
        var count = new RawEventCountState();
        var logA = new EventLogData("LogA", LogPathType.Channel);
        var logB = new EventLogData("LogB", LogPathType.Channel);

        (store, count) = AddTable(store, count, logA);
        AssertInSync(store, count);

        (store, count) = AddTable(store, count, logB);
        AssertInSync(store, count);

        (store, count) = Ingest(store, count, RawIngestMode.Append, (logA.Id, Events(1, 3)));
        AssertInSync(store, count);

        (store, count) = Ingest(store, count, RawIngestMode.Prepend, (logA.Id, Events(50, 2)));
        AssertInSync(store, count);
        Assert.Equal(5, count.ByLog[logA.Id].Total);

        (store, count) = Ingest(store, count, RawIngestMode.Append, (logB.Id, Events(10, 4)));
        AssertInSync(store, count);

        (store, count) = Ingest(store, count, RawIngestMode.Append, (logA.Id, Events(0, 0)));
        AssertInSync(store, count);

        (store, count) = Ingest(store, count, RawIngestMode.Append, (EventLogId.Create(), Events(1, 3)));
        AssertInSync(store, count);

        (store, count) = Ingest(store, count, RawIngestMode.Replace, (logA.Id, Events(200, 1)));
        AssertInSync(store, count);
        Assert.Equal(1, count.ByLog[logA.Id].Total);

        (store, count) = CloseLog(store, count, logB.Id);
        AssertInSync(store, count);
        Assert.False(count.ByLog.ContainsKey(logB.Id));

        (store, count) = CloseAll(store, count);
        AssertInSync(store, count);
        Assert.True(count.ByLog.IsEmpty);
        Assert.Equal(0, count.Total);
    }

    [Fact]
    public void CountState_MirrorsStore_AcrossLoadLifecycle()
    {
        var store = new RawEventStoreState();
        var count = new RawEventCountState();
        var logA = new EventLogData("LogA", LogPathType.Channel);

        (store, count) = AddTable(store, count, logA);
        AssertInSync(store, count);

        (store, count) = LoadPartial(store, count, logA, Events(1, 3));
        AssertInSync(store, count);

        (store, count) = LoadPartial(store, count, logA, Events(10, 2));
        AssertInSync(store, count);
        Assert.Equal(5, count.ByLog[logA.Id].Total);

        (store, count) = Load(store, count, logA, Events(100, 1));
        AssertInSync(store, count);
        Assert.Equal(1, count.ByLog[logA.Id].Total);

        var unopened = new EventLogData("LogB", LogPathType.Channel);
        (store, count) = Load(store, count, unopened, Events(1, 4));
        AssertInSync(store, count);
        Assert.False(count.ByLog.ContainsKey(unopened.Id));

        (store, count) = CloseLog(store, count, logA.Id);
        (store, count) = LoadPartial(store, count, logA, Events(200, 2));
        AssertInSync(store, count);
        Assert.False(count.ByLog.ContainsKey(logA.Id));
    }

    [Fact]
    public void CountState_TalliesResolutionBreakdown_IngestAppendsThenReplaceResets()
    {
        // The partial/load test covers CountBatch via the load path; this locks the same breakdown on the ingest path
        // where Append/Prepend ADD their batch and Replace re-tallies only its own batch.
        var count = new RawEventCountState();
        var logData = new EventLogData("LogA", LogPathType.Channel);
        count = RawEventCountReducers.ReduceAddTable(count, new AddTableAction(logData));

        count = IngestCount(count, RawIngestMode.Append, logData.Id, Mixed(
            EventResolutionStatus.Resolved, EventResolutionStatus.NoProvider));
        count = IngestCount(count, RawIngestMode.Prepend, logData.Id, Mixed(
            EventResolutionStatus.NoMessage, EventResolutionStatus.Failed, EventResolutionStatus.Resolved));

        var afterAdds = count.ByLog[logData.Id];
        Assert.Equal(5, afterAdds.Total);
        Assert.Equal(2, afterAdds.Resolved);
        Assert.Equal(1, afterAdds.NoProvider);
        Assert.Equal(1, afterAdds.NoMessage);
        Assert.Equal(1, afterAdds.Failed);

        count = IngestCount(count, RawIngestMode.Replace, logData.Id, Mixed(EventResolutionStatus.Failed));

        var afterReplace = count.ByLog[logData.Id];
        Assert.Equal(1, afterReplace.Total);
        Assert.Equal(0, afterReplace.Resolved);
        Assert.Equal(0, afterReplace.NoProvider);
        Assert.Equal(0, afterReplace.NoMessage);
        Assert.Equal(1, afterReplace.Failed);
    }

    [Fact]
    public void CountState_TalliesResolutionBreakdown_PartialAddsThenLoadReplaces()
    {
        var count = new RawEventCountState();
        var logData = new EventLogData("LogA", LogPathType.Channel);
        count = RawEventCountReducers.ReduceAddTable(count, new AddTableAction(logData));

        count = RawEventCountReducers.ReduceLoadEventsPartial(count, new LoadEventsPartialAction(logData, Mixed(
            EventResolutionStatus.Resolved, EventResolutionStatus.Resolved, EventResolutionStatus.NoProvider)));

        var afterFirst = count.ByLog[logData.Id];
        Assert.Equal(3, afterFirst.Total);
        Assert.Equal(2, afterFirst.Resolved);
        Assert.Equal(1, afterFirst.NoProvider);

        count = RawEventCountReducers.ReduceLoadEventsPartial(count, new LoadEventsPartialAction(logData, Mixed(
            EventResolutionStatus.NoMessage, EventResolutionStatus.Failed)));

        var afterSecond = count.ByLog[logData.Id];
        Assert.Equal(5, afterSecond.Total);
        Assert.Equal(2, afterSecond.Resolved);
        Assert.Equal(1, afterSecond.NoProvider);
        Assert.Equal(1, afterSecond.NoMessage);
        Assert.Equal(1, afterSecond.Failed);

        // The terminal full load re-tallies the whole cumulative list and REPLACES the accumulated partial tallies.
        count = RawEventCountReducers.ReduceLoadEvents(count, new LoadEventsAction(logData, Mixed(
            EventResolutionStatus.Resolved)));

        var afterLoad = count.ByLog[logData.Id];
        Assert.Equal(1, afterLoad.Total);
        Assert.Equal(1, afterLoad.Resolved);
        Assert.Equal(0, afterLoad.NoProvider);
        Assert.Equal(0, afterLoad.NoMessage);
        Assert.Equal(0, afterLoad.Failed);
    }

    [Fact]
    public void ReduceAddTable_SeedsZeroCount()
    {
        var logData = new EventLogData("LogA", LogPathType.Channel);

        var count = RawEventCountReducers.ReduceAddTable(new RawEventCountState(), new AddTableAction(logData));

        Assert.Equal(0, count.ByLog[logData.Id].Total);
        Assert.Equal(0, count.Total);
    }

    [Fact]
    public void ReduceIngestRawEvents_ReplaceWithSameCountDifferentEvents_KeepsCountAndStaysInSync()
    {
        // Locks the int-vs-reference change-detection edge: the store emits a new EventColumnStore reference but the
        // count value is unchanged, so the count reducer must not drift from the store.
        var store = new RawEventStoreState();
        var count = new RawEventCountState();
        var logData = new EventLogData("LogA", LogPathType.Channel);
        (store, count) = AddTable(store, count, logData);

        (store, count) = Ingest(store, count, RawIngestMode.Replace, (logData.Id, Events(1, 5)));
        AssertInSync(store, count);
        Assert.Equal(5, count.ByLog[logData.Id].Total);

        (store, count) = Ingest(store, count, RawIngestMode.Replace, (logData.Id, Events(100, 5)));
        AssertInSync(store, count);
        Assert.Equal(5, count.ByLog[logData.Id].Total);
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
            Assert.Equal(list.Count, count.ByLog[id].Total);
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

    private static RawEventCountState IngestCount(
        RawEventCountState count,
        RawIngestMode mode,
        EventLogId id,
        IReadOnlyList<ResolvedEvent> events) =>
        RawEventCountReducers.ReduceIngestRawEvents(
            count,
            new IngestRawEventsAction(
                new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>> { [id] = events },
                mode));

    private static (RawEventStoreState, RawEventCountState) Load(
        RawEventStoreState store,
        RawEventCountState count,
        EventLogData log,
        IReadOnlyList<ResolvedEvent> events)
    {
        var action = new LoadEventsAction(log, events);

        return (RawEventStoreReducers.ReduceLoadEvents(store, action),
            RawEventCountReducers.ReduceLoadEvents(count, action));
    }

    private static (RawEventStoreState, RawEventCountState) LoadPartial(
        RawEventStoreState store,
        RawEventCountState count,
        EventLogData log,
        IReadOnlyList<ResolvedEvent> events)
    {
        var action = new LoadEventsPartialAction(log, events);

        return (RawEventStoreReducers.ReduceLoadEventsPartial(store, action),
            RawEventCountReducers.ReduceLoadEventsPartial(count, action));
    }

    private static IReadOnlyList<ResolvedEvent> Mixed(params EventResolutionStatus[] statuses) =>
        [.. statuses.Select((status, index) =>
            new ResolvedEvent("LogA", LogPathType.Channel) { Id = index + 1, ResolutionStatus = status })];
}
