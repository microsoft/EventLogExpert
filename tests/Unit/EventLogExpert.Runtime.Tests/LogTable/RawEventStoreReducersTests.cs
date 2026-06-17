// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using CloseLogAction = EventLogExpert.Runtime.LogTable.CloseLogAction;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class RawEventStoreReducersTests
{
    [Fact]
    public void ReduceAddTable_SeedsAnEmptyRawEntryForTheRealLog()
    {
        var logData = new EventLogData("LogA", LogPathType.Channel, []);

        var state = RawEventStoreReducers.ReduceAddTable(new RawEventStoreState(), new AddTableAction(logData));

        Assert.True(state.ByLog.ContainsKey(logData.Id));
        Assert.Empty(state.ByLog[logData.Id]);
    }

    [Fact]
    public void ReduceCloseAll_ClearsEveryRawEntry()
    {
        var (state, id) = Opened();
        state = Ingest(state, id, RawIngestMode.Append, Ev(1));

        state = RawEventStoreReducers.ReduceCloseAll(state);

        Assert.True(state.ByLog.IsEmpty);
    }

    [Fact]
    public void ReduceCloseLog_RemovesTheRawEntry()
    {
        var (state, id) = Opened();
        state = Ingest(state, id, RawIngestMode.Append, Ev(1));

        state = RawEventStoreReducers.ReduceCloseLog(state, new CloseLogAction(id));

        Assert.False(state.ByLog.ContainsKey(id));
    }

    [Fact]
    public void ReduceIngestRawEvents_Append_AccumulatesAtTheEnd()
    {
        var (state, id) = Opened();

        state = Ingest(state, id, RawIngestMode.Append, Ev(1), Ev(2));
        state = Ingest(state, id, RawIngestMode.Append, Ev(3));

        Assert.Equal([1, 2, 3], state.ByLog[id].Select(e => e.Id));
    }

    [Fact]
    public void ReduceIngestRawEvents_ForAnUnopenedLog_IsSkipped()
    {
        var state = new RawEventStoreState();
        var unknown = EventLogId.Create();

        state = Ingest(state, unknown, RawIngestMode.Append, Ev(1));

        Assert.False(state.ByLog.ContainsKey(unknown));
    }

    [Fact]
    public void ReduceIngestRawEvents_Prepend_PutsNewestFirst()
    {
        var (state, id) = Opened();

        state = Ingest(state, id, RawIngestMode.Append, Ev(1), Ev(2));
        state = Ingest(state, id, RawIngestMode.Prepend, Ev(3));

        // Live-tail order: newest prepended ahead of the earlier events (matches ActiveLogs.Events).
        Assert.Equal([3, 1, 2], state.ByLog[id].Select(e => e.Id));
    }

    [Fact]
    public void ReduceIngestRawEvents_Replace_SetsTheLogToExactlyThoseEvents()
    {
        var (state, id) = Opened();
        state = Ingest(state, id, RawIngestMode.Append, Ev(1), Ev(2));

        state = Ingest(state, id, RawIngestMode.Replace, Ev(9));

        Assert.Equal([9], state.ByLog[id].Select(e => e.Id));
    }

    private static ResolvedEvent Ev(int id) => new("LogA", LogPathType.Channel) { Id = id };

    private static RawEventStoreState Ingest(
        RawEventStoreState state,
        EventLogId id,
        RawIngestMode mode,
        params ResolvedEvent[] events) =>
        RawEventStoreReducers.ReduceIngestRawEvents(
            state,
            new IngestRawEventsAction(
                new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>> { [id] = events },
                mode));

    private static (RawEventStoreState State, EventLogId Id) Opened()
    {
        var logData = new EventLogData("LogA", LogPathType.Channel, []);
        var state = RawEventStoreReducers.ReduceAddTable(new RawEventStoreState(), new AddTableAction(logData));

        return (state, logData.Id);
    }
}
