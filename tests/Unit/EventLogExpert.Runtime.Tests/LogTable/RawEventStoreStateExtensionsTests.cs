// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class RawEventStoreStateExtensionsTests
{
    private static readonly DateTime s_mid = new(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime s_newest = new(2024, 12, 31, 23, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime s_oldest = new(2024, 1, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TryGetRawEventDateRange_AcrossChunksAfterPrepend_ReadsTrueEndpoints()
    {
        var (state, id) = Opened("LogA");

        state = Ingest(state, id, RawIngestMode.Append, Ev(1, s_mid), Ev(2, s_oldest));
        state = Ingest(state, id, RawIngestMode.Prepend, Ev(3, s_newest));

        Assert.Equal((s_oldest, s_newest), state.TryGetRawEventDateRange());
    }

    [Fact]
    public void TryGetRawEventDateRange_EmptyStore_ReturnsNull() =>
        Assert.Null(new RawEventStoreState().TryGetRawEventDateRange());

    [Fact]
    public void TryGetRawEventDateRange_MultipleLogs_ReturnsCrossLogMinMax()
    {
        var (state, idA) = Opened("LogA");
        (state, var idB) = OpenedOn(state, "LogB");

        state = Ingest(state, idA, RawIngestMode.Append, Ev(1, s_mid), Ev(2, s_oldest));
        state = Ingest(state, idB, RawIngestMode.Append, Ev(3, s_newest), Ev(4, s_mid));

        Assert.Equal((s_oldest, s_newest), state.TryGetRawEventDateRange());
    }

    [Fact]
    public void TryGetRawEventDateRange_OpenLogWithNoEvents_ReturnsNull()
    {
        var (state, _) = Opened("LogA");

        Assert.Null(state.TryGetRawEventDateRange());
    }

    [Fact]
    public void TryGetRawEventDateRange_SingleLog_ReturnsOldestAndNewest()
    {
        var (state, id) = Opened("LogA");

        // Newest-first order (mirrors ActiveLogs.Events sorted RecordId descending).
        state = Ingest(state, id, RawIngestMode.Append, Ev(1, s_newest), Ev(2, s_oldest));

        Assert.Equal((s_oldest, s_newest), state.TryGetRawEventDateRange());
    }

    private static ResolvedEvent Ev(int id, DateTime timeCreated) =>
        new("LogA", LogPathType.Channel) { Id = id, TimeCreated = timeCreated };

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

    private static (RawEventStoreState State, EventLogId Id) Opened(string name) =>
        OpenedOn(new RawEventStoreState(), name);

    private static (RawEventStoreState State, EventLogId Id) OpenedOn(RawEventStoreState state, string name)
    {
        var logData = new EventLogData(name, LogPathType.Channel);

        return (RawEventStoreReducers.ReduceAddTable(state, new AddTableAction(logData)), logData.Id);
    }
}
