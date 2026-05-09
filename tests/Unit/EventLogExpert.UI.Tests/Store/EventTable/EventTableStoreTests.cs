// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.EventTable;
using EventLogExpert.UI.Tests.TestUtils;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.Store.EventTable;

public sealed class EventTableStoreTests
{
    [Fact]
    public void EventTableAction_AddTable_ShouldStoreLogData()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);

        // Act
        var action = new EventTableAction.AddTable(logData);

        // Assert
        Assert.Equal(logData, action.LogData);
    }

    [Fact]
    public void EventTableAction_CloseLog_ShouldStoreLogId()
    {
        // Arrange
        var logId = EventLogId.Create();

        // Act
        var action = new EventTableAction.CloseLog(logId);

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

        var order = ColumnDefaults.Order;

        // Act
        var action = new EventTableAction.LoadColumnsCompleted(columns, widths, order);

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
        var action = new EventTableAction.SetActiveTable(logId);

        // Assert
        Assert.Equal(logId, action.LogId);
    }

    [Fact]
    public void EventTableAction_SetOrderBy_ShouldStoreColumnName()
    {
        // Act
        var action = new EventTableAction.SetOrderBy(ColumnName.Level);

        // Assert
        Assert.Equal(ColumnName.Level, action.OrderBy);
    }

    [Fact]
    public void EventTableAction_SetOrderBy_WithNull_ShouldStoreNull()
    {
        // Act
        var action = new EventTableAction.SetOrderBy(null);

        // Assert
        Assert.Null(action.OrderBy);
    }

    [Fact]
    public void EventTableAction_ToggleColumn_ShouldStoreColumnName()
    {
        // Act
        var action = new EventTableAction.ToggleColumn(ColumnName.Source);

        // Assert
        Assert.Equal(ColumnName.Source, action.ColumnName);
    }

    [Fact]
    public void EventTableAction_ToggleLoading_ShouldStoreLogId()
    {
        // Arrange
        var logId = EventLogId.Create();

        // Act
        var action = new EventTableAction.ToggleLoading(logId);

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
        var action = new EventTableAction.UpdateDisplayedEvents(activeLogs);

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
        var action = new EventTableAction.UpdateTable(logId, events);

        // Assert
        Assert.Equal(logId, action.LogId);
        Assert.Equal(2, action.Events.Count);
    }

    [Fact]
    public void EventTableModel_ComputerName_AfterFirstEventArrives_ShouldBeStoredOnTable()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData));

        var firstBatch = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 10, RecordId = 1, ComputerName = Constants.EventComputerServer01 }
        };

        state = EventTableReducers.ReduceAppendTableEvents(
            state,
            new EventTableAction.AppendTableEvents(logData.Id, firstBatch));

        var secondBatch = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 11, RecordId = 2, ComputerName = Constants.EventComputerServer02 }
        };

        // Act — second batch with a different ComputerName must not overwrite the latched value
        state = EventTableReducers.ReduceAppendTableEvents(
            state,
            new EventTableAction.AppendTableEvents(logData.Id, secondBatch));

        // Assert — ComputerName latches to the first non-empty observed value
        var table = state.EventTables.First(t => t.Id == logData.Id);
        Assert.Equal(Constants.EventComputerServer01, table.ComputerName);
    }

    [Fact]
    public void EventTableModel_ComputerName_WhenFirstEventHasEmptyComputerName_ShouldUseLaterEventInBatch()
    {
        // Arrange — first event has an empty ComputerName (resolver miss); second carries the name
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData));

        var batch = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 10, RecordId = 1, ComputerName = string.Empty },
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 11, RecordId = 2, ComputerName = Constants.EventComputerServer01 }
        };

        // Act
        state = EventTableReducers.ReduceAppendTableEvents(
            state,
            new EventTableAction.AppendTableEvents(logData.Id, batch));

        // Assert — reducer scans past the empty leading event and latches the first non-empty value
        var table = state.EventTables.First(t => t.Id == logData.Id);
        Assert.Equal(Constants.EventComputerServer01, table.ComputerName);
    }

    [Fact]
    public void EventTableModel_ComputerName_WhenNoEvents_ShouldReturnEmpty()
    {
        // Arrange
        var model = new EventTableModel(EventLogId.Create());

        // Act
        var computerName = model.ComputerName;

        // Assert
        Assert.Equal(string.Empty, computerName);
    }

    [Fact]
    public void EventTableModel_ShouldHaveUniqueId()
    {
        // Arrange & Act
        var model1 = new EventTableModel(EventLogId.Create());
        var model2 = new EventTableModel(EventLogId.Create());

        // Assert
        Assert.NotEqual(model1.Id, model2.Id);
    }

    [Fact]
    public void EventTableState_DefaultState_ShouldHaveCorrectDefaults()
    {
        // Arrange & Act
        var state = new EventTableState();

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
    public void IntegrationTest_ColumnManagement()
    {
        // Arrange
        var state = new EventTableState();

        // Act - Load columns
        var columns = new Dictionary<ColumnName, bool>
        {
            { ColumnName.Level, true },
            { ColumnName.DateAndTime, true },
            { ColumnName.Source, false }
        };

        state = EventTableReducers.ReduceLoadColumnsCompleted(
            state,
            new EventTableAction.LoadColumnsCompleted(columns, new Dictionary<ColumnName, int>(), ColumnDefaults.Order));

        // Assert
        Assert.Equal(3, state.Columns.Count);
        Assert.True(state.Columns[ColumnName.Level]);
        Assert.False(state.Columns[ColumnName.Source]);
    }

    [Fact]
    public void IntegrationTest_LoadAndUpdateTableEvents()
    {
        // Arrange
        var state = new EventTableState();
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);

        // Act - Add table
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData));
        Assert.True(state.EventTables.First().IsLoading);

        // Act - Update table with events
        var events = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(100),
            EventUtils.CreateTestEvent(200)
        };

        state = EventTableReducers.ReduceUpdateTable(state, new EventTableAction.UpdateTable(logData.Id, events));

        // Assert
        Assert.False(state.EventTables.First().IsLoading);
        Assert.Equal(2, state.DisplayedEvents.Count);
    }

    [Fact]
    public void IntegrationTest_OpenMultipleLogsAndCloseOne()
    {
        // Arrange
        var state = new EventTableState();
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel, []);
        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel, []);
        var logData3 = new EventLogData(Constants.LogNameLog3, LogPathType.Channel, []);

        // Act - Open three logs
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData1));
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData2));
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData3));

        // Assert - Should have 4 tables (3 logs + 1 combined)
        Assert.Equal(4, state.EventTables.Count);
        Assert.Single(state.EventTables, t => t.IsCombined);

        // Act - Close one log
        state = EventTableReducers.ReduceCloseLog(state, new EventTableAction.CloseLog(logData2.Id));

        // Assert - Should have 3 tables (2 logs + 1 combined)
        Assert.Equal(3, state.EventTables.Count);
        Assert.Single(state.EventTables, t => t.IsCombined);
        Assert.DoesNotContain(state.EventTables, t => t.Id == logData2.Id);
    }

    [Fact]
    public void ReduceAddTable_WhenCombinedExists_ShouldNotCreateAnotherCombined()
    {
        // Arrange
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel, []);
        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel, []);
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData1));
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData2));

        var logData3 = new EventLogData(Constants.LogNameLog3, LogPathType.Channel, []);
        var action = new EventTableAction.AddTable(logData3);

        // Act
        var newState = EventTableReducers.ReduceAddTable(state, action);

        // Assert
        Assert.Equal(4, newState.EventTables.Count);
        Assert.Single(newState.EventTables, t => t.IsCombined);
    }

    [Fact]
    public void ReduceAddTable_WhenFirstTable_ShouldBeLoading()
    {
        // Arrange
        var state = new EventTableState();
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var action = new EventTableAction.AddTable(logData);

        // Act
        var newState = EventTableReducers.ReduceAddTable(state, action);

        // Assert
        Assert.True(newState.EventTables.First().IsLoading);
    }

    [Fact]
    public void ReduceAddTable_WhenFirstTable_ShouldSetAsActive()
    {
        // Arrange
        var state = new EventTableState();
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var action = new EventTableAction.AddTable(logData);

        // Act
        var newState = EventTableReducers.ReduceAddTable(state, action);

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
        var state = new EventTableState();
        var logData = new EventLogData(Constants.FilePathTestEvtx, LogPathType.File, []);
        var action = new EventTableAction.AddTable(logData);

        // Act
        var newState = EventTableReducers.ReduceAddTable(state, action);

        // Assert
        Assert.Equal(Constants.FilePathTestEvtx, newState.EventTables.First().FileName);
        Assert.Equal(LogPathType.File, newState.EventTables.First().LogPathType);
    }

    [Fact]
    public void ReduceAddTable_WhenFirstTable_WithLogName_ShouldNotSetFileName()
    {
        // Arrange
        var state = new EventTableState();
        var logData = new EventLogData(Constants.LogNameApplication, LogPathType.Channel, []);
        var action = new EventTableAction.AddTable(logData);

        // Act
        var newState = EventTableReducers.ReduceAddTable(state, action);

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
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData1));

        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel, []);
        var action = new EventTableAction.AddTable(logData2);

        // Act
        var newState = EventTableReducers.ReduceAddTable(state, action);

        // Assert
        Assert.Equal(3, newState.EventTables.Count);
        Assert.Contains(newState.EventTables, t => t.IsCombined);
    }

    [Fact]
    public void ReduceAddTable_WhenSecondTable_ShouldSetCombinedAsActive()
    {
        // Arrange
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel, []);
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData1));

        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel, []);
        var action = new EventTableAction.AddTable(logData2);

        // Act
        var newState = EventTableReducers.ReduceAddTable(state, action);

        // Assert
        var combinedTable = newState.EventTables.First(t => t.IsCombined);
        Assert.Equal(combinedTable.Id, newState.ActiveEventLogId);
    }

    [Fact]
    public void ReduceAppendTableEventsBatch_WhenBatchTargetsClosedLog_ShouldSkipThatBatch()
    {
        // Arrange — open log plus a stale log id whose tab no longer exists (race: closed mid-flight)
        var openLog = new EventLogData(Constants.LogNameLog1, LogPathType.Channel, []);
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(openLog));

        var staleLogId = EventLogId.Create();
        var batches = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>
        {
            { openLog.Id, [new(Constants.LogNameLog1, LogPathType.Channel) { Id = 10, RecordId = 1 }] },
            { staleLogId, [new("ClosedLog", LogPathType.Channel) { Id = 99, RecordId = 99 }] }
        };

        // Act
        var newState = EventTableReducers.ReduceAppendTableEventsBatch(
            state,
            new EventTableAction.AppendTableEventsBatch(batches));

        // Assert — stale batch is skipped; canonical and EventCountByLog only reflect the open log
        Assert.Single(newState.DisplayedEvents);
        Assert.Equal(1L, newState.DisplayedEvents[0].RecordId);
        Assert.False(newState.EventCountByLog.ContainsKey(staleLogId));
        Assert.Equal(1, newState.EventCountByLog[openLog.Id]);
    }

    [Fact]
    public void ReduceAppendTableEvents_ShouldAppendEventsToExistingDisplayedEvents()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData));

        var initialEvents = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(100, recordId: 1),
            EventUtils.CreateTestEvent(200, recordId: 2)
        };

        state = EventTableReducers.ReduceAppendTableEvents(
            state,
            new EventTableAction.AppendTableEvents(logData.Id, initialEvents));

        var deltaEvents = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(300, recordId: 3),
            EventUtils.CreateTestEvent(400, recordId: 4)
        };

        var action = new EventTableAction.AppendTableEvents(logData.Id, deltaEvents);

        // Act
        var newState = EventTableReducers.ReduceAppendTableEvents(state, action);

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
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData));

        // Table should be in loading state after AddTable
        Assert.True(state.EventTables.First(t => t.Id == logData.Id).IsLoading);

        var action = new EventTableAction.AppendTableEvents(
            logData.Id,
            new List<ResolvedEvent> { EventUtils.CreateTestEvent(100, recordId: 1) });

        // Act
        var newState = EventTableReducers.ReduceAppendTableEvents(state, action);

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
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData));

        var initialEvents = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(100, recordId: 10),
            EventUtils.CreateTestEvent(200, recordId: 20)
        };

        state = EventTableReducers.ReduceAppendTableEvents(
            state,
            new EventTableAction.AppendTableEvents(logData.Id, initialEvents));

        // Append events with record IDs that should sort between and after existing
        var deltaEvents = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(300, recordId: 5),
            EventUtils.CreateTestEvent(400, recordId: 15)
        };

        var action = new EventTableAction.AppendTableEvents(logData.Id, deltaEvents);

        // Act
        var newState = EventTableReducers.ReduceAppendTableEvents(state, action);

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
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData));

        var unknownLogId = EventLogId.Create();

        var action = new EventTableAction.AppendTableEvents(
            unknownLogId,
            new List<ResolvedEvent> { EventUtils.CreateTestEvent(100) });

        // Act
        var newState = EventTableReducers.ReduceAppendTableEvents(state, action);

        // Assert
        Assert.Same(state, newState);
    }

    [Fact]
    public void ReduceCloseAll_ShouldClearAllTables()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData));

        // Act
        var newState = EventTableReducers.ReduceCloseAll(state);

        // Assert
        Assert.Empty(newState.EventTables);
        Assert.Null(newState.ActiveEventLogId);
    }

    [Fact]
    public void ReduceCloseLog_ShouldScrubCanonicalRowsAndCountForClosedLog()
    {
        // Arrange — three logs with canonical rows; close the middle one. Canonical must lose
        // only the closed log's rows, and EventCountByLog must drop the closed log's id.
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel, []);
        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel, []);
        var logData3 = new EventLogData(Constants.LogNameLog3, LogPathType.Channel, []);
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData1));
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData2));
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData3));

        var log1Events = new List<ResolvedEvent>
        {
            new(Constants.LogNameLog1, LogPathType.Channel) { Id = 100, RecordId = 1 }
        };

        var log2Events = new List<ResolvedEvent>
        {
            new(Constants.LogNameLog2, LogPathType.Channel) { Id = 200, RecordId = 1 },
            new(Constants.LogNameLog2, LogPathType.Channel) { Id = 201, RecordId = 2 }
        };

        var log3Events = new List<ResolvedEvent>
        {
            new(Constants.LogNameLog3, LogPathType.Channel) { Id = 300, RecordId = 1 }
        };

        state = EventTableReducers.ReduceUpdateTable(state, new EventTableAction.UpdateTable(logData1.Id, log1Events));
        state = EventTableReducers.ReduceUpdateTable(state, new EventTableAction.UpdateTable(logData2.Id, log2Events));
        state = EventTableReducers.ReduceUpdateTable(state, new EventTableAction.UpdateTable(logData3.Id, log3Events));

        // Act
        var newState = EventTableReducers.ReduceCloseLog(state, new EventTableAction.CloseLog(logData2.Id));

        // Assert — canonical loses only logData2's rows; count map drops only logData2's id.
        Assert.Equal(2, newState.DisplayedEvents.Count);
        Assert.DoesNotContain(newState.DisplayedEvents, e => e.OwningLog == Constants.LogNameLog2);
        Assert.False(newState.EventCountByLog.ContainsKey(logData2.Id));
        Assert.Equal(1, newState.EventCountByLog[logData1.Id]);
        Assert.Equal(1, newState.EventCountByLog[logData3.Id]);
    }

    [Fact]
    public void ReduceCloseLog_WhenClosingDownToOneLog_ShouldKeepOnlySoleRemainingLogRows()
    {
        // Arrange — two logs with canonical rows; close one. The Combined table is removed and
        // canonical is filtered down to just the remaining log's rows. Exercises the case-1
        // branch (which uses FilterByOwningLog rather than FilterOutOwningLog).
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel, []);
        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel, []);
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData1));
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData2));

        var log1Events = new List<ResolvedEvent>
        {
            new(Constants.LogNameLog1, LogPathType.Channel) { Id = 100, RecordId = 1 }
        };

        var log2Events = new List<ResolvedEvent>
        {
            new(Constants.LogNameLog2, LogPathType.Channel) { Id = 200, RecordId = 1 },
            new(Constants.LogNameLog2, LogPathType.Channel) { Id = 201, RecordId = 2 }
        };

        state = EventTableReducers.ReduceUpdateTable(state, new EventTableAction.UpdateTable(logData1.Id, log1Events));
        state = EventTableReducers.ReduceUpdateTable(state, new EventTableAction.UpdateTable(logData2.Id, log2Events));

        // Act
        var newState = EventTableReducers.ReduceCloseLog(state, new EventTableAction.CloseLog(logData1.Id));

        // Assert — only logData2's rows remain; logData1 dropped from count map.
        Assert.Equal(2, newState.DisplayedEvents.Count);
        Assert.All(newState.DisplayedEvents, e => Assert.Equal(Constants.LogNameLog2, e.OwningLog));
        Assert.False(newState.EventCountByLog.ContainsKey(logData1.Id));
        Assert.Equal(2, newState.EventCountByLog[logData2.Id]);
    }

    [Fact]
    public void ReduceCloseLog_WhenLastTable_ShouldClearAll()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData));

        var action = new EventTableAction.CloseLog(logData.Id);

        // Act
        var newState = EventTableReducers.ReduceCloseLog(state, action);

        // Assert
        Assert.Empty(newState.EventTables);
        Assert.Null(newState.ActiveEventLogId);
    }

    [Fact]
    public void ReduceCloseLog_WhenThreeTables_ShouldKeepCombinedAndRemainingTables()
    {
        // Arrange
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel, []);
        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel, []);
        var logData3 = new EventLogData(Constants.LogNameLog3, LogPathType.Channel, []);
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData1));
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData2));
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData3));

        var action = new EventTableAction.CloseLog(logData1.Id);

        // Act
        var newState = EventTableReducers.ReduceCloseLog(state, action);

        // Assert
        Assert.Equal(3, newState.EventTables.Count);
        Assert.Single(newState.EventTables, t => t.IsCombined);
        Assert.Contains(newState.EventTables, t => t.Id == logData2.Id);
        Assert.Contains(newState.EventTables, t => t.Id == logData3.Id);
    }

    [Fact]
    public void ReduceCloseLog_WhenTwoTables_ShouldRemoveCombinedAndSetRemaining()
    {
        // Arrange
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel, []);
        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel, []);
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData1));
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData2));

        var action = new EventTableAction.CloseLog(logData1.Id);

        // Act
        var newState = EventTableReducers.ReduceCloseLog(state, action);

        // Assert
        Assert.Single(newState.EventTables);
        Assert.DoesNotContain(newState.EventTables, t => t.IsCombined);
        Assert.Equal(logData2.Id, newState.EventTables.First().Id);
        Assert.Equal(logData2.Id, newState.ActiveEventLogId);
    }

    [Fact]
    public void ReduceLoadColumnsCompleted_ShouldUpdateColumns()
    {
        // Arrange
        var state = new EventTableState();

        var columns = new Dictionary<ColumnName, bool>
        {
            { ColumnName.Level, true },
            { ColumnName.Source, false }
        };

        var widths = new Dictionary<ColumnName, int>
        {
            { ColumnName.Level, 120 },
            { ColumnName.Source, 250 }
        };

        var order = ColumnDefaults.Order;
        var action = new EventTableAction.LoadColumnsCompleted(columns, widths, order);

        // Act
        var newState = EventTableReducers.ReduceLoadColumnsCompleted(state, action);

        // Assert
        Assert.Equal(2, newState.Columns.Count);
        Assert.True(newState.Columns[ColumnName.Level]);
        Assert.False(newState.Columns[ColumnName.Source]);
        Assert.Equal(120, newState.ColumnWidths[ColumnName.Level]);
        Assert.Equal(250, newState.ColumnWidths[ColumnName.Source]);
        Assert.Equal(order, newState.ColumnOrder);
    }

    [Fact]
    public void ReduceReorderColumn_ShouldInsertAfterTarget()
    {
        // Arrange
        var state = new EventTableState
        {
            ColumnOrder = [ColumnName.Level, ColumnName.DateAndTime, ColumnName.Source, ColumnName.EventId]
        };

        // Move Level after Source (drag right, insertAfter = true)
        var action = new EventTableAction.ReorderColumn(ColumnName.Level, ColumnName.Source, true);

        // Act
        var newState = EventTableReducers.ReduceReorderColumn(state, action);

        // Assert
        Assert.Equal(ColumnName.DateAndTime, newState.ColumnOrder[0]);
        Assert.Equal(ColumnName.Source, newState.ColumnOrder[1]);
        Assert.Equal(ColumnName.Level, newState.ColumnOrder[2]);
        Assert.Equal(ColumnName.EventId, newState.ColumnOrder[3]);
    }

    [Fact]
    public void ReduceReorderColumn_ShouldInsertBeforeTarget()
    {
        // Arrange
        var state = new EventTableState
        {
            ColumnOrder = [ColumnName.Level, ColumnName.DateAndTime, ColumnName.Source, ColumnName.EventId]
        };

        // Move Source before Level (drag left, insertAfter = false)
        var action = new EventTableAction.ReorderColumn(ColumnName.Source, ColumnName.Level, false);

        // Act
        var newState = EventTableReducers.ReduceReorderColumn(state, action);

        // Assert
        Assert.Equal(ColumnName.Source, newState.ColumnOrder[0]);
        Assert.Equal(ColumnName.Level, newState.ColumnOrder[1]);
        Assert.Equal(ColumnName.DateAndTime, newState.ColumnOrder[2]);
        Assert.Equal(ColumnName.EventId, newState.ColumnOrder[3]);
    }

    [Fact]
    public void ReduceReorderColumn_WhenColumnNotInOrder_ShouldReturnUnchanged()
    {
        // Arrange
        var state = new EventTableState
        {
            ColumnOrder = [ColumnName.Level, ColumnName.DateAndTime]
        };

        var action = new EventTableAction.ReorderColumn(ColumnName.Source, ColumnName.Level, true);

        // Act
        var newState = EventTableReducers.ReduceReorderColumn(state, action);

        // Assert
        Assert.Equal(state.ColumnOrder, newState.ColumnOrder);
    }

    [Fact]
    public void ReduceReorderColumn_WhenTargetMissing_ShouldReturnUnchanged()
    {
        // Arrange
        var state = new EventTableState
        {
            ColumnOrder = [ColumnName.Level, ColumnName.DateAndTime, ColumnName.Source]
        };

        var action = new EventTableAction.ReorderColumn(ColumnName.Level, ColumnName.EventId, true);

        // Act
        var newState = EventTableReducers.ReduceReorderColumn(state, action);

        // Assert
        Assert.Equal(state.ColumnOrder, newState.ColumnOrder);
    }

    [Fact]
    public void ReduceReorderColumn_WithDisabledColumnsInterleaved_ShouldInsertRelativeToTarget()
    {
        // Arrange — full order has disabled columns interleaved among visible ones
        var state = new EventTableState
        {
            ColumnOrder = [ColumnName.Level, ColumnName.ActivityId, ColumnName.DateAndTime, ColumnName.Log, ColumnName.Source]
        };

        // Drag Level right and drop "after Source" (insertAfter = true)
        var action = new EventTableAction.ReorderColumn(ColumnName.Level, ColumnName.Source, true);

        // Act
        var newState = EventTableReducers.ReduceReorderColumn(state, action);

        // Assert — Level should land immediately after Source, regardless of disabled columns
        var levelIndex = newState.ColumnOrder.IndexOf(ColumnName.Level);
        var sourceIndex = newState.ColumnOrder.IndexOf(ColumnName.Source);
        Assert.Equal(sourceIndex + 1, levelIndex);
    }

    [Fact]
    public void ReduceSetActiveTable_WhenTableIsLoading_ShouldChangeActive()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData));

        var action = new EventTableAction.SetActiveTable(logData.Id);

        // Act
        var newState = EventTableReducers.ReduceSetActiveTable(state, action);

        // Assert
        Assert.Equal(logData.Id, newState.ActiveEventLogId);
    }

    [Fact]
    public void ReduceSetActiveTable_WhenTableIsNotLoading_ShouldChangeActive()
    {
        // Arrange
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel, []);
        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel, []);
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData1));
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData2));

        // Mark as not loading
        state = EventTableReducers.ReduceToggleLoading(state, new EventTableAction.ToggleLoading(logData2.Id));

        var action = new EventTableAction.SetActiveTable(logData2.Id);

        // Act
        var newState = EventTableReducers.ReduceSetActiveTable(state, action);

        // Assert
        Assert.Equal(logData2.Id, newState.ActiveEventLogId);
    }

    [Fact]
    public void ReduceSetActiveTable_WhenTableNotFound_ShouldReturnStateUnchanged()
    {
        // Arrange
        var state = new EventTableState();
        var staleLogId = EventLogId.Create();
        var action = new EventTableAction.SetActiveTable(staleLogId);

        // Act
        var newState = EventTableReducers.ReduceSetActiveTable(state, action);

        // Assert
        Assert.Same(state, newState);
    }

    [Fact]
    public void ReduceSetColumnWidth_ShouldUpdateWidth()
    {
        // Arrange
        var state = new EventTableState
        {
            ColumnWidths = new Dictionary<ColumnName, int>
            {
                { ColumnName.Level, 100 },
                { ColumnName.Source, 250 }
            }.ToImmutableDictionary()
        };

        var action = new EventTableAction.SetColumnWidth(ColumnName.Level, 150);

        // Act
        var newState = EventTableReducers.ReduceSetColumnWidth(state, action);

        // Assert
        Assert.Equal(150, newState.ColumnWidths[ColumnName.Level]);
        Assert.Equal(250, newState.ColumnWidths[ColumnName.Source]);
    }

    [Fact]
    public void ReduceSetColumnWidth_WhenColumnNotYetInWidths_ShouldAddIt()
    {
        // Arrange
        var state = new EventTableState();

        var action = new EventTableAction.SetColumnWidth(ColumnName.EventId, 90);

        // Act
        var newState = EventTableReducers.ReduceSetColumnWidth(state, action);

        // Assert
        Assert.Equal(90, newState.ColumnWidths[ColumnName.EventId]);
    }

    [Fact]
    public void ReduceSetOrderBy_WhenToggledOff_ShouldSortPerLogListsByEffectiveComparator()
    {
        // After the user clicks the active column header to deselect it (OrderBy reset to null),
        // per-log lists must remain sorted by the same comparator the combined merge will use
        // (DateAndTime). Otherwise a subsequent ReduceUpdateCombinedEvents merges RecordId-sorted
        // inputs under a DateAndTime comparator and produces silently incorrect output.
        var baseTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var state = new EventTableState { OrderBy = ColumnName.Level, IsDescending = false };
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData));

        var events = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 10, RecordId = 1, TimeCreated = baseTime.AddSeconds(40) },
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 11, RecordId = 2, TimeCreated = baseTime.AddSeconds(20) }
        };

        state = EventTableReducers.ReduceUpdateTable(state, new EventTableAction.UpdateTable(logData.Id, events));

        // Act — user deselects the active column
        var toggledState = EventTableReducers.ReduceSetOrderBy(state, new EventTableAction.SetOrderBy(ColumnName.Level));

        // Assert — toggle-off forces descending; the order distinguishes a DateAndTime sort
        // (events ordered +40s then +20s) from the buggy RecordId sort (which would order
        // RecordId=2 (+20s) before RecordId=1 (+40s)).
        Assert.Null(toggledState.OrderBy);
        Assert.True(toggledState.IsDescending);
        var sortedEvents = toggledState.DisplayedEvents;
        Assert.Equal(baseTime.AddSeconds(40), sortedEvents[0].TimeCreated);
        Assert.Equal(baseTime.AddSeconds(20), sortedEvents[1].TimeCreated);
    }

    [Fact]
    public void ReduceSetOrderBy_WithNewColumn_ShouldUpdateOrderBy()
    {
        // Arrange
        var state = new EventTableState { OrderBy = ColumnName.DateAndTime };
        var action = new EventTableAction.SetOrderBy(ColumnName.Level);

        // Act
        var newState = EventTableReducers.ReduceSetOrderBy(state, action);

        // Assert
        Assert.Equal(ColumnName.Level, newState.OrderBy);
    }

    [Fact]
    public void ReduceSetOrderBy_WithSameColumn_ShouldToggleSorting()
    {
        // Arrange
        var state = new EventTableState { OrderBy = ColumnName.Level, IsDescending = true };
        var action = new EventTableAction.SetOrderBy(ColumnName.Level);

        // Act
        var newState = EventTableReducers.ReduceSetOrderBy(state, action);

        // Assert
        Assert.Null(newState.OrderBy);
        Assert.True(newState.IsDescending);
    }

    [Fact]
    public void ReduceToggleLoading_ShouldToggleLoadingState()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData));

        Assert.True(state.EventTables.First().IsLoading);

        var action = new EventTableAction.ToggleLoading(logData.Id);

        // Act
        var newState = EventTableReducers.ReduceToggleLoading(state, action);

        // Assert
        Assert.False(newState.EventTables.First(t => t.Id == logData.Id).IsLoading);
    }

    [Fact]
    public void ReduceToggleLoading_WhenTableNotFound_ShouldReturnSameState()
    {
        // Arrange
        var state = new EventTableState();
        var action = new EventTableAction.ToggleLoading(EventLogId.Create());

        // Act
        var newState = EventTableReducers.ReduceToggleLoading(state, action);

        // Assert
        Assert.Same(state, newState);
    }

    [Fact]
    public void ReduceToggleSorting_ShouldToggleIsDescending()
    {
        // Arrange
        var state = new EventTableState { IsDescending = true };

        // Act
        var newState = EventTableReducers.ReduceToggleSorting(state);

        // Assert
        Assert.False(newState.IsDescending);
    }

    [Fact]
    public void ReduceToggleSorting_WhenOrderByIsNull_ShouldPreserveNullOrderBy()
    {
        // ToggleSorting only flips IsDescending. When the user has previously deselected the
        // active column (OrderBy is null), toggling sort direction must preserve that null so
        // the UI does not silently re-light a column header. Sort still uses the effective
        // (DateAndTime) comparator inside SortDisplayEvents.
        var baseTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var state = new EventTableState { OrderBy = null, IsDescending = false };
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData));

        var events = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 10, RecordId = 1, TimeCreated = baseTime.AddSeconds(40) },
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 11, RecordId = 2, TimeCreated = baseTime.AddSeconds(20) }
        };

        state = EventTableReducers.ReduceUpdateTable(state, new EventTableAction.UpdateTable(logData.Id, events));

        // Act
        var toggledState = EventTableReducers.ReduceToggleSorting(state);

        // Assert
        Assert.Null(toggledState.OrderBy);
        Assert.True(toggledState.IsDescending);
        var sortedEvents = toggledState.DisplayedEvents;
        Assert.Equal(baseTime.AddSeconds(40), sortedEvents[0].TimeCreated);
        Assert.Equal(baseTime.AddSeconds(20), sortedEvents[1].TimeCreated);
    }

    [Fact]
    public void ReduceUpdateDisplayedEvents_ShouldUpdateSlicesForLogsInActiveLogs()
    {
        // Arrange — two logs added, then a filter-driven UpdateDisplayedEvents arrives with
        // post-filter results for both logs.
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel, []);
        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel, []);
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData1));
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData2));

        var log1Events = new List<ResolvedEvent>
        {
            new(Constants.LogNameLog1, LogPathType.Channel) { Id = 10, RecordId = 1 }
        };

        var log2Events = new List<ResolvedEvent>
        {
            new(Constants.LogNameLog2, LogPathType.Channel) { Id = 20, RecordId = 2 }
        };

        var activeLogs = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>
        {
            { logData1.Id, log1Events },
            { logData2.Id, log2Events }
        };

        var action = new EventTableAction.UpdateDisplayedEvents(activeLogs);

        // Act
        var newState = EventTableReducers.ReduceUpdateDisplayedEvents(state, action);

        // Assert — canonical contains both logs' filtered events; per-log counts updated.
        Assert.Equal(2, newState.DisplayedEvents.Count);
        Assert.Equal(1, newState.EventCountByLog[logData1.Id]);
        Assert.Equal(1, newState.EventCountByLog[logData2.Id]);
    }

    [Fact]
    public void ReduceUpdateDisplayedEvents_WhenActiveLogsContainsClosedLogId_ShouldSkipIt()
    {
        // Arrange — filter result includes a log id whose tab was closed mid-flight
        var openLog = new EventLogData(Constants.LogNameLog1, LogPathType.Channel, []);
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(openLog));

        var staleLogId = EventLogId.Create();
        var openLogEvents = new List<ResolvedEvent>
        {
            new(Constants.LogNameLog1, LogPathType.Channel) { Id = 10, RecordId = 1 }
        };

        var staleEvents = new List<ResolvedEvent>
        {
            new("ClosedLog", LogPathType.Channel) { Id = 99, RecordId = 99 }
        };

        var activeLogs = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>
        {
            { openLog.Id, openLogEvents },
            { staleLogId, staleEvents }
        };

        // Act
        var newState = EventTableReducers.ReduceUpdateDisplayedEvents(
            state,
            new EventTableAction.UpdateDisplayedEvents(activeLogs));

        // Assert — closed log id contributes nothing to canonical or to the count map
        Assert.Single(newState.DisplayedEvents);
        Assert.Equal(1L, newState.DisplayedEvents[0].RecordId);
        Assert.False(newState.EventCountByLog.ContainsKey(staleLogId));
    }

    [Fact]
    public void ReduceUpdateDisplayedEvents_WhenLogIsNotInActiveLogs_ShouldPreserveExistingCanonicalRows()
    {
        // Arrange — log A has rows in canonical (live-load via UpdateTable); a filter dispatch
        // then arrives with empty ActiveLogs (e.g. log opened mid-filter so the snapshot did
        // not include it). The omitted log's rows must stay in canonical — otherwise a fresh
        // load could be silently scrubbed by a stale filter result.
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData));

        var loadedEvents = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 10, RecordId = 1 },
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 11, RecordId = 2 }
        };

        state = EventTableReducers.ReduceUpdateTable(state, new EventTableAction.UpdateTable(logData.Id, loadedEvents));

        var emptyActiveLogs = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>();
        var action = new EventTableAction.UpdateDisplayedEvents(emptyActiveLogs);

        // Act
        var newState = EventTableReducers.ReduceUpdateDisplayedEvents(state, action);

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
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData1));
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData2));

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

        state = EventTableReducers.ReduceUpdateTable(state, new EventTableAction.UpdateTable(logData1.Id, log1Loaded));
        state = EventTableReducers.ReduceUpdateTable(state, new EventTableAction.UpdateTable(logData2.Id, log2Loaded));

        var log1Filtered = new List<ResolvedEvent>
        {
            new(Constants.LogNameLog1, LogPathType.Channel) { Id = 100, RecordId = 1 }
        };

        var activeLogs = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>
        {
            { logData1.Id, log1Filtered }
        };

        // Act
        var newState = EventTableReducers.ReduceUpdateDisplayedEvents(
            state,
            new EventTableAction.UpdateDisplayedEvents(activeLogs));

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
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData));

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
        var newState = EventTableReducers.ReduceUpdateDisplayedEvents(
            state,
            new EventTableAction.UpdateDisplayedEvents(activeLogs));

        // Assert — UpdateDisplayedEvents also latches ComputerName, not just the append paths
        var updatedTable = newState.EventTables.First(t => t.Id == logData.Id);
        Assert.Equal(Constants.EventComputerServer01, updatedTable.ComputerName);
    }

    [Fact]
    public void ReduceUpdateTable_AfterPartialAppends_ShouldReplaceNotMergeWithPartials()
    {
        // Arrange — partial AppendTableEvents land first (live-load deltas), then UpdateTable arrives with the full filtered list
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var state = new EventTableState { IsDescending = false };
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData));

        var partial1 = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 10, RecordId = 1 },
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 11, RecordId = 2 }
        };

        state = EventTableReducers.ReduceAppendTableEvents(
            state,
            new EventTableAction.AppendTableEvents(logData.Id, partial1));

        var partial2 = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 12, RecordId = 3 }
        };

        state = EventTableReducers.ReduceAppendTableEvents(
            state,
            new EventTableAction.AppendTableEvents(logData.Id, partial2));

        Assert.Equal(3, state.DisplayedEvents.Count);

        var fullLoad = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 20, RecordId = 1 },
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 21, RecordId = 2 },
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 22, RecordId = 3 },
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 23, RecordId = 4 }
        };

        // Act
        state = EventTableReducers.ReduceUpdateTable(
            state,
            new EventTableAction.UpdateTable(logData.Id, fullLoad));

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
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData));

        var events = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(100),
            EventUtils.CreateTestEvent(200)
        };

        var action = new EventTableAction.UpdateTable(logData.Id, events);

        // Act
        var newState = EventTableReducers.ReduceUpdateTable(state, action);

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
        var state = new EventTableState { IsDescending = false };
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData));

        var firstLoad = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 10, RecordId = 1 },
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 11, RecordId = 2 }
        };

        state = EventTableReducers.ReduceUpdateTable(state, new EventTableAction.UpdateTable(logData.Id, firstLoad));
        Assert.Equal(2, state.DisplayedEvents.Count);

        var secondLoad = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 12, RecordId = 3 },
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 13, RecordId = 4 },
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 14, RecordId = 5 }
        };

        // Act — second UpdateTable for the same log (e.g., filter-driven reload)
        state = EventTableReducers.ReduceUpdateTable(state, new EventTableAction.UpdateTable(logData.Id, secondLoad));

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
        var state = new EventTableState { IsDescending = true };
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData1));
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData2));

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
        state = EventTableReducers.ReduceUpdateTable(state, new EventTableAction.UpdateTable(logData1.Id, eventsLog1));
        state = EventTableReducers.ReduceUpdateTable(state, new EventTableAction.UpdateTable(logData2.Id, eventsLog2));

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
        var state = new EventTableState { IsDescending = false };
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData1));
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData2));

        var eventsLog1 = new List<ResolvedEvent>
        {
            new(Constants.LogNameLog1, LogPathType.Channel) { Id = 10, RecordId = 5 },
            new(Constants.LogNameLog1, LogPathType.Channel) { Id = 11, RecordId = 7 }
        };

        // Act
        state = EventTableReducers.ReduceUpdateTable(state, new EventTableAction.UpdateTable(logData1.Id, eventsLog1));
        state = EventTableReducers.ReduceUpdateTable(state, new EventTableAction.UpdateTable(logData2.Id, []));

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
        var state = new EventTableState { IsDescending = false };
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData1));
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData2));

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
        state = EventTableReducers.ReduceUpdateTable(state, new EventTableAction.UpdateTable(logData1.Id, eventsLog1));
        state = EventTableReducers.ReduceUpdateTable(state, new EventTableAction.UpdateTable(logData2.Id, eventsLog2));

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
        var state = new EventTableState();
        var staleLogId = EventLogId.Create();
        var events = new List<ResolvedEvent> { EventUtils.CreateTestEvent(100) };
        var action = new EventTableAction.UpdateTable(staleLogId, events);

        // Act — stale UpdateTable for a non-existent table
        var newState = EventTableReducers.ReduceUpdateTable(state, action);

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
        var state = new EventTableState { IsDescending = false };
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData1));
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData2));

        // Act
        state = EventTableReducers.ReduceUpdateTable(state, new EventTableAction.UpdateTable(logData1.Id, log1Events));
        state = EventTableReducers.ReduceUpdateTable(state, new EventTableAction.UpdateTable(logData2.Id, log2Events));

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
