// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.UI.LogTable;
using EventLogExpert.UI.Tests.TestUtils;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using System.Collections.Immutable;
using CloseLogAction = EventLogExpert.UI.LogTable.CloseLogAction;
using Reducers = EventLogExpert.UI.LogTable.Reducers;

namespace EventLogExpert.UI.Tests.LogTable;

public sealed class LogTableStoreTests
{
    private static readonly ColumnDefaults s_columnDefaults = new();

    [Fact]
    public void EventTableAction_AddTable_ShouldStoreLogData()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);

        // Act
        var action = new AddTableAction(logData);

        // Assert
        Assert.Equal(logData, action.LogData);
    }

    [Fact]
    public void EventTableAction_CloseLog_ShouldStoreLogId()
    {
        // Arrange
        var logId = EventLogId.Create();

        // Act
        var action = new CloseLogAction(logId);

        // Assert
        Assert.Equal(logId, action.LogId);
    }

    [Fact]
    public void EventTableAction_LoadColumnsCompleted_ShouldStoreColumns()
    {
        // Arrange
        var columns = new Dictionary<ColumnName, bool>
        {
            { ColumnName.Level, true },
            { ColumnName.DateAndTime, true }
        };

        var widths = new Dictionary<ColumnName, int>
        {
            { ColumnName.Level, 100 },
            { ColumnName.DateAndTime, 160 }
        };

        var order = s_columnDefaults.ColumnOrder;

        // Act
        var action = new LoadColumnsCompletedAction(columns.ToImmutableDictionary(), widths.ToImmutableDictionary(), order);

        // Assert
        Assert.Equal(2, action.LoadedColumns.Count);
        Assert.True(action.LoadedColumns[ColumnName.Level]);
        Assert.True(action.LoadedColumns[ColumnName.DateAndTime]);
    }

    [Fact]
    public void EventTableAction_SetActiveTable_ShouldStoreLogId()
    {
        // Arrange
        var logId = EventLogId.Create();

        // Act
        var action = new SetActiveTableAction(logId);

        // Assert
        Assert.Equal(logId, action.LogId);
    }

    [Fact]
    public void EventTableAction_SetOrderBy_ShouldStoreColumnName()
    {
        // Act
        var action = new SetOrderByAction(ColumnName.Level);

        // Assert
        Assert.Equal(ColumnName.Level, action.OrderBy);
    }

    [Fact]
    public void EventTableAction_SetOrderBy_WithNull_ShouldStoreNull()
    {
        // Act
        var action = new SetOrderByAction(null);

        // Assert
        Assert.Null(action.OrderBy);
    }

    [Fact]
    public void EventTableAction_ToggleColumn_ShouldStoreColumnName()
    {
        // Act
        var action = new ToggleColumnAction(ColumnName.Source);

        // Assert
        Assert.Equal(ColumnName.Source, action.ColumnName);
    }

    [Fact]
    public void EventTableAction_ToggleLoading_ShouldStoreLogId()
    {
        // Arrange
        var logId = EventLogId.Create();

        // Act
        var action = new ToggleLoadingAction(logId);

        // Assert
        Assert.Equal(logId, action.LogId);
    }

    [Fact]
    public void EventTableAction_UpdateDisplayedEvents_ShouldStoreActiveLogs()
    {
        // Arrange
        var logId = EventLogId.Create();
        var events = new List<ResolvedEvent> { EventUtils.CreateTestEvent(100) };

        var activeLogs = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>
        {
            { logId, events }
        };

        // Act
        var action = new UpdateDisplayedEventsAction(activeLogs);

        // Assert
        Assert.Single(action.ActiveLogs);
        Assert.True(action.ActiveLogs.ContainsKey(logId));
    }

    [Fact]
    public void EventTableAction_UpdateTable_ShouldStoreLogIdAndEvents()
    {
        // Arrange
        var logId = EventLogId.Create();

        var events = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(100),
            EventUtils.CreateTestEvent(200)
        };

        // Act
        var action = new UpdateTableAction(logId, events);

        // Assert
        Assert.Equal(logId, action.LogId);
        Assert.Equal(2, action.Events.Count);
    }

    [Fact]
    public void IntegrationTest_ColumnManagement()
    {
        // Arrange
        var state = new LogTableState();

        // Act - Load columns
        var columns = new Dictionary<ColumnName, bool>
        {
            { ColumnName.Level, true },
            { ColumnName.DateAndTime, true },
            { ColumnName.Source, false }
        };

        state = Reducers.ReduceLoadColumnsCompleted(
            state,
            new LoadColumnsCompletedAction(columns.ToImmutableDictionary(), ImmutableDictionary<ColumnName, int>.Empty, s_columnDefaults.ColumnOrder));

        // Assert
        Assert.Equal(3, state.Columns.Count);
        Assert.True(state.Columns[ColumnName.Level]);
        Assert.False(state.Columns[ColumnName.Source]);
    }

    [Fact]
    public void IntegrationTest_LoadAndUpdateTableEvents()
    {
        // Arrange
        var state = new LogTableState();
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);

        // Act - Add table
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));
        Assert.True(state.EventTables.First().IsLoading);

        // Act - Update table with events
        var events = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(100),
            EventUtils.CreateTestEvent(200)
        };

        state = Reducers.ReduceUpdateTable(state, new UpdateTableAction(logData.Id, events));

        // Assert
        Assert.False(state.EventTables.First().IsLoading);
        Assert.Equal(2, state.DisplayedEvents.Count);
    }

    [Fact]
    public void IntegrationTest_OpenMultipleLogsAndCloseOne()
    {
        // Arrange
        var state = new LogTableState();
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel, []);
        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel, []);
        var logData3 = new EventLogData(Constants.LogNameLog3, LogPathType.Channel, []);

        // Act - Open three logs
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData1));
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData2));
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData3));

        // Assert - Should have 4 tables (3 logs + 1 combined)
        Assert.Equal(4, state.EventTables.Count);
        Assert.Single(state.EventTables, t => t.IsCombined);

        // Act - Close one log
        state = Reducers.ReduceCloseLog(state, new CloseLogAction(logData2.Id));

        // Assert - Should have 3 tables (2 logs + 1 combined)
        Assert.Equal(3, state.EventTables.Count);
        Assert.Single(state.EventTables, t => t.IsCombined);
        Assert.DoesNotContain(state.EventTables, t => t.Id == logData2.Id);
    }

    [Fact]
    public void LogTableState_DefaultState_ShouldHaveCorrectDefaults()
    {
        // Arrange & Act
        var state = new LogTableState();

        // Assert
        Assert.Empty(state.EventTables);
        Assert.Null(state.ActiveEventLogId);
        Assert.Empty(state.Columns);
        Assert.Empty(state.ColumnWidths);
        Assert.Empty(state.ColumnOrder);
        Assert.Null(state.OrderBy);
        Assert.True(state.IsDescending);
    }

    [Fact]
    public void LogView_ComputerName_AfterFirstEventArrives_ShouldBeStoredOnTable()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));

        var firstBatch = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 10, RecordId = 1, ComputerName = Constants.EventComputerServer01 }
        };

        state = Reducers.ReduceAppendTableEvents(
            state,
            new AppendTableEventsAction(logData.Id, firstBatch));

        var secondBatch = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 11, RecordId = 2, ComputerName = Constants.EventComputerServer02 }
        };

        // Act — second batch with a different ComputerName must not overwrite the latched value
        state = Reducers.ReduceAppendTableEvents(
            state,
            new AppendTableEventsAction(logData.Id, secondBatch));

        // Assert — ComputerName latches to the first non-empty observed value
        var table = state.EventTables.First(t => t.Id == logData.Id);
        Assert.Equal(Constants.EventComputerServer01, table.ComputerName);
    }

    [Fact]
    public void LogView_ComputerName_WhenFirstEventHasEmptyComputerName_ShouldUseLaterEventInBatch()
    {
        // Arrange — first event has an empty ComputerName (resolver miss); second carries the name
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));

        var batch = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 10, RecordId = 1, ComputerName = string.Empty },
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 11, RecordId = 2, ComputerName = Constants.EventComputerServer01 }
        };

        // Act
        state = Reducers.ReduceAppendTableEvents(
            state,
            new AppendTableEventsAction(logData.Id, batch));

        // Assert — reducer scans past the empty leading event and latches the first non-empty value
        var table = state.EventTables.First(t => t.Id == logData.Id);
        Assert.Equal(Constants.EventComputerServer01, table.ComputerName);
    }

    [Fact]
    public void LogView_ComputerName_WhenNoEvents_ShouldReturnEmpty()
    {
        // Arrange
        var model = new LogView(EventLogId.Create());

        // Act
        var computerName = model.ComputerName;

        // Assert
        Assert.Equal(string.Empty, computerName);
    }

    [Fact]
    public void LogView_ShouldHaveUniqueId()
    {
        // Arrange & Act
        var model1 = new LogView(EventLogId.Create());
        var model2 = new LogView(EventLogId.Create());

        // Assert
        Assert.NotEqual(model1.Id, model2.Id);
    }

    [Fact]
    public void ReduceAddTable_WhenCombinedExists_ShouldNotCreateAnotherCombined()
    {
        // Arrange
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel, []);
        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel, []);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData1));
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData2));

        var logData3 = new EventLogData(Constants.LogNameLog3, LogPathType.Channel, []);
        var action = new AddTableAction(logData3);

        // Act
        var newState = Reducers.ReduceAddTable(state, action);

        // Assert
        Assert.Equal(4, newState.EventTables.Count);
        Assert.Single(newState.EventTables, t => t.IsCombined);
    }

    [Fact]
    public void ReduceAddTable_WhenFirstTable_ShouldBeLoading()
    {
        // Arrange
        var state = new LogTableState();
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var action = new AddTableAction(logData);

        // Act
        var newState = Reducers.ReduceAddTable(state, action);

        // Assert
        Assert.True(newState.EventTables.First().IsLoading);
    }

    [Fact]
    public void ReduceAddTable_WhenFirstTable_ShouldSetAsActive()
    {
        // Arrange
        var state = new LogTableState();
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var action = new AddTableAction(logData);

        // Act
        var newState = Reducers.ReduceAddTable(state, action);

        // Assert
        Assert.Single(newState.EventTables);
        Assert.NotNull(newState.ActiveEventLogId);
        Assert.Equal(logData.Id, newState.EventTables.First().Id);
        Assert.Equal(logData.Id, newState.ActiveEventLogId);
    }

    [Fact]
    public void ReduceAddTable_WhenFirstTable_WithFilePath_ShouldSetFileName()
    {
        // Arrange
        var state = new LogTableState();
        var logData = new EventLogData(Constants.FilePathTestEvtx, LogPathType.File, []);
        var action = new AddTableAction(logData);

        // Act
        var newState = Reducers.ReduceAddTable(state, action);

        // Assert
        Assert.Equal(Constants.FilePathTestEvtx, newState.EventTables.First().FileName);
        Assert.Equal(LogPathType.File, newState.EventTables.First().LogPathType);
    }

    [Fact]
    public void ReduceAddTable_WhenFirstTable_WithLogName_ShouldNotSetFileName()
    {
        // Arrange
        var state = new LogTableState();
        var logData = new EventLogData(Constants.LogNameApplication, LogPathType.Channel, []);
        var action = new AddTableAction(logData);

        // Act
        var newState = Reducers.ReduceAddTable(state, action);

        // Assert
        Assert.Null(newState.EventTables.First().FileName);
        Assert.Equal(Constants.LogNameApplication, newState.EventTables.First().LogName);
        Assert.Equal(LogPathType.Channel, newState.EventTables.First().LogPathType);
    }

    [Fact]
    public void ReduceAddTable_WhenSecondTable_ShouldCreateCombinedTable()
    {
        // Arrange
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel, []);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData1));

        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel, []);
        var action = new AddTableAction(logData2);

        // Act
        var newState = Reducers.ReduceAddTable(state, action);

        // Assert
        Assert.Equal(3, newState.EventTables.Count);
        Assert.Contains(newState.EventTables, t => t.IsCombined);
    }

    [Fact]
    public void ReduceAddTable_WhenSecondTable_ShouldSetCombinedAsActive()
    {
        // Arrange
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel, []);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData1));

        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel, []);
        var action = new AddTableAction(logData2);

        // Act
        var newState = Reducers.ReduceAddTable(state, action);

        // Assert
        var combinedTable = newState.EventTables.First(t => t.IsCombined);
        Assert.Equal(combinedTable.Id, newState.ActiveEventLogId);
    }

    [Fact]
    public void ReduceAppendTableEvents_ShouldAppendEventsToExistingDisplayedEvents()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));

        var initialEvents = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(100, recordId: 1),
            EventUtils.CreateTestEvent(200, recordId: 2)
        };

        state = Reducers.ReduceAppendTableEvents(
            state,
            new AppendTableEventsAction(logData.Id, initialEvents));

        var deltaEvents = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(300, recordId: 3),
            EventUtils.CreateTestEvent(400, recordId: 4)
        };

        var action = new AppendTableEventsAction(logData.Id, deltaEvents);

        // Act
        var newState = Reducers.ReduceAppendTableEvents(state, action);

        // Assert
        var updatedTable = newState.EventTables.First(t => t.Id == logData.Id);
        Assert.Equal(4, newState.DisplayedEvents.Count);
        Assert.Equal(4, newState.EventCountByLog[updatedTable.Id]);
    }

    [Fact]
    public void ReduceAppendTableEvents_ShouldNotChangeIsLoading()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));

        // Table should be in loading state after AddTable
        Assert.True(state.EventTables.First(t => t.Id == logData.Id).IsLoading);

        var action = new AppendTableEventsAction(
            logData.Id,
            new List<ResolvedEvent> { EventUtils.CreateTestEvent(100, recordId: 1) });

        // Act
        var newState = Reducers.ReduceAppendTableEvents(state, action);

        // Assert - IsLoading should still be true (partial update doesn't complete loading)
        var updatedTable = newState.EventTables.First(t => t.Id == logData.Id);
        Assert.True(updatedTable.IsLoading);
        Assert.Single(newState.DisplayedEvents);
    }

    [Fact]
    public void ReduceAppendTableEvents_ShouldPreserveSortOrder()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));

        var initialEvents = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(100, recordId: 10),
            EventUtils.CreateTestEvent(200, recordId: 20)
        };

        state = Reducers.ReduceAppendTableEvents(
            state,
            new AppendTableEventsAction(logData.Id, initialEvents));

        // Append events with record IDs that should sort between and after existing
        var deltaEvents = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(300, recordId: 5),
            EventUtils.CreateTestEvent(400, recordId: 15)
        };

        var action = new AppendTableEventsAction(logData.Id, deltaEvents);

        // Act
        var newState = Reducers.ReduceAppendTableEvents(state, action);

        // Assert - sort is by DateAndTime descending; ties on TimeCreated fall through
        // to the RecordId tiebreaker, which preserves descending RecordId order here.
        var displayedEvents = newState.DisplayedEvents;
        Assert.Equal(4, displayedEvents.Count);

        var recordIds = displayedEvents.Select(e => e.RecordId).ToList();
        Assert.Equal(recordIds.OrderByDescending(x => x).ToList(), recordIds);
    }

    [Fact]
    public void ReduceAppendTableEvents_WhenTableNotFound_ShouldReturnUnchangedState()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));

        var unknownLogId = EventLogId.Create();

        var action = new AppendTableEventsAction(
            unknownLogId,
            new List<ResolvedEvent> { EventUtils.CreateTestEvent(100) });

        // Act
        var newState = Reducers.ReduceAppendTableEvents(state, action);

        // Assert
        Assert.Same(state, newState);
    }

    [Fact]
    public void ReduceAppendTableEventsBatch_WhenBatchTargetsClosedLog_ShouldSkipThatBatch()
    {
        // Arrange — open log plus a stale log id whose tab no longer exists (race: closed mid-flight)
        var openLog = new EventLogData(Constants.LogNameLog1, LogPathType.Channel, []);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(openLog));

        var staleLogId = EventLogId.Create();
        var batches = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>
        {
            { openLog.Id, [new ResolvedEvent(Constants.LogNameLog1, LogPathType.Channel) { Id = 10, RecordId = 1 }] },
            { staleLogId, [new ResolvedEvent("ClosedLog", LogPathType.Channel) { Id = 99, RecordId = 99 }] }
        };

        // Act
        var newState = Reducers.ReduceAppendTableEventsBatch(
            state,
            new AppendTableEventsBatchAction(batches));

        // Assert — stale batch is skipped; canonical and EventCountByLog only reflect the open log
        Assert.Single(newState.DisplayedEvents);
        Assert.Equal(1L, newState.DisplayedEvents[0].RecordId);
        Assert.False(newState.EventCountByLog.ContainsKey(staleLogId));
        Assert.Equal(1, newState.EventCountByLog[openLog.Id]);
    }

    [Fact]
    public void ReduceUpdateDisplayedEvents_WhenLogIsNotInActiveLogs_ShouldPreserveExistingCanonicalRows()
    {
        // Arrange — log A has rows in canonical (live-load via UpdateTable); a filter dispatch
        // then arrives with empty ActiveLogs (e.g. log opened mid-filter so the snapshot did
        // not include it). The omitted log's rows must stay in canonical — otherwise a fresh
        // load could be silently scrubbed by a stale filter result.
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));

        var loadedEvents = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 10, RecordId = 1 },
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 11, RecordId = 2 }
        };

        state = Reducers.ReduceUpdateTable(state, new UpdateTableAction(logData.Id, loadedEvents));

        var emptyActiveLogs = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>();
        var action = new UpdateDisplayedEventsAction(emptyActiveLogs);

        // Act
        var newState = Reducers.ReduceUpdateDisplayedEvents(state, action);

        // Assert — table still exists, canonical rows preserved, count map preserved.
        Assert.Single(newState.EventTables);
        Assert.Equal(2, newState.DisplayedEvents.Count);
        Assert.Equal(2, newState.EventCountByLog[logData.Id]);
    }

    [Fact]
    public void ReduceUpdateDisplayedEvents_WhenSomeLogsOmitted_ShouldReplaceIncludedAndPreserveOmitted()
    {
        // Arrange — two logs, both populated via UpdateTable. Filter dispatch arrives for log A
        // only (e.g. log B opened or finished loading after the snapshot). Log A's rows must be
        // replaced by the filter result; log B's rows must be preserved untouched.
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel, []);
        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel, []);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData1));
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData2));

        var log1Loaded = new List<ResolvedEvent>
        {
            new(Constants.LogNameLog1, LogPathType.Channel) { Id = 100, RecordId = 1 },
            new(Constants.LogNameLog1, LogPathType.Channel) { Id = 101, RecordId = 2 }
        };

        var log2Loaded = new List<ResolvedEvent>
        {
            new(Constants.LogNameLog2, LogPathType.Channel) { Id = 200, RecordId = 1 },
            new(Constants.LogNameLog2, LogPathType.Channel) { Id = 201, RecordId = 2 },
            new(Constants.LogNameLog2, LogPathType.Channel) { Id = 202, RecordId = 3 }
        };

        state = Reducers.ReduceUpdateTable(state, new UpdateTableAction(logData1.Id, log1Loaded));
        state = Reducers.ReduceUpdateTable(state, new UpdateTableAction(logData2.Id, log2Loaded));

        var log1Filtered = new List<ResolvedEvent>
        {
            new(Constants.LogNameLog1, LogPathType.Channel) { Id = 100, RecordId = 1 }
        };

        var activeLogs = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>
        {
            { logData1.Id, log1Filtered }
        };

        // Act
        var newState = Reducers.ReduceUpdateDisplayedEvents(
            state,
            new UpdateDisplayedEventsAction(activeLogs));

        // Assert — log A reduced from 2 to 1, log B's 3 rows untouched; counts reflect both.
        Assert.Equal(4, newState.DisplayedEvents.Count);
        Assert.Equal(1, newState.DisplayedEvents.Count(e => e.OwningLog == Constants.LogNameLog1));
        Assert.Equal(3, newState.DisplayedEvents.Count(e => e.OwningLog == Constants.LogNameLog2));
        Assert.Equal(1, newState.EventCountByLog[logData1.Id]);
        Assert.Equal(3, newState.EventCountByLog[logData2.Id]);
    }

    [Fact]
    public void ReduceUpdateDisplayedEvents_WhenTableComputerNameEmpty_ShouldLatchFromFirstNonEmptyEvent()
    {
        // Arrange — log first becomes visible via UpdateDisplayedEvents (filter clear), not via append
        var logData = new EventLogData(Constants.LogNameLog1, LogPathType.Channel, []);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));

        var revealedEvents = new List<ResolvedEvent>
        {
            new(Constants.LogNameLog1, LogPathType.Channel) { Id = 10, RecordId = 1, ComputerName = string.Empty },
            new(Constants.LogNameLog1, LogPathType.Channel) { Id = 11, RecordId = 2, ComputerName = Constants.EventComputerServer01 }
        };

        var activeLogs = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>
        {
            { logData.Id, revealedEvents }
        };

        // Act
        var newState = Reducers.ReduceUpdateDisplayedEvents(
            state,
            new UpdateDisplayedEventsAction(activeLogs));

        // Assert — UpdateDisplayedEvents also latches ComputerName, not just the append paths
        var updatedTable = newState.EventTables.First(t => t.Id == logData.Id);
        Assert.Equal(Constants.EventComputerServer01, updatedTable.ComputerName);
    }

    [Fact]
    public void ReduceUpdateTable_AfterPartialAppends_ShouldReplaceNotMergeWithPartials()
    {
        // Arrange — partial AppendTableEvents land first (live-load deltas), then UpdateTable arrives with the full filtered list
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var state = new LogTableState { IsDescending = false };
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));

        var partial1 = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 10, RecordId = 1 },
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 11, RecordId = 2 }
        };

        state = Reducers.ReduceAppendTableEvents(
            state,
            new AppendTableEventsAction(logData.Id, partial1));

        var partial2 = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 12, RecordId = 3 }
        };

        state = Reducers.ReduceAppendTableEvents(
            state,
            new AppendTableEventsAction(logData.Id, partial2));

        Assert.Equal(3, state.DisplayedEvents.Count);

        var fullLoad = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 20, RecordId = 1 },
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 21, RecordId = 2 },
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 22, RecordId = 3 },
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 23, RecordId = 4 }
        };

        // Act
        state = Reducers.ReduceUpdateTable(
            state,
            new UpdateTableAction(logData.Id, fullLoad));

        // Assert — partials are dropped before merging the full slice; canonical is exactly the full load
        Assert.Equal(4, state.DisplayedEvents.Count);
        Assert.Equal(state.DisplayedEvents.Select(e => e.RecordId), [1L, 2L, 3L, 4L]);
        Assert.Equal(4, state.EventCountByLog[logData.Id]);
    }

    [Fact]
    public void ReduceUpdateTable_ShouldUpdateTableEvents()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));

        var events = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(100),
            EventUtils.CreateTestEvent(200)
        };

        var action = new UpdateTableAction(logData.Id, events);

        // Act
        var newState = Reducers.ReduceUpdateTable(state, action);

        // Assert
        var updatedTable = newState.EventTables.First(t => t.Id == logData.Id);
        Assert.Equal(2, newState.DisplayedEvents.Count);
        Assert.Equal(2, newState.EventCountByLog[updatedTable.Id]);
        Assert.False(updatedTable.IsLoading);
    }

    [Fact]
    public void ReduceUpdateTable_WhenCalledTwiceForSameLog_ShouldReplaceNotDuplicate()
    {
        // Arrange — first UpdateTable populates canonical with two events
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var state = new LogTableState { IsDescending = false };
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));

        var firstLoad = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 10, RecordId = 1 },
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 11, RecordId = 2 }
        };

        state = Reducers.ReduceUpdateTable(state, new UpdateTableAction(logData.Id, firstLoad));
        Assert.Equal(2, state.DisplayedEvents.Count);

        var secondLoad = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 12, RecordId = 3 },
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 13, RecordId = 4 },
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 14, RecordId = 5 }
        };

        // Act — second UpdateTable for the same log (e.g., filter-driven reload)
        state = Reducers.ReduceUpdateTable(state, new UpdateTableAction(logData.Id, secondLoad));

        // Assert — canonical reflects only the second load; first load is replaced, not appended
        Assert.Equal(3, state.DisplayedEvents.Count);
        Assert.Equal(state.DisplayedEvents.Select(e => e.RecordId), [3L, 4L, 5L]);
        Assert.Equal(3, state.EventCountByLog[logData.Id]);
    }

    [Fact]
    public void ReduceUpdateTable_WhenDescendingOrderRequested_ShouldMergeInDescendingOrder()
    {
        // Arrange — two logs, descending sort
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel, []);
        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel, []);
        var state = new LogTableState { IsDescending = true };
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData1));
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData2));

        var eventsLog1 = new List<ResolvedEvent>
        {
            new(Constants.LogNameLog1, LogPathType.Channel) { Id = 10, RecordId = 1 },
            new(Constants.LogNameLog1, LogPathType.Channel) { Id = 11, RecordId = 3 }
        };

        var eventsLog2 = new List<ResolvedEvent>
        {
            new(Constants.LogNameLog2, LogPathType.Channel) { Id = 20, RecordId = 2 },
            new(Constants.LogNameLog2, LogPathType.Channel) { Id = 21, RecordId = 4 }
        };

        // Act
        state = Reducers.ReduceUpdateTable(state, new UpdateTableAction(logData1.Id, eventsLog1));
        state = Reducers.ReduceUpdateTable(state, new UpdateTableAction(logData2.Id, eventsLog2));

        // Assert — canonical view in descending RecordId order
        Assert.Equal(4, state.DisplayedEvents.Count);
        Assert.Equal(4L, state.DisplayedEvents[0].RecordId);
        Assert.Equal(3L, state.DisplayedEvents[1].RecordId);
        Assert.Equal(2L, state.DisplayedEvents[2].RecordId);
        Assert.Equal(1L, state.DisplayedEvents[3].RecordId);
    }

    [Fact]
    public void ReduceUpdateTable_WhenSecondLogIsEmpty_ShouldKeepFirstLogEvents()
    {
        // Arrange — one log populated, one log empty (but not loading), ascending RecordId order
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel, []);
        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel, []);
        var state = new LogTableState { IsDescending = false };
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData1));
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData2));

        var eventsLog1 = new List<ResolvedEvent>
        {
            new(Constants.LogNameLog1, LogPathType.Channel) { Id = 10, RecordId = 5 },
            new(Constants.LogNameLog1, LogPathType.Channel) { Id = 11, RecordId = 7 }
        };

        // Act
        state = Reducers.ReduceUpdateTable(state, new UpdateTableAction(logData1.Id, eventsLog1));
        state = Reducers.ReduceUpdateTable(state, new UpdateTableAction(logData2.Id, []));

        // Assert — canonical view contains only the populated log's events
        Assert.Equal(2, state.DisplayedEvents.Count);
        Assert.Equal(5L, state.DisplayedEvents[0].RecordId);
        Assert.Equal(7L, state.DisplayedEvents[1].RecordId);
    }

    [Fact]
    public void ReduceUpdateTable_WhenSecondLogPopulated_ShouldMergeIntoCanonicalInSortedOrder()
    {
        // Arrange — two logs with interleaved RecordIds, ascending RecordId order
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel, []);
        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel, []);
        var state = new LogTableState { IsDescending = false };
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData1));
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData2));

        var eventsLog1 = new List<ResolvedEvent>
        {
            new(Constants.LogNameLog1, LogPathType.Channel) { Id = 10, RecordId = 1 },
            new(Constants.LogNameLog1, LogPathType.Channel) { Id = 11, RecordId = 3 }
        };

        var eventsLog2 = new List<ResolvedEvent>
        {
            new(Constants.LogNameLog2, LogPathType.Channel) { Id = 20, RecordId = 2 },
            new(Constants.LogNameLog2, LogPathType.Channel) { Id = 21, RecordId = 4 }
        };

        // Act — UpdateTable maintains the canonical view atomically; no follow-up call needed.
        state = Reducers.ReduceUpdateTable(state, new UpdateTableAction(logData1.Id, eventsLog1));
        state = Reducers.ReduceUpdateTable(state, new UpdateTableAction(logData2.Id, eventsLog2));

        // Assert — canonical view interleaves both logs in RecordId order
        Assert.Equal(4, state.DisplayedEvents.Count);
        Assert.Equal(1L, state.DisplayedEvents[0].RecordId);
        Assert.Equal(2L, state.DisplayedEvents[1].RecordId);
        Assert.Equal(3L, state.DisplayedEvents[2].RecordId);
        Assert.Equal(4L, state.DisplayedEvents[3].RecordId);
        Assert.Equal(Constants.LogNameLog1, state.DisplayedEvents[0].OwningLog);
        Assert.Equal(Constants.LogNameLog2, state.DisplayedEvents[1].OwningLog);
    }

    [Fact]
    public void ReduceUpdateTable_WhenTableNotFound_ShouldReturnStateUnchanged()
    {
        // Arrange — empty state, no tables
        var state = new LogTableState();
        var staleLogId = EventLogId.Create();
        var events = new List<ResolvedEvent> { EventUtils.CreateTestEvent(100) };
        var action = new UpdateTableAction(staleLogId, events);

        // Act — stale UpdateTable for a non-existent table
        var newState = Reducers.ReduceUpdateTable(state, action);

        // Assert — state unchanged, no exception thrown
        Assert.Same(state, newState);
    }

    [Fact]
    public void ReduceUpdateTable_WhenTimeCreatedDivergesFromRecordId_ShouldMergeByTimeCreated()
    {
        // Arrange — RecordIds ascending but TimeCreated descending: verifies sort is by timestamp
        var baseTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var log1Events = new List<ResolvedEvent>
        {
            new(Constants.LogNameLog1, LogPathType.Channel) { Id = 10, RecordId = 1, TimeCreated = baseTime.AddSeconds(40) },
            new(Constants.LogNameLog1, LogPathType.Channel) { Id = 11, RecordId = 2, TimeCreated = baseTime.AddSeconds(20) }
        };

        var log2Events = new List<ResolvedEvent>
        {
            new(Constants.LogNameLog2, LogPathType.Channel) { Id = 20, RecordId = 1, TimeCreated = baseTime.AddSeconds(30) },
            new(Constants.LogNameLog2, LogPathType.Channel) { Id = 21, RecordId = 2, TimeCreated = baseTime.AddSeconds(10) }
        };

        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel, []);
        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel, []);
        var state = new LogTableState { IsDescending = false };
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData1));
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData2));

        // Act
        state = Reducers.ReduceUpdateTable(state, new UpdateTableAction(logData1.Id, log1Events));
        state = Reducers.ReduceUpdateTable(state, new UpdateTableAction(logData2.Id, log2Events));

        // Assert — canonical comes out time-ordered, not RecordId-ordered
        var actualTimes = state.DisplayedEvents.Select(e => e.TimeCreated).ToList();
        var expectedTimes = new List<DateTime>
        {
            baseTime.AddSeconds(10),
            baseTime.AddSeconds(20),
            baseTime.AddSeconds(30),
            baseTime.AddSeconds(40)
        };
        Assert.Equal(expectedTimes, actualTimes);
    }
}
