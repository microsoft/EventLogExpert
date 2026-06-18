// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Tests.TestUtils.Constants;
using LogTableReducers = EventLogExpert.Runtime.LogTable.Reducers;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class PerLogStoreViewTests
{
    [Fact]
    public void Assemble_ForAnUnknownLog_ReturnsNull()
    {
        Assert.Null(PerLogStoreView.Assemble(new LogTableState(), new RawEventStoreState(), EventLogId.Create()));
    }

    [Fact]
    public void Assemble_JoinsMetadataRawEventsAndDisplayList()
    {
        var logData = new EventLogData(Constants.LogNameLog1, LogPathType.Channel);
        var displayed = new ResolvedEvent(Constants.LogNameLog1, LogPathType.Channel) { Id = 1, RecordId = 10 };

        var logTable = new LogTableState();
        logTable = LogTableReducers.ReduceAddTable(logTable, new AddTableAction(logData));
        logTable = LogTableReducers.ReduceUpdateTable(logTable, new UpdateTableAction(logData.Id, [displayed]));

        var raw = RawEventStoreReducers.ReduceAddTable(new RawEventStoreState(), new AddTableAction(logData));

        var store = PerLogStoreView.Assemble(logTable, raw, logData.Id);

        Assert.NotNull(store);
        Assert.Equal(logData.Id, store!.Id);
        Assert.Equal(Constants.LogNameLog1, store.Name);
        Assert.Equal(LogPathType.Channel, store.Type);
        // DisplayList is the live per-log filtered list (same reference EventsForLog exposes).
        Assert.Same(logTable.EventsForLog(logData.Id), store.DisplayList);
    }

    [Fact]
    public void Assemble_RawEventsContainFilterHiddenEventsAbsentFromDisplayList()
    {
        var logData = new EventLogData(Constants.LogNameLog1, LogPathType.Channel);
        var shown = new ResolvedEvent(Constants.LogNameLog1, LogPathType.Channel) { Id = 1, RecordId = 10 };
        var hidden = new ResolvedEvent(Constants.LogNameLog1, LogPathType.Channel) { Id = 2, RecordId = 20 };

        // Display gets only the filtered event; the raw store gets both (the filter-hidden one too).
        var logTable = new LogTableState();
        logTable = LogTableReducers.ReduceAddTable(logTable, new AddTableAction(logData));
        logTable = LogTableReducers.ReduceUpdateTable(logTable, new UpdateTableAction(logData.Id, [shown]));

        var raw = RawEventStoreReducers.ReduceAddTable(new RawEventStoreState(), new AddTableAction(logData));
        raw = RawEventStoreReducers.ReduceIngestRawEvents(
            raw,
            new IngestRawEventsAction(
                new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>> { [logData.Id] = new[] { shown, hidden } },
                RawIngestMode.Replace));

        var store = PerLogStoreView.Assemble(logTable, raw, logData.Id);

        Assert.NotNull(store);
        Assert.Equal([1, 2], store!.RawEvents.Select(e => e.Id));
        Assert.Equal([1], store.DisplayList.Select(e => e.Id));
    }

    [Fact]
    public void Assemble_WhenNoFilteredRowsYet_FallsBackToAnEmptyDisplayList()
    {
        var logData = new EventLogData(Constants.LogNameLog1, LogPathType.Channel);

        var logTable = new LogTableState();
        logTable = LogTableReducers.ReduceAddTable(logTable, new AddTableAction(logData));

        var raw = RawEventStoreReducers.ReduceAddTable(new RawEventStoreState(), new AddTableAction(logData));
        raw = RawEventStoreReducers.ReduceIngestRawEvents(
            raw,
            new IngestRawEventsAction(
                new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>
                {
                    [logData.Id] = new[] { new ResolvedEvent(Constants.LogNameLog1, LogPathType.Channel) { Id = 1 } }
                },
                RawIngestMode.Replace));

        var store = PerLogStoreView.Assemble(logTable, raw, logData.Id);

        Assert.NotNull(store);
        Assert.Single(store!.RawEvents);
        Assert.Empty(store.DisplayList);
    }

    [Fact]
    public void AssembleAll_SkipsTheCombinedTab()
    {
        var log1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel);
        var log2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel);

        var logTable = new LogTableState();
        logTable = LogTableReducers.ReduceAddTable(logTable, new AddTableAction(log1));
        logTable = LogTableReducers.ReduceAddTable(logTable, new AddTableAction(log2));

        var raw = new RawEventStoreState();
        raw = RawEventStoreReducers.ReduceAddTable(raw, new AddTableAction(log1));
        raw = RawEventStoreReducers.ReduceAddTable(raw, new AddTableAction(log2));

        // A combined tab exists with two logs, but AssembleAll yields only the two real per-log stores.
        Assert.Contains(logTable.EventTables, t => t.IsCombined);

        var ids = PerLogStoreView.AssembleAll(logTable, raw).Select(s => s.Id).ToList();

        Assert.Equal(2, ids.Count);
        Assert.Contains(log1.Id, ids);
        Assert.Contains(log2.Id, ids);
    }
}
