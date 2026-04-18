// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
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
        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, []);

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
        var events = new List<DisplayEventModel> { EventUtils.CreateTestEvent(100) };

        var activeLogs = new Dictionary<EventLogId, IReadOnlyList<DisplayEventModel>>
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

        var events = new List<DisplayEventModel>
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
    public void EventTableModel_ComputerName_ShouldReturnFirstEventComputer()
    {
        // Arrange
        var events = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(100, computerName: Constants.EventComputerServer01),
            EventUtils.CreateTestEvent(200, computerName: Constants.EventComputerServer02)
        };

        var model = new EventTableModel(EventLogId.Create())
        {
            DisplayedEvents = events
        };

        // Act
        var computerName = model.ComputerName;

        // Assert
        Assert.Equal(Constants.EventComputerServer01, computerName);
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
        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, []);

        // Act - Add table
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData));
        Assert.True(state.EventTables.First().IsLoading);

        // Act - Update table with events
        var events = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(100),
            EventUtils.CreateTestEvent(200)
        };

        state = EventTableReducers.ReduceUpdateTable(state, new EventTableAction.UpdateTable(logData.Id, events));

        // Assert
        Assert.False(state.EventTables.First().IsLoading);
        Assert.Equal(2, state.EventTables.First().DisplayedEvents.Count);
    }

    [Fact]
    public void IntegrationTest_OpenMultipleLogsAndCloseOne()
    {
        // Arrange
        var state = new EventTableState();
        var logData1 = new EventLogData(Constants.LogNameLog1, PathType.LogName, []);
        var logData2 = new EventLogData(Constants.LogNameLog2, PathType.LogName, []);
        var logData3 = new EventLogData(Constants.LogNameLog3, PathType.LogName, []);

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
        var logData1 = new EventLogData(Constants.LogNameLog1, PathType.LogName, []);
        var logData2 = new EventLogData(Constants.LogNameLog2, PathType.LogName, []);
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData1));
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData2));

        var logData3 = new EventLogData(Constants.LogNameLog3, PathType.LogName, []);
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
        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, []);
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
        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, []);
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
        var logData = new EventLogData(Constants.FilePathTestEvtx, PathType.FilePath, []);
        var action = new EventTableAction.AddTable(logData);

        // Act
        var newState = EventTableReducers.ReduceAddTable(state, action);

        // Assert
        Assert.Equal(Constants.FilePathTestEvtx, newState.EventTables.First().FileName);
        Assert.Equal(PathType.FilePath, newState.EventTables.First().PathType);
    }

    [Fact]
    public void ReduceAddTable_WhenFirstTable_WithLogName_ShouldNotSetFileName()
    {
        // Arrange
        var state = new EventTableState();
        var logData = new EventLogData(Constants.LogNameApplication, PathType.LogName, []);
        var action = new EventTableAction.AddTable(logData);

        // Act
        var newState = EventTableReducers.ReduceAddTable(state, action);

        // Assert
        Assert.Null(newState.EventTables.First().FileName);
        Assert.Equal(Constants.LogNameApplication, newState.EventTables.First().LogName);
        Assert.Equal(PathType.LogName, newState.EventTables.First().PathType);
    }

    [Fact]
    public void ReduceAddTable_WhenSecondTable_ShouldCreateCombinedTable()
    {
        // Arrange
        var logData1 = new EventLogData(Constants.LogNameLog1, PathType.LogName, []);
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData1));

        var logData2 = new EventLogData(Constants.LogNameLog2, PathType.LogName, []);
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
        var logData1 = new EventLogData(Constants.LogNameLog1, PathType.LogName, []);
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData1));

        var logData2 = new EventLogData(Constants.LogNameLog2, PathType.LogName, []);
        var action = new EventTableAction.AddTable(logData2);

        // Act
        var newState = EventTableReducers.ReduceAddTable(state, action);

        // Assert
        var combinedTable = newState.EventTables.First(t => t.IsCombined);
        Assert.Equal(combinedTable.Id, newState.ActiveEventLogId);
    }

    [Fact]
    public void ReduceAppendTableEvents_ShouldAppendEventsToExistingDisplayedEvents()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, []);
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData));

        var initialEvents = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(100, recordId: 1),
            EventUtils.CreateTestEvent(200, recordId: 2)
        };

        state = EventTableReducers.ReduceAppendTableEvents(
            state,
            new EventTableAction.AppendTableEvents(logData.Id, initialEvents));

        var deltaEvents = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(300, recordId: 3),
            EventUtils.CreateTestEvent(400, recordId: 4)
        };

        var action = new EventTableAction.AppendTableEvents(logData.Id, deltaEvents);

        // Act
        var newState = EventTableReducers.ReduceAppendTableEvents(state, action);

        // Assert
        var updatedTable = newState.EventTables.First(t => t.Id == logData.Id);
        Assert.Equal(4, updatedTable.DisplayedEvents.Count);
    }

    [Fact]
    public void ReduceAppendTableEvents_ShouldNotChangeIsLoading()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, []);
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData));

        // Table should be in loading state after AddTable
        Assert.True(state.EventTables.First(t => t.Id == logData.Id).IsLoading);

        var action = new EventTableAction.AppendTableEvents(
            logData.Id,
            new List<DisplayEventModel> { EventUtils.CreateTestEvent(100, recordId: 1) });

        // Act
        var newState = EventTableReducers.ReduceAppendTableEvents(state, action);

        // Assert - IsLoading should still be true (partial update doesn't complete loading)
        var updatedTable = newState.EventTables.First(t => t.Id == logData.Id);
        Assert.True(updatedTable.IsLoading);
        Assert.Single(updatedTable.DisplayedEvents);
    }

    [Fact]
    public void ReduceAppendTableEvents_ShouldPreserveSortOrder()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, []);
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData));

        var initialEvents = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(100, recordId: 10),
            EventUtils.CreateTestEvent(200, recordId: 20)
        };

        state = EventTableReducers.ReduceAppendTableEvents(
            state,
            new EventTableAction.AppendTableEvents(logData.Id, initialEvents));

        // Append events with record IDs that should sort between and after existing
        var deltaEvents = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(300, recordId: 5),
            EventUtils.CreateTestEvent(400, recordId: 15)
        };

        var action = new EventTableAction.AppendTableEvents(logData.Id, deltaEvents);

        // Act
        var newState = EventTableReducers.ReduceAppendTableEvents(state, action);

        // Assert - default sort is by RecordId descending (IsDescending defaults to true)
        var displayedEvents = newState.EventTables.First(t => t.Id == logData.Id).DisplayedEvents;
        Assert.Equal(4, displayedEvents.Count);

        var recordIds = displayedEvents.Select(e => e.RecordId).ToList();
        Assert.Equal(recordIds.OrderByDescending(x => x).ToList(), recordIds);
    }

    [Fact]
    public void ReduceAppendTableEvents_WhenTableNotFound_ShouldReturnUnchangedState()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, []);
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData));

        var unknownLogId = EventLogId.Create();

        var action = new EventTableAction.AppendTableEvents(
            unknownLogId,
            new List<DisplayEventModel> { EventUtils.CreateTestEvent(100) });

        // Act
        var newState = EventTableReducers.ReduceAppendTableEvents(state, action);

        // Assert
        Assert.Same(state, newState);
    }

    [Fact]
    public void ReduceCloseAll_ShouldClearAllTables()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, []);
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData));

        // Act
        var newState = EventTableReducers.ReduceCloseAll(state);

        // Assert
        Assert.Empty(newState.EventTables);
        Assert.Null(newState.ActiveEventLogId);
    }

    [Fact]
    public void ReduceCloseLog_WhenLastTable_ShouldClearAll()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, []);
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
        var logData1 = new EventLogData(Constants.LogNameLog1, PathType.LogName, []);
        var logData2 = new EventLogData(Constants.LogNameLog2, PathType.LogName, []);
        var logData3 = new EventLogData(Constants.LogNameLog3, PathType.LogName, []);
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
        var logData1 = new EventLogData(Constants.LogNameLog1, PathType.LogName, []);
        var logData2 = new EventLogData(Constants.LogNameLog2, PathType.LogName, []);
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
        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, []);
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
        var logData1 = new EventLogData(Constants.LogNameLog1, PathType.LogName, []);
        var logData2 = new EventLogData(Constants.LogNameLog2, PathType.LogName, []);
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
        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, []);
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
    public void ReduceUpdateCombinedEvents_WhenAllTablesAreLoading_ShouldReturnSameState()
    {
        // Arrange
        var logData1 = new EventLogData(Constants.LogNameLog1, PathType.LogName, []);
        var logData2 = new EventLogData(Constants.LogNameLog2, PathType.LogName, []);
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData1));
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData2));

        // Act
        var newState = EventTableReducers.ReduceUpdateCombinedEvents(state);

        // Assert
        Assert.Same(state, newState);
    }

    [Fact]
    public void ReduceUpdateCombinedEvents_WhenOneTable_ShouldReturnSameState()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, []);
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData));

        // Act
        var newState = EventTableReducers.ReduceUpdateCombinedEvents(state);

        // Assert
        Assert.Same(state, newState);
    }

    [Fact]
    public void ReduceUpdateDisplayedEvents_ShouldUpdateNonCombinedTables()
    {
        // Arrange
        var logData1 = new EventLogData(Constants.LogNameLog1, PathType.LogName, []);
        var logData2 = new EventLogData(Constants.LogNameLog2, PathType.LogName, []);
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData1));
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData2));

        var events = new List<DisplayEventModel> { EventUtils.CreateTestEvent(100) };

        var activeLogs = new Dictionary<EventLogId, IReadOnlyList<DisplayEventModel>>
        {
            { logData1.Id, events },
            { logData2.Id, events }
        };

        var action = new EventTableAction.UpdateDisplayedEvents(activeLogs);

        // Act
        var newState = EventTableReducers.ReduceUpdateDisplayedEvents(state, action);

        // Assert
        Assert.All(newState.EventTables.Where(t => !t.IsCombined),
            table => Assert.Single(table.DisplayedEvents));
    }

    [Fact]
    public void ReduceUpdateDisplayedEvents_WhenTableNotInActiveLogs_ShouldPreserveTable()
    {
        // Arrange — add a table but provide ActiveLogs that don't include it
        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, []);
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData));

        var emptyActiveLogs = new Dictionary<EventLogId, IReadOnlyList<DisplayEventModel>>();
        var action = new EventTableAction.UpdateDisplayedEvents(emptyActiveLogs);

        // Act — table not in ActiveLogs should be preserved as-is
        var newState = EventTableReducers.ReduceUpdateDisplayedEvents(state, action);

        // Assert — table still exists, events unchanged
        Assert.Single(newState.EventTables);
    }

    [Fact]
    public void ReduceUpdateTable_ShouldUpdateTableEvents()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, []);
        var state = new EventTableState();
        state = EventTableReducers.ReduceAddTable(state, new EventTableAction.AddTable(logData));

        var events = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(100),
            EventUtils.CreateTestEvent(200)
        };

        var action = new EventTableAction.UpdateTable(logData.Id, events);

        // Act
        var newState = EventTableReducers.ReduceUpdateTable(state, action);

        // Assert
        var updatedTable = newState.EventTables.First(t => t.Id == logData.Id);
        Assert.Equal(2, updatedTable.DisplayedEvents.Count);
        Assert.False(updatedTable.IsLoading);
    }

    [Fact]
    public void ReduceUpdateTable_WhenTableNotFound_ShouldReturnStateUnchanged()
    {
        // Arrange — empty state, no tables
        var state = new EventTableState();
        var staleLogId = EventLogId.Create();
        var events = new List<DisplayEventModel> { EventUtils.CreateTestEvent(100) };
        var action = new EventTableAction.UpdateTable(staleLogId, events);

        // Act — stale UpdateTable for a non-existent table
        var newState = EventTableReducers.ReduceUpdateTable(state, action);

        // Assert — state unchanged, no exception thrown
        Assert.Same(state, newState);
    }
}
