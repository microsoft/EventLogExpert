// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using System.Collections.Immutable;
using CloseLogAction = EventLogExpert.Runtime.LogTable.CloseLogAction;
using EventLogReducers = EventLogExpert.Runtime.EventLog.Reducers;
using EventLogState = EventLogExpert.Runtime.EventLog.EventLogState;
using LoadEventsAction = EventLogExpert.Runtime.EventLog.LoadEventsAction;
using LoadEventsPartialAction = EventLogExpert.Runtime.EventLog.LoadEventsPartialAction;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class RawEventStoreReducersTests
{
    [Fact]
    public void ReduceAddTable_SeedsAnEmptyRawEntryForTheRealLog()
    {
        var logData = new EventLogData("LogA", LogPathType.Channel);

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

    [Fact]
    public void ReduceLoadEvents_File_StoreAndEventLogReducers_AcceptAndRejectInLockstep()
    {
        var openLog = new EventLogData("LogA", LogPathType.File);

        var eventLogState = new EventLogState
        {
            ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(openLog.Name, openLog)
        };

        var rawState = RawEventStoreReducers.ReduceAddTable(new RawEventStoreState(), new AddTableAction(openLog));

        var loadForOpenLog = new LoadEventsAction(openLog, [EvNamed(1, "Security"), EvNamed(2, "Setup")]);

        Assert.NotSame(eventLogState, EventLogReducers.ReduceLoadEvents(eventLogState, loadForOpenLog));
        Assert.NotSame(rawState, RawEventStoreReducers.ReduceLoadEvents(rawState, loadForOpenLog));

        var staleReopenedLog = new EventLogData("LogA", LogPathType.File);
        var loadForStaleId = new LoadEventsAction(staleReopenedLog, [EvNamed(3, "Security")]);

        Assert.Same(eventLogState, EventLogReducers.ReduceLoadEvents(eventLogState, loadForStaleId));
        Assert.Same(rawState, RawEventStoreReducers.ReduceLoadEvents(rawState, loadForStaleId));
    }

    [Fact]
    public void ReduceLoadEvents_ForAnOpenLog_ReplacesWithExactlyThoseEvents()
    {
        var (state, log) = OpenedLog();
        state = Ingest(state, log.Id, RawIngestMode.Append, Ev(1), Ev(2));

        state = RawEventStoreReducers.ReduceLoadEvents(state, new LoadEventsAction(log, [Ev(9)]));

        Assert.Equal([9], state.ByLog[log.Id].Select(e => e.Id));
    }

    [Fact]
    public void ReduceLoadEvents_ForAnUnopenedLog_IsSkipped()
    {
        var log = new EventLogData("LogA", LogPathType.Channel);

        var state = RawEventStoreReducers.ReduceLoadEvents(
            new RawEventStoreState(),
            new LoadEventsAction(log, [Ev(1)]));

        Assert.False(state.ByLog.ContainsKey(log.Id));
    }

    [Fact]
    public void ReduceLoadEvents_WhenEmptyOnAFreshLog_IsANoOp()
    {
        var (state, log) = OpenedLog();

        var result = RawEventStoreReducers.ReduceLoadEvents(state, new LoadEventsAction(log, []));

        Assert.Same(state, result);
    }

    [Fact]
    public void ReduceLoadEventsPartial_ForAnOpenLog_AppendsAfterExistingEvents()
    {
        var (state, log) = OpenedLog();
        state = Ingest(state, log.Id, RawIngestMode.Append, Ev(1), Ev(2));

        state = RawEventStoreReducers.ReduceLoadEventsPartial(state, new LoadEventsPartialAction(log, [Ev(3)]));

        Assert.Equal([1, 2, 3], state.ByLog[log.Id].Select(e => e.Id));
    }

    [Fact]
    public void ReduceLoadEventsPartial_ForAnUnopenedLog_IsSkipped()
    {
        var log = new EventLogData("LogA", LogPathType.Channel);

        var state = RawEventStoreReducers.ReduceLoadEventsPartial(
            new RawEventStoreState(),
            new LoadEventsPartialAction(log, [Ev(1)]));

        Assert.False(state.ByLog.ContainsKey(log.Id));
    }

    private static ResolvedEvent Ev(int id) => new("LogA", LogPathType.Channel) { Id = id };

    private static ResolvedEvent EvNamed(int id, string logName) =>
        new("LogA", LogPathType.File) { Id = id, LogName = logName };

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
        var (state, log) = OpenedLog();

        return (state, log.Id);
    }

    private static (RawEventStoreState State, EventLogData Log) OpenedLog()
    {
        var logData = new EventLogData("LogA", LogPathType.Channel);
        var state = RawEventStoreReducers.ReduceAddTable(new RawEventStoreState(), new AddTableAction(logData));

        return (state, logData);
    }
}
