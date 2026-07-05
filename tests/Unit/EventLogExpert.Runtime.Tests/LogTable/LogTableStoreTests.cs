// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Filtering.TestUtils.Constants;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Tests.TestUtils.Constants;
using System.Collections.Immutable;
using ApplyFilterAction = EventLogExpert.Runtime.EventLog.ApplyFilterAction;
using CloseLogAction = EventLogExpert.Runtime.LogTable.CloseLogAction;
using Reducers = EventLogExpert.Runtime.LogTable.Reducers;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class LogTableStoreTests
{
    private static readonly ColumnDefaults s_columnDefaults = new();

    [Fact]
    public void DisplayedEvents_WithSingleLog_ShouldResolveAClonedEventToTheLiveIndex()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));
        state = Reducers.ReduceUpdateTable(
            state,
            new UpdateTableAction(logData.Id, new List<ResolvedEvent>
            {
                new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 1, RecordId = 10 },
                new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 2, RecordId = 20 }
            }));

        var displayed = state.DisplayedEvents;
        var live = displayed.First(resolved => resolved.RecordId == 20);
        var clone = new ResolvedEvent(Constants.LogNameTestLog, LogPathType.Channel) { Id = 2, RecordId = 20 };

        // A value-equal clone (e.g. a reloaded selection) resolves to the live instance's index via the single-log
        // fast path, matching CombinedEventView's key-based Rank rather than returning -1.
        Assert.Equal(ResolvedEventIndex.IndexOf(displayed, live), ResolvedEventIndex.IndexOf(displayed, clone));
        Assert.True(ResolvedEventIndex.IndexOf(displayed, clone) >= 0);
    }

    [Fact]
    public void DisplayedEvents_WithSingleLog_ShouldReturnAStableReferenceAcrossReads()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));
        state = Reducers.ReduceAppendTableEvents(
            state,
            new AppendTableEventsAction(logData.Id, new List<ResolvedEvent>
            {
                FilterEventBuilder.CreateTestEvent(100, recordId: 10)
            }));

        // LogTablePane skips re-indexing while DisplayedEvents is reference-stable, so the fast path must
        // return the same instance across reads when PerLogEvents is unchanged.
        Assert.Same(state.DisplayedEvents, state.DisplayedEvents);
    }

    [Fact]
    public void DisplayedEvents_WithSingleLog_ShouldServeThePerLogListDirectly()
    {
        // Arrange - one open log with events.
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));
        state = Reducers.ReduceAppendTableEvents(
            state,
            new AppendTableEventsAction(logData.Id, new List<ResolvedEvent>
            {
                FilterEventBuilder.CreateTestEvent(100, recordId: 10),
                FilterEventBuilder.CreateTestEvent(200, recordId: 20)
            }));

        // Assert - the single-log fast path returns the per-log list itself (same reference as EventsForLog),
        // bypassing the CombinedEventView merge wrapper used for multiple logs.
        Assert.Same(state.EventsForLog(logData.Id), state.DisplayedEvents);
    }

    [Fact]
    public void DisplayedEvents_WithTwoLogs_ShouldServeCombinedViewNotAPerLogList()
    {
        // Arrange - two open logs. Both tables are added before either is populated so that every
        // per-log list is built under the same (multi-log) sort context, matching the real load flow.
        var first = new EventLogData(Constants.LogNameLog1, LogPathType.Channel);
        var second = new EventLogData(Constants.LogNameLog2, LogPathType.Channel);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(first));
        state = Reducers.ReduceAddTable(state, new AddTableAction(second));
        state = Reducers.ReduceUpdateTable(
            state,
            new UpdateTableAction(first.Id, new List<ResolvedEvent>
            {
                new(Constants.LogNameLog1, LogPathType.Channel) { Id = 100, RecordId = 10 }
            }));
        state = Reducers.ReduceUpdateTable(
            state,
            new UpdateTableAction(second.Id, new List<ResolvedEvent>
            {
                new(Constants.LogNameLog2, LogPathType.Channel) { Id = 200, RecordId = 20 }
            }));

        // Assert - with multiple logs the merged view is served, distinct from either per-log list.
        Assert.NotSame(state.EventsForLog(first.Id), state.DisplayedEvents);
        Assert.NotSame(state.EventsForLog(second.Id), state.DisplayedEvents);
        Assert.Equal(2, state.DisplayedEvents.Count);
    }

    [Fact]
    public void EventTableAction_AddTable_ShouldStoreLogData()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);

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
    public void EventTableAction_DisplayReady_ShouldStoreLists()
    {
        // Arrange
        var logId = EventLogId.Create();
        var events = new List<ResolvedEvent> { FilterEventBuilder.CreateTestEvent(100) };

        var lists = new Dictionary<EventLogId, SegmentedSortedList>
        {
            { logId, SegmentedSortedList.CreateSorted(events, new SortContext(null, true, null, false)) }
        };

        // Act
        var action = new DisplayReadyAction { Lists = lists };

        // Assert
        Assert.Single(action.Lists);
        Assert.True(action.Lists.ContainsKey(logId));
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
    public void EventTableAction_UpdateTable_ShouldStoreLogIdAndEvents()
    {
        // Arrange
        var logId = EventLogId.Create();

        var events = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(100),
            FilterEventBuilder.CreateTestEvent(200)
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
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);

        // Act - Add table
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));
        Assert.True(state.EventTables.First().IsLoading);

        // Act - Update table with events
        var events = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(100),
            FilterEventBuilder.CreateTestEvent(200)
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
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel);
        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel);
        var logData3 = new EventLogData(Constants.LogNameLog3, LogPathType.Channel);

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

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void IsGroupCollapsed_XorsDefaultAndOverride(bool collapsedByDefault, bool hasOverride, bool expected)
    {
        var overrides = hasOverride ? ImmutableHashSet.Create("g") : ImmutableHashSet<string>.Empty;
        var state = new LogTableState
        {
            GroupsCollapsedByDefault = collapsedByDefault,
            GroupCollapseOverrides = overrides
        };

        Assert.Equal(expected, state.IsGroupCollapsed("g"));
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
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));

        var firstBatch = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 10, RecordId = 1, ComputerName = FilterTestConstants.EventComputerServer01 }
        };

        state = Reducers.ReduceAppendTableEvents(
            state,
            new AppendTableEventsAction(logData.Id, firstBatch));

        var secondBatch = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 11, RecordId = 2, ComputerName = FilterTestConstants.EventComputerServer02 }
        };

        // Act - second batch with a different ComputerName must not overwrite the latched value
        state = Reducers.ReduceAppendTableEvents(
            state,
            new AppendTableEventsAction(logData.Id, secondBatch));

        // Assert - ComputerName latches to the first non-empty observed value
        var table = state.EventTables.First(t => t.Id == logData.Id);
        Assert.Equal(FilterTestConstants.EventComputerServer01, table.ComputerName);
    }

    [Fact]
    public void LogView_ComputerName_WhenFirstEventHasEmptyComputerName_ShouldUseLaterEventInBatch()
    {
        // Arrange - first event has an empty ComputerName (resolver miss); second carries the name
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));

        var batch = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 10, RecordId = 1, ComputerName = string.Empty },
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 11, RecordId = 2, ComputerName = FilterTestConstants.EventComputerServer01 }
        };

        // Act
        state = Reducers.ReduceAppendTableEvents(
            state,
            new AppendTableEventsAction(logData.Id, batch));

        // Assert - reducer scans past the empty leading event and latches the first non-empty value
        var table = state.EventTables.First(t => t.Id == logData.Id);
        Assert.Equal(FilterTestConstants.EventComputerServer01, table.ComputerName);
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
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel);
        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData1));
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData2));

        var logData3 = new EventLogData(Constants.LogNameLog3, LogPathType.Channel);
        var action = new AddTableAction(logData3);

        // Act
        var newState = Reducers.ReduceAddTable(state, action);

        // Assert
        Assert.Equal(4, newState.EventTables.Count);
        Assert.Single(newState.EventTables, t => t.IsCombined);
    }

    [Fact]
    public void ReduceAddTable_WhenFirstLogSetsActive_ResetsCollapse()
    {
        var state = new LogTableState
        {
            GroupsCollapsedByDefault = true,
            GroupCollapseOverrides = ImmutableHashSet.Create("g")
        };

        var result = Reducers.ReduceAddTable(
            state,
            new AddTableAction(new EventLogData("Application", LogPathType.Channel)));

        Assert.NotNull(result.ActiveEventLogId);
        Assert.False(result.GroupsCollapsedByDefault);
        Assert.Empty(result.GroupCollapseOverrides);
    }

    [Fact]
    public void ReduceAddTable_WhenFirstTable_ShouldBeLoading()
    {
        // Arrange
        var state = new LogTableState();
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
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
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
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
        var logData = new EventLogData(Constants.FilePathTestEvtx, LogPathType.File);
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
        var logData = new EventLogData(Constants.LogNameApplication, LogPathType.Channel);
        var action = new AddTableAction(logData);

        // Act
        var newState = Reducers.ReduceAddTable(state, action);

        // Assert
        Assert.Null(newState.EventTables.First().FileName);
        Assert.Equal(Constants.LogNameApplication, newState.EventTables.First().LogName);
        Assert.Equal(LogPathType.Channel, newState.EventTables.First().LogPathType);
    }

    [Fact]
    public void ReduceAddTable_WhenSecondLogSwitchesToCombined_ResetsCollapse()
    {
        var state = Reducers.ReduceAddTable(
            new LogTableState(),
            new AddTableAction(new EventLogData("First", LogPathType.Channel)));
        state = state with
        {
            GroupsCollapsedByDefault = true,
            GroupCollapseOverrides = ImmutableHashSet.Create("g")
        };

        var result = Reducers.ReduceAddTable(
            state,
            new AddTableAction(new EventLogData("Second", LogPathType.Channel)));

        Assert.Contains(result.EventTables, t => t.IsCombined);
        Assert.Equal(result.EventTables.First(t => t.IsCombined).Id, result.ActiveEventLogId);
        Assert.False(result.GroupsCollapsedByDefault);
        Assert.Empty(result.GroupCollapseOverrides);
    }

    [Fact]
    public void ReduceAddTable_WhenSecondTable_ShouldCreateCombinedTable()
    {
        // Arrange
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData1));

        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel);
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
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData1));

        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel);
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
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));

        var initialEvents = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(100, recordId: 1),
            FilterEventBuilder.CreateTestEvent(200, recordId: 2)
        };

        state = Reducers.ReduceAppendTableEvents(
            state,
            new AppendTableEventsAction(logData.Id, initialEvents));

        var deltaEvents = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(300, recordId: 3),
            FilterEventBuilder.CreateTestEvent(400, recordId: 4)
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
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));

        // Table should be in loading state after AddTable
        Assert.True(state.EventTables.First(t => t.Id == logData.Id).IsLoading);

        var action = new AppendTableEventsAction(
            logData.Id,
            new List<ResolvedEvent> { FilterEventBuilder.CreateTestEvent(100, recordId: 1) });

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
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));

        var initialEvents = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(100, recordId: 10),
            FilterEventBuilder.CreateTestEvent(200, recordId: 20)
        };

        state = Reducers.ReduceAppendTableEvents(
            state,
            new AppendTableEventsAction(logData.Id, initialEvents));

        // Append events with record IDs that should sort between and after existing
        var deltaEvents = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(300, recordId: 5),
            FilterEventBuilder.CreateTestEvent(400, recordId: 15)
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
    public void ReduceAppendTableEvents_WhenGrouped_MergesIntoExistingAndNewGroups()
    {
        var baseTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var table = new LogView(EventLogId.Create()) { LogName = "Application" };
        var state = new LogTableState
        {
            EventTables = ImmutableList.Create(table),
            ActiveEventLogId = table.Id,
            GroupBy = ColumnName.Source,
            IsDescending = false,
            EventCountByLog = ImmutableDictionary<EventLogId, int>.Empty.Add(table.Id, 0)
        };

        IReadOnlyList<ResolvedEvent> batch1 =
        [
            FilterEventBuilder.CreateTestEvent(id: 100, source: "A", timeCreated: baseTime.AddSeconds(1)),
            FilterEventBuilder.CreateTestEvent(id: 200, source: "B", timeCreated: baseTime.AddSeconds(2))
        ];
        state = Reducers.ReduceAppendTableEvents(state, new AppendTableEventsAction(table.Id, batch1));

        IReadOnlyList<ResolvedEvent> batch2 =
        [
            FilterEventBuilder.CreateTestEvent(id: 300, source: "A", timeCreated: baseTime.AddSeconds(3)),
            FilterEventBuilder.CreateTestEvent(id: 500, source: "C", timeCreated: baseTime.AddSeconds(5))
        ];
        var result = Reducers.ReduceAppendTableEvents(state, new AppendTableEventsAction(table.Id, batch2));

        Assert.Equal(new[] { "A", "A", "B", "C" }, result.DisplayedEvents.Select(e => e.Source));
        Assert.Equal(new[] { 100, 300, 200, 500 }, result.DisplayedEvents.Select(e => e.Id));
    }

    [Fact]
    public void ReduceAppendTableEvents_WhenTableNotFound_ShouldReturnUnchangedState()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));

        var unknownLogId = EventLogId.Create();

        var action = new AppendTableEventsAction(
            unknownLogId,
            new List<ResolvedEvent> { FilterEventBuilder.CreateTestEvent(100) });

        // Act
        var newState = Reducers.ReduceAppendTableEvents(state, action);

        // Assert
        Assert.Same(state, newState);
    }

    [Fact]
    public void ReduceAppendTableEventsBatch_WhenBatchTargetsClosedLog_ShouldSkipThatBatch()
    {
        // Arrange - open log plus a stale log id whose tab no longer exists (race: closed mid-flight)
        var openLog = new EventLogData(Constants.LogNameLog1, LogPathType.Channel);
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

        // Assert - stale batch is skipped; canonical and EventCountByLog only reflect the open log
        Assert.Single(newState.DisplayedEvents);
        Assert.Equal(1L, newState.DisplayedEvents[0].RecordId);
        Assert.False(newState.EventCountByLog.ContainsKey(staleLogId));
        Assert.Equal(1, newState.EventCountByLog[openLog.Id]);
    }

    [Fact]
    public void ReduceAppendTableEventsBatch_WhenGrouped_MergesIntoExistingGroups()
    {
        var baseTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var table = new LogView(EventLogId.Create()) { LogName = "Application" };
        var state = new LogTableState
        {
            EventTables = ImmutableList.Create(table),
            ActiveEventLogId = table.Id,
            GroupBy = ColumnName.Source,
            IsDescending = false,
            EventCountByLog = ImmutableDictionary<EventLogId, int>.Empty.Add(table.Id, 0)
        };

        state = Reducers.ReduceAppendTableEvents(
            state,
            new AppendTableEventsAction(table.Id,
            [
                FilterEventBuilder.CreateTestEvent(id: 100, source: "A", timeCreated: baseTime.AddSeconds(1)),
                FilterEventBuilder.CreateTestEvent(id: 200, source: "B", timeCreated: baseTime.AddSeconds(2))
            ]));

        var byLog = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>
        {
            [table.Id] =
            [
                FilterEventBuilder.CreateTestEvent(id: 300, source: "A", timeCreated: baseTime.AddSeconds(3)),
                FilterEventBuilder.CreateTestEvent(id: 500, source: "C", timeCreated: baseTime.AddSeconds(5))
            ]
        };

        var result = Reducers.ReduceAppendTableEventsBatch(state, new AppendTableEventsBatchAction(byLog));

        Assert.Equal(new[] { "A", "A", "B", "C" }, result.DisplayedEvents.Select(e => e.Source));
        Assert.Equal(new[] { 100, 300, 200, 500 }, result.DisplayedEvents.Select(e => e.Id));
    }

    [Fact]
    public void ReduceApplyFilter_BumpsVersionAndPreservesRequestedSort()
    {
        var state = new LogTableState
        {
            RequestedOrderBy = ColumnName.Source,
            RequestedGroupBy = ColumnName.EventId,
            RequestedIsDescending = false,
            RequestedIsGroupDescending = true,
            DisplayListVersion = 3
        };

        var result = Reducers.ReduceApplyFilter(
            state,
            new ApplyFilterAction(new Filter(null, [])));

        Assert.Equal(4, result.DisplayListVersion);
        Assert.Equal(ColumnName.Source, result.RequestedOrderBy);
        Assert.Equal(ColumnName.EventId, result.RequestedGroupBy);
        Assert.False(result.RequestedIsDescending);
        Assert.True(result.RequestedIsGroupDescending);
    }

    [Fact]
    public void ReduceCloseAll_ResetsCollapse()
    {
        var table = new LogView(EventLogId.Create()) { LogName = "A" };
        var state = new LogTableState
        {
            EventTables = ImmutableList.Create(table),
            ActiveEventLogId = table.Id,
            GroupsCollapsedByDefault = true,
            GroupCollapseOverrides = ImmutableHashSet.Create("g")
        };

        var result = Reducers.ReduceCloseAll(state);

        Assert.Null(result.ActiveEventLogId);
        Assert.False(result.GroupsCollapsedByDefault);
        Assert.Empty(result.GroupCollapseOverrides);
    }

    [Fact]
    public void ReduceCloseLog_WhenActiveChanges_ResetsCollapse()
    {
        var tableA = new LogView(EventLogId.Create()) { LogName = "A" };
        var tableB = new LogView(EventLogId.Create()) { LogName = "B" };
        var combined = new LogView(EventLogId.Create()) { GroupId = LogTabGroupId.AllLogs };
        var state = new LogTableState
        {
            EventTables = ImmutableList.Create(combined, tableA, tableB),
            ActiveEventLogId = combined.Id,
            EventCountByLog = ImmutableDictionary<EventLogId, int>.Empty
                .Add(tableA.Id, 0)
                .Add(tableB.Id, 0),
            GroupsCollapsedByDefault = true,
            GroupCollapseOverrides = ImmutableHashSet.Create("g")
        };

        var result = Reducers.ReduceCloseLog(state, new CloseLogAction(tableB.Id));

        Assert.Equal(tableA.Id, result.ActiveEventLogId);
        Assert.False(result.GroupsCollapsedByDefault);
        Assert.Empty(result.GroupCollapseOverrides);
    }

    [Fact]
    public void ReduceDisplayReady_WhenFilterSupersedesSort_DropsSortRebuildAndAppliesLatest()
    {
        var seeded = SeedTabled(new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(id: 1, source: "A"),
            FilterEventBuilder.CreateTestEvent(id: 2, source: "B")
        });

        var afterSort = Reducers.ReduceSetOrderBy(seeded, new SetOrderByAction(ColumnName.Source));
        var afterFilter = Reducers.ReduceApplyFilter(
            afterSort,
            new ApplyFilterAction(new Filter(null, [])));

        var lists = new Dictionary<EventLogId, SegmentedSortedList>(afterFilter.PerLogEvents.Count);

        foreach (var (logId, list) in afterFilter.PerLogEvents)
        {
            lists[logId] = SegmentedSortedList.CreateSorted(list, afterFilter.SortContext);
        }

        var droppedSortRebuild = Reducers.ReduceDisplayReady(
            afterFilter,
            new DisplayReadyAction { Lists = lists, Version = afterSort.DisplayListVersion });

        Assert.Same(afterFilter, droppedSortRebuild);
        Assert.Null(droppedSortRebuild.OrderBy);

        var appliedFilterRebuild = Reducers.ReduceDisplayReady(
            afterFilter,
            new DisplayReadyAction { Lists = lists, Version = afterFilter.DisplayListVersion });

        Assert.Equal(ColumnName.Source, appliedFilterRebuild.OrderBy);
        Assert.Equal(ColumnName.Source, appliedFilterRebuild.RequestedOrderBy);
    }

    [Fact]
    public void ReduceDisplayReady_WhenLogIsNotInLists_ShouldPreserveExistingCanonicalRows()
    {
        // Arrange - log A has rows in canonical (live-load via UpdateTable); a filter dispatch
        // then arrives with empty Lists (e.g. log opened mid-filter so the snapshot did
        // not include it). The omitted log's rows must stay in canonical - otherwise a fresh
        // load could be silently scrubbed by a stale filter result.
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));

        var loadedEvents = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 10, RecordId = 1 },
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 11, RecordId = 2 }
        };

        state = Reducers.ReduceUpdateTable(state, new UpdateTableAction(logData.Id, loadedEvents));

        var emptyLists = new Dictionary<EventLogId, SegmentedSortedList>();
        var action = new DisplayReadyAction { Lists = emptyLists };

        // Act
        var newState = Reducers.ReduceDisplayReady(state, action);

        // Assert - table still exists, canonical rows preserved, count map preserved.
        Assert.Single(newState.EventTables);
        Assert.Equal(2, newState.DisplayedEvents.Count);
        Assert.Equal(2, newState.EventCountByLog[logData.Id]);
    }

    [Fact]
    public void ReduceDisplayReady_WhenPrebuiltContextMatchesLiveContext_ShouldSwapReferenceWithoutResorting()
    {
        var logData = new EventLogData(Constants.LogNameLog1, LogPathType.Channel);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));

        var events = new List<ResolvedEvent>
        {
            new(Constants.LogNameLog1, LogPathType.Channel) { Id = 10, RecordId = 1 },
            new(Constants.LogNameLog1, LogPathType.Channel) { Id = 20, RecordId = 2 },
            new(Constants.LogNameLog1, LogPathType.Channel) { Id = 30, RecordId = 3 }
        };

        var prebuilt = SegmentedSortedList.CreateSorted(events, new SortContext(null, true, null, false));

        var lists = new Dictionary<EventLogId, SegmentedSortedList> { { logData.Id, prebuilt } };

        var after = Reducers.ReduceDisplayReady(state, new DisplayReadyAction { Lists = lists });

        Assert.Same(prebuilt, after.PerLogEvents[logData.Id]);
        Assert.Equal(new[] { 30, 20, 10 }, after.DisplayedEvents.Select(e => e.Id).ToArray());
    }

    [Fact]
    public void ReduceDisplayReady_WhenPrebuiltContextStale_ShouldHealToLiveContext()
    {
        var logData = new EventLogData(Constants.LogNameLog1, LogPathType.Channel);
        var state = new LogTableState
        {
            OrderBy = ColumnName.DateAndTime,
            IsDescending = true,
            RequestedOrderBy = ColumnName.DateAndTime,
            RequestedIsDescending = true
        };
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));

        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var events = new List<ResolvedEvent>
        {
            new(Constants.LogNameLog1, LogPathType.Channel) { Id = 10, RecordId = 1, TimeCreated = baseTime.AddMinutes(1) },
            new(Constants.LogNameLog1, LogPathType.Channel) { Id = 20, RecordId = 2, TimeCreated = baseTime.AddMinutes(2) },
            new(Constants.LogNameLog1, LogPathType.Channel) { Id = 30, RecordId = 3, TimeCreated = baseTime.AddMinutes(3) }
        };

        var prebuilt = SegmentedSortedList.CreateSorted(events, new SortContext(ColumnName.DateAndTime, false, null, false));

        var lists = new Dictionary<EventLogId, SegmentedSortedList> { { logData.Id, prebuilt } };

        var after = Reducers.ReduceDisplayReady(state, new DisplayReadyAction { Lists = lists });

        Assert.NotSame(prebuilt, after.PerLogEvents[logData.Id]);
        Assert.Equal(3, after.PerLogEvents[logData.Id].Count);
        Assert.Equal(new[] { 30, 20, 10 }, after.DisplayedEvents.Select(e => e.Id).ToArray());
    }

    [Fact]
    public void ReduceDisplayReady_WhenRequestedDiffersFromLive_ShouldSwapReferenceAndFlipLiveAtomically()
    {
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var events = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(id: 1, source: "B", timeCreated: baseTime.AddMinutes(2)),
            FilterEventBuilder.CreateTestEvent(id: 2, source: "A", timeCreated: baseTime.AddMinutes(1))
        };

        var seeded = SeedTabled(events, orderBy: ColumnName.DateAndTime, isDescending: true);
        var requested = seeded with
        {
            RequestedOrderBy = ColumnName.Source,
            RequestedIsDescending = false,
            DisplayListVersion = seeded.DisplayListVersion + 1
        };

        var logId = requested.PerLogEvents.Keys.Single();
        var prebuilt = SegmentedSortedList.CreateSorted(events, requested.SortContext);
        var lists = new Dictionary<EventLogId, SegmentedSortedList> { { logId, prebuilt } };

        var after = Reducers.ReduceDisplayReady(
            requested,
            new DisplayReadyAction { Lists = lists, Version = requested.DisplayListVersion });

        Assert.Same(prebuilt, after.PerLogEvents[logId]);
        Assert.Equal(ColumnName.Source, after.OrderBy);
        Assert.False(after.IsDescending);
        Assert.Equal(new[] { "A", "B" }, after.DisplayedEvents.Select(e => e.Source));
    }

    [Fact]
    public void ReduceDisplayReady_WhenRequestedGroupByDiffers_ShouldResetCollapseState()
    {
        var events = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(id: 1, source: "A"),
            FilterEventBuilder.CreateTestEvent(id: 2, source: "B")
        };

        var seeded = SeedTabled(events, collapseOverrides: ImmutableHashSet.Create("stale"));
        var requested = seeded with
        {
            RequestedGroupBy = ColumnName.Source,
            DisplayListVersion = seeded.DisplayListVersion + 1,
            GroupsCollapsedByDefault = true
        };

        var logId = requested.PerLogEvents.Keys.Single();
        var prebuilt = SegmentedSortedList.CreateSorted(events, requested.SortContext);
        var lists = new Dictionary<EventLogId, SegmentedSortedList> { { logId, prebuilt } };

        var after = Reducers.ReduceDisplayReady(
            requested,
            new DisplayReadyAction { Lists = lists, Version = requested.DisplayListVersion });

        Assert.Equal(ColumnName.Source, after.GroupBy);
        Assert.False(after.GroupsCollapsedByDefault);
        Assert.Empty(after.GroupCollapseOverrides);
    }

    [Fact]
    public void ReduceDisplayReady_WhenSecondLogOpensAcrossTheGap_HealsOneLogListToTwoLogDefault()
    {
        var first = new EventLogData(Constants.LogNameLog1, LogPathType.Channel);
        var second = new EventLogData(Constants.LogNameLog2, LogPathType.Channel);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(first));
        state = Reducers.ReduceAddTable(state, new AddTableAction(second));

        state = Reducers.ReduceUpdateTable(
            state,
            new UpdateTableAction(second.Id, new List<ResolvedEvent>
            {
                new(Constants.LogNameLog2, LogPathType.Channel) { Id = 200, RecordId = 20 }
            }));

        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var firstEvents = new List<ResolvedEvent>
        {
            new(Constants.LogNameLog1, LogPathType.Channel) { Id = 10, RecordId = 1, TimeCreated = baseTime.AddMinutes(3) },
            new(Constants.LogNameLog1, LogPathType.Channel) { Id = 20, RecordId = 2, TimeCreated = baseTime.AddMinutes(2) },
            new(Constants.LogNameLog1, LogPathType.Channel) { Id = 30, RecordId = 3, TimeCreated = baseTime.AddMinutes(1) }
        };

        var prebuiltOneLog = SegmentedSortedList.CreateSorted(firstEvents, new SortContext(null, true, null, false));
        Assert.Equal(new[] { 30, 20, 10 }, prebuiltOneLog.Select(e => e.Id).ToArray());

        var lists = new Dictionary<EventLogId, SegmentedSortedList> { { first.Id, prebuiltOneLog } };

        var after = Reducers.ReduceDisplayReady(
            state, new DisplayReadyAction { Lists = lists, Version = state.DisplayListVersion });

        var healed = after.PerLogEvents[first.Id];
        Assert.NotSame(prebuiltOneLog, healed);
        Assert.True(healed.HasContext(new SortContext(ColumnName.DateAndTime, true, null, false)));
        Assert.Equal(new[] { 10, 20, 30 }, healed.Select(e => e.Id).ToArray());
    }

    [Fact]
    public void ReduceDisplayReady_WhenSomeLogsOmitted_ShouldReplaceIncludedAndPreserveOmitted()
    {
        // Arrange - two logs, both populated via UpdateTable. Filter dispatch arrives for log A
        // only (e.g. log B opened or finished loading after the snapshot). Log A's rows must be
        // replaced by the filter result; log B's rows must be preserved untouched.
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel);
        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel);
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

        var lists = new Dictionary<EventLogId, SegmentedSortedList>
        {
            { logData1.Id, SegmentedSortedList.CreateSorted(log1Filtered, state.SortContext) }
        };

        // Act
        var newState = Reducers.ReduceDisplayReady(state, new DisplayReadyAction { Lists = lists });

        // Assert - log A reduced from 2 to 1, log B's 3 rows untouched; counts reflect both.
        Assert.Equal(4, newState.DisplayedEvents.Count);
        Assert.Equal(1, newState.DisplayedEvents.Count(e => e.OwningLog == Constants.LogNameLog1));
        Assert.Equal(3, newState.DisplayedEvents.Count(e => e.OwningLog == Constants.LogNameLog2));
        Assert.Equal(1, newState.EventCountByLog[logData1.Id]);
        Assert.Equal(3, newState.EventCountByLog[logData2.Id]);
    }

    [Fact]
    public void ReduceDisplayReady_WhenTableComputerNameEmpty_ShouldLatchFromFirstNonEmptyEvent()
    {
        // Arrange - log first becomes visible via DisplayReady (filter clear), not via append
        var logData = new EventLogData(Constants.LogNameLog1, LogPathType.Channel);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));

        var revealedEvents = new List<ResolvedEvent>
        {
            new(Constants.LogNameLog1, LogPathType.Channel) { Id = 10, RecordId = 1, ComputerName = string.Empty },
            new(Constants.LogNameLog1, LogPathType.Channel) { Id = 11, RecordId = 2, ComputerName = FilterTestConstants.EventComputerServer01 }
        };

        var lists = new Dictionary<EventLogId, SegmentedSortedList>
        {
            { logData.Id, SegmentedSortedList.CreateSorted(revealedEvents, state.SortContext) }
        };

        // Act
        var newState = Reducers.ReduceDisplayReady(state, new DisplayReadyAction { Lists = lists });

        // Assert - DisplayReady also latches ComputerName, not just the append paths
        var updatedTable = newState.EventTables.First(t => t.Id == logData.Id);
        Assert.Equal(FilterTestConstants.EventComputerServer01, updatedTable.ComputerName);
    }

    [Fact]
    public void ReduceDisplayReady_WhenVersionStale_ShouldDropAndWhenMatching_ShouldApply()
    {
        var requested = Reducers.ReduceSetOrderBy(
            SeedTabled(new List<ResolvedEvent>
            {
                FilterEventBuilder.CreateTestEvent(id: 1, source: "A"),
                FilterEventBuilder.CreateTestEvent(id: 2, source: "B")
            }),
            new SetOrderByAction(ColumnName.Source));

        var lists = new Dictionary<EventLogId, SegmentedSortedList>(requested.PerLogEvents.Count);

        foreach (var (logId, list) in requested.PerLogEvents)
        {
            lists[logId] = SegmentedSortedList.CreateSorted(list, requested.SortContext);
        }

        var stale = Reducers.ReduceDisplayReady(
            requested,
            new DisplayReadyAction { Lists = lists, Version = requested.DisplayListVersion - 1 });

        Assert.Same(requested, stale);
        Assert.Null(stale.OrderBy);

        var applied = Reducers.ReduceDisplayReady(
            requested,
            new DisplayReadyAction { Lists = lists, Version = requested.DisplayListVersion });

        Assert.Equal(ColumnName.Source, applied.OrderBy);
    }

    [Fact]
    public void ReduceLoadColumnsCompleted_WhenGroupColumnHidden_ClearsGroupAndResortsUngrouped()
    {
        var state = new LogTableState
        {
            GroupBy = ColumnName.Source,
            IsGroupDescending = true,
            GroupCollapseOverrides = ImmutableHashSet.Create("g"),
            OrderBy = ColumnName.EventId,
            IsDescending = false,
            PerLogEvents = BuildPerLog(
                new List<ResolvedEvent>
                {
                    FilterEventBuilder.CreateTestEvent(id: 2, source: "B"),
                    FilterEventBuilder.CreateTestEvent(id: 1, source: "A")
                },
                [],
                orderBy: ColumnName.EventId, isDescending: false, groupBy: ColumnName.Source, isGroupDescending: true)
        };

        var hiddenColumns = ImmutableDictionary<ColumnName, bool>.Empty
            .Add(ColumnName.Source, false)
            .Add(ColumnName.EventId, true);

        var result = Reducers.ReduceLoadColumnsCompleted(
            state,
            new LoadColumnsCompletedAction(hiddenColumns, ImmutableDictionary<ColumnName, int>.Empty, []));

        Assert.Null(result.GroupBy);
        Assert.False(result.IsGroupDescending);
        Assert.Empty(result.GroupCollapseOverrides);
        Assert.Equal(new[] { 1, 2 }, result.DisplayedEvents.Select(e => e.Id));
    }

    [Fact]
    public void ReduceLoadColumnsCompleted_WhenGroupColumnStaysVisible_KeepsGroup()
    {
        var state = new LogTableState { GroupBy = ColumnName.Source };

        var visibleColumns = ImmutableDictionary<ColumnName, bool>.Empty
            .Add(ColumnName.Source, true)
            .Add(ColumnName.Level, false);

        var result = Reducers.ReduceLoadColumnsCompleted(
            state,
            new LoadColumnsCompletedAction(visibleColumns, ImmutableDictionary<ColumnName, int>.Empty, []));

        Assert.Equal(ColumnName.Source, result.GroupBy);
    }

    [Fact]
    public void ReduceLoadColumnsCompleted_WhenLiveAndRequestedGroupBothHidden_ClearsBoth()
    {
        var state = new LogTableState
        {
            GroupBy = ColumnName.Source,
            IsGroupDescending = true,
            RequestedGroupBy = ColumnName.Source,
            RequestedIsGroupDescending = true
        };

        var columns = ImmutableDictionary<ColumnName, bool>.Empty
            .Add(ColumnName.Source, false)
            .Add(ColumnName.Level, true);

        var result = Reducers.ReduceLoadColumnsCompleted(
            state,
            new LoadColumnsCompletedAction(columns, ImmutableDictionary<ColumnName, int>.Empty, []));

        Assert.Null(result.GroupBy);
        Assert.False(result.IsGroupDescending);
        Assert.Null(result.RequestedGroupBy);
        Assert.False(result.RequestedIsGroupDescending);
    }

    [Fact]
    public void ReduceLoadColumnsCompleted_WhenLiveGroupHiddenButRequestedGroupVisible_PreservesPendingRegroup()
    {
        var state = new LogTableState
        {
            GroupBy = ColumnName.Source,
            RequestedGroupBy = ColumnName.Level
        };

        var columns = ImmutableDictionary<ColumnName, bool>.Empty
            .Add(ColumnName.Source, false)
            .Add(ColumnName.Level, true);

        var result = Reducers.ReduceLoadColumnsCompleted(
            state,
            new LoadColumnsCompletedAction(columns, ImmutableDictionary<ColumnName, int>.Empty, []));

        Assert.Null(result.GroupBy);
        Assert.Equal(ColumnName.Level, result.RequestedGroupBy);
    }

    [Fact]
    public void ReduceLoadColumnsCompleted_WhenRequestedGroupHiddenWhileLiveUngrouped_ClearsPendingRequest()
    {
        var state = new LogTableState
        {
            GroupBy = null,
            RequestedGroupBy = ColumnName.Source,
            RequestedIsGroupDescending = true
        };

        var columns = ImmutableDictionary<ColumnName, bool>.Empty
            .Add(ColumnName.Source, false)
            .Add(ColumnName.Level, true);

        var result = Reducers.ReduceLoadColumnsCompleted(
            state,
            new LoadColumnsCompletedAction(columns, ImmutableDictionary<ColumnName, int>.Empty, []));

        Assert.Null(result.RequestedGroupBy);
        Assert.False(result.RequestedIsGroupDescending);
        Assert.Null(result.GroupBy);
    }

    [Fact]
    public void ReduceLoadColumnsCompleted_WhenResetDefaultsHidesGroupColumn_ClearsGroup()
    {
        var state = new LogTableState { GroupBy = ColumnName.ActivityId };

        var defaults = ImmutableDictionary<ColumnName, bool>.Empty
            .Add(ColumnName.Level, true)
            .Add(ColumnName.DateAndTime, true);

        var result = Reducers.ReduceLoadColumnsCompleted(
            state,
            new LoadColumnsCompletedAction(defaults, ImmutableDictionary<ColumnName, int>.Empty, []));

        Assert.Null(result.GroupBy);
    }

    [Fact]
    public void ReduceSetActiveTable_WhenActiveChanges_ResetsCollapse()
    {
        var tableA = new LogView(EventLogId.Create()) { LogName = "A" };
        var tableB = new LogView(EventLogId.Create()) { LogName = "B" };
        var state = new LogTableState
        {
            EventTables = ImmutableList.Create(tableA, tableB),
            ActiveEventLogId = tableA.Id,
            GroupsCollapsedByDefault = true,
            GroupCollapseOverrides = ImmutableHashSet.Create("g")
        };

        var result = Reducers.ReduceSetActiveTable(state, new SetActiveTableAction(tableB.Id));

        Assert.Equal(tableB.Id, result.ActiveEventLogId);
        Assert.False(result.GroupsCollapsedByDefault);
        Assert.Empty(result.GroupCollapseOverrides);
    }

    [Fact]
    public void ReduceSetActiveTable_WhenActiveUnchanged_PreservesCollapse()
    {
        var tableA = new LogView(EventLogId.Create()) { LogName = "A" };
        var state = new LogTableState
        {
            EventTables = ImmutableList.Create(tableA),
            ActiveEventLogId = tableA.Id,
            GroupCollapseOverrides = ImmutableHashSet.Create("g")
        };

        var result = Reducers.ReduceSetActiveTable(state, new SetActiveTableAction(tableA.Id));

        Assert.Contains("g", result.GroupCollapseOverrides);
    }

    [Fact]
    public void ReduceSetAllGroupsCollapsed_SetsDefaultAndClearsOverrides()
    {
        var state = new LogTableState { GroupBy = ColumnName.Source, GroupCollapseOverrides = ImmutableHashSet.Create("x") };

        var result = Reducers.ReduceSetAllGroupsCollapsed(state, new SetAllGroupsCollapsedAction(true));

        Assert.True(result.GroupsCollapsedByDefault);
        Assert.Empty(result.GroupCollapseOverrides);
    }

    [Fact]
    public void ReduceSetAllGroupsCollapsed_WhenNotGrouping_NoOps()
    {
        var state = new LogTableState { GroupCollapseOverrides = ImmutableHashSet.Create("x") };

        var result = Reducers.ReduceSetAllGroupsCollapsed(state, new SetAllGroupsCollapsedAction(true));

        Assert.Same(state, result);
        Assert.False(result.GroupsCollapsedByDefault);
    }

    [Fact]
    public void ReduceSetGroupBy_IsLightweight_SetsRequestedWithoutTouchingLiveOrLists()
    {
        var state = SeedTabled(new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(id: 1, source: "A"),
            FilterEventBuilder.CreateTestEvent(id: 2, source: "B")
        });

        var result = Reducers.ReduceSetGroupBy(state, new SetGroupByAction(ColumnName.Source));

        Assert.Same(state.PerLogEvents, result.PerLogEvents);
        Assert.Equal(state.DisplayListVersion + 1, result.DisplayListVersion);
        Assert.Equal(ColumnName.Source, result.RequestedGroupBy);
        Assert.False(result.RequestedIsGroupDescending);
        Assert.Null(result.GroupBy);
    }

    [Fact]
    public void ReduceSetGroupBy_SetsGroupResortsAndResetsDirectionAndCollapse()
    {
        var state = SeedTabled(
            new List<ResolvedEvent>
            {
                FilterEventBuilder.CreateTestEvent(id: 2, source: "B"),
                FilterEventBuilder.CreateTestEvent(id: 1, source: "A")
            },
            isGroupDescending: true,
            collapseOverrides: ImmutableHashSet.Create("stale"));

        var result = Settle(Reducers.ReduceSetGroupBy(state, new SetGroupByAction(ColumnName.Source)));

        Assert.Equal(ColumnName.Source, result.GroupBy);
        Assert.False(result.IsGroupDescending);
        Assert.Empty(result.GroupCollapseOverrides);
        Assert.Equal(new[] { "A", "B" }, result.DisplayedEvents.Select(e => e.Source));
    }

    [Fact]
    public void ReduceSetGroupBy_WhenNull_ClearsGroupingAndResortsUngrouped()
    {
        var state = SeedTabled(
            new List<ResolvedEvent>
            {
                FilterEventBuilder.CreateTestEvent(id: 2, source: "B"),
                FilterEventBuilder.CreateTestEvent(id: 1, source: "A")
            },
            orderBy: ColumnName.EventId,
            isDescending: false,
            groupBy: ColumnName.Source);

        var result = Settle(Reducers.ReduceSetGroupBy(state, new SetGroupByAction(null)));

        Assert.Null(result.GroupBy);
        Assert.Equal(new[] { 1, 2 }, result.DisplayedEvents.Select(e => e.Id));
    }

    [Fact]
    public void ReduceSetGroupBy_WhenSameColumn_PreservesDirectionAndCollapse()
    {
        var state = new LogTableState
        {
            GroupBy = ColumnName.Source,
            RequestedGroupBy = ColumnName.Source,
            IsGroupDescending = true,
            RequestedIsGroupDescending = true,
            GroupCollapseOverrides = ImmutableHashSet.Create("kept")
        };

        var result = Reducers.ReduceSetGroupBy(state, new SetGroupByAction(ColumnName.Source));

        Assert.Same(state, result);
        Assert.True(result.IsGroupDescending);
        Assert.Contains("kept", result.GroupCollapseOverrides);
    }

    [Fact]
    public void ReduceSetOrderBy_IsLightweight_SetsRequestedWithoutTouchingLiveOrLists()
    {
        var state = SeedTabled(new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(id: 1, source: "A"),
            FilterEventBuilder.CreateTestEvent(id: 2, source: "B")
        });

        var result = Reducers.ReduceSetOrderBy(state, new SetOrderByAction(ColumnName.Source));

        Assert.Same(state.PerLogEvents, result.PerLogEvents);
        Assert.Equal(state.DisplayListVersion + 1, result.DisplayListVersion);
        Assert.Equal(ColumnName.Source, result.RequestedOrderBy);
        Assert.Null(result.OrderBy);
    }

    [Fact]
    public void ReduceSetOrderBy_LeavesPerLogListsConsistentWithLiveDisplayedContext()
    {
        var state = SeedTabled(
            new List<ResolvedEvent>
            {
                FilterEventBuilder.CreateTestEvent(id: 1, source: "A"),
                FilterEventBuilder.CreateTestEvent(id: 2, source: "B")
            },
            groupBy: ColumnName.Source);

        var afterSort = Reducers.ReduceSetOrderBy(state, new SetOrderByAction(ColumnName.EventId));

        var displayed = new SortContext(
            ResolvedEventOrdering.ResolveDefaultOrderBy(afterSort.OrderBy, afterSort.GroupBy, afterSort.PerLogEvents.Count),
            afterSort.IsDescending,
            afterSort.GroupBy,
            afterSort.IsGroupDescending);

        Assert.All(afterSort.PerLogEvents.Values, list => Assert.True(list.HasContext(displayed)));
    }

    [Fact]
    public void ReduceToggleGroupCollapsed_TogglesKey()
    {
        var collapsed = Reducers.ReduceToggleGroupCollapsed(
            new LogTableState { GroupBy = ColumnName.Source },
            new ToggleGroupCollapsedAction("grp"));

        Assert.Contains("grp", collapsed.GroupCollapseOverrides);

        var expanded = Reducers.ReduceToggleGroupCollapsed(collapsed, new ToggleGroupCollapsedAction("grp"));

        Assert.DoesNotContain("grp", expanded.GroupCollapseOverrides);
    }

    [Fact]
    public void ReduceToggleGroupCollapsed_WhenNotGrouping_NoOps()
    {
        var state = new LogTableState();

        var result = Reducers.ReduceToggleGroupCollapsed(state, new ToggleGroupCollapsedAction("grp"));

        Assert.Same(state, result);
    }

    [Fact]
    public void ReduceToggleGroupSorting_IsLightweight_SetsRequestedWithoutTouchingLiveOrLists()
    {
        var state = SeedTabled(
            new List<ResolvedEvent>
            {
                FilterEventBuilder.CreateTestEvent(id: 1, source: "A"),
                FilterEventBuilder.CreateTestEvent(id: 2, source: "B")
            },
            groupBy: ColumnName.Source);

        var result = Reducers.ReduceToggleGroupSorting(state);

        Assert.Same(state.PerLogEvents, result.PerLogEvents);
        Assert.Equal(state.DisplayListVersion + 1, result.DisplayListVersion);
        Assert.True(result.RequestedIsGroupDescending);
        Assert.False(result.IsGroupDescending);
        Assert.Equal(ColumnName.Source, result.GroupBy);
    }

    [Fact]
    public void ReduceToggleGroupSorting_WhenGrouped_FlipsDirectionAndResorts()
    {
        var state = SeedTabled(
            new List<ResolvedEvent>
            {
                FilterEventBuilder.CreateTestEvent(id: 1, source: "A"),
                FilterEventBuilder.CreateTestEvent(id: 2, source: "B")
            },
            groupBy: ColumnName.Source);

        var result = Settle(Reducers.ReduceToggleGroupSorting(state));

        Assert.True(result.IsGroupDescending);
        Assert.Equal(new[] { "B", "A" }, result.DisplayedEvents.Select(e => e.Source));
    }

    [Fact]
    public void ReduceToggleGroupSorting_WhenNotGrouped_IsNoOp()
    {
        var state = new LogTableState { GroupBy = null, IsGroupDescending = false };

        var result = Reducers.ReduceToggleGroupSorting(state);

        Assert.Same(state, result);
    }

    [Fact]
    public void ReduceToggleSorting_ComposesOffRequestedWithoutTouchingLive()
    {
        var seeded = SeedTabled(new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(id: 1, source: "A"),
            FilterEventBuilder.CreateTestEvent(id: 2, source: "B")
        });
        var state = seeded with { IsDescending = true, RequestedIsDescending = false };

        var afterFirst = Reducers.ReduceToggleSorting(state);

        Assert.True(afterFirst.RequestedIsDescending);
        Assert.True(afterFirst.IsDescending);

        var afterSecond = Reducers.ReduceToggleSorting(afterFirst);

        Assert.False(afterSecond.RequestedIsDescending);
        Assert.True(afterSecond.IsDescending);
        Assert.Equal(state.DisplayListVersion + 2, afterSecond.DisplayListVersion);
    }

    [Fact]
    public void ReduceToggleSorting_IsLightweight_SetsRequestedWithoutTouchingLiveOrLists()
    {
        var state = SeedTabled(new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(id: 1, source: "A"),
            FilterEventBuilder.CreateTestEvent(id: 2, source: "B")
        });

        var result = Reducers.ReduceToggleSorting(state);

        Assert.Same(state.PerLogEvents, result.PerLogEvents);
        Assert.Equal(state.DisplayListVersion + 1, result.DisplayListVersion);
        Assert.False(result.RequestedIsDescending);
        Assert.True(result.IsDescending);
    }

    [Fact]
    public void ReduceUpdateTable_AfterPartialAppends_ShouldReplaceNotMergeWithPartials()
    {
        // Arrange - partial AppendTableEvents land first (live-load deltas), then UpdateTable arrives with the full filtered list
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
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

        // Assert - partials are dropped before merging the full slice; canonical is exactly the full load
        Assert.Equal(4, state.DisplayedEvents.Count);
        Assert.Equal(state.DisplayedEvents.Select(e => e.RecordId), [1L, 2L, 3L, 4L]);
        Assert.Equal(4, state.EventCountByLog[logData.Id]);
    }

    [Fact]
    public void ReduceUpdateTable_ShouldUpdateTableEvents()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));

        var events = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(100),
            FilterEventBuilder.CreateTestEvent(200)
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
        // Arrange - first UpdateTable populates canonical with two events
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
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

        // Act - second UpdateTable for the same log (e.g., filter-driven reload)
        state = Reducers.ReduceUpdateTable(state, new UpdateTableAction(logData.Id, secondLoad));

        // Assert - canonical reflects only the second load; first load is replaced, not appended
        Assert.Equal(3, state.DisplayedEvents.Count);
        Assert.Equal(state.DisplayedEvents.Select(e => e.RecordId), [3L, 4L, 5L]);
        Assert.Equal(3, state.EventCountByLog[logData.Id]);
    }

    [Fact]
    public void ReduceUpdateTable_WhenDescendingOrderRequested_ShouldMergeInDescendingOrder()
    {
        // Arrange - two logs, descending sort
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel);
        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel);
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

        // Assert - canonical view in descending RecordId order
        Assert.Equal(4, state.DisplayedEvents.Count);
        Assert.Equal(4L, state.DisplayedEvents[0].RecordId);
        Assert.Equal(3L, state.DisplayedEvents[1].RecordId);
        Assert.Equal(2L, state.DisplayedEvents[2].RecordId);
        Assert.Equal(1L, state.DisplayedEvents[3].RecordId);
    }

    [Fact]
    public void ReduceUpdateTable_WhenGrouped_ReplacesLogSliceAndRetainsOtherLogGrouped()
    {
        var baseTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var log1 = new LogView(EventLogId.Create()) { LogName = "Log1" };
        var log2 = new LogView(EventLogId.Create()) { LogName = "Log2" };
        var combined = new LogView(EventLogId.Create()) { GroupId = LogTabGroupId.AllLogs };

        var existing = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(id: 1, source: "A", timeCreated: baseTime.AddSeconds(1), owningLog: "Log1"),
            FilterEventBuilder.CreateTestEvent(id: 2, source: "A", timeCreated: baseTime.AddSeconds(2), owningLog: "Log2")
        }.SortEvents(orderBy: null, isDescending: false, groupBy: ColumnName.Source);

        var state = new LogTableState
        {
            EventTables = ImmutableList.Create(combined, log1, log2),
            ActiveEventLogId = combined.Id,
            GroupBy = ColumnName.Source,
            IsDescending = false,
            PerLogEvents = BuildPerLog(
                existing, [log1, log2], orderBy: null, isDescending: false, groupBy: ColumnName.Source),
            EventCountByLog = ImmutableDictionary<EventLogId, int>.Empty
                .Add(log1.Id, 1)
                .Add(log2.Id, 1)
        };

        IReadOnlyList<ResolvedEvent> log1Replacement =
        [
            FilterEventBuilder.CreateTestEvent(id: 3, source: "A", timeCreated: baseTime.AddSeconds(3), owningLog: "Log1")
        ];

        var result = Reducers.ReduceUpdateTable(state, new UpdateTableAction(log1.Id, log1Replacement));

        Assert.Equal(new[] { 2, 3 }, result.DisplayedEvents.Select(e => e.Id).OrderBy(id => id));
        Assert.DoesNotContain(result.DisplayedEvents, e => e.Id == 1);
        Assert.Equal(new[] { "A", "A" }, result.DisplayedEvents.Select(e => e.Source));
    }

    [Fact]
    public void ReduceUpdateTable_WhenListVersionMatchesAndCountEqual_SkipsResort()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var state = Reducers.ReduceAddTable(new LogTableState(), new AddTableAction(logData));

        var events = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 10, RecordId = 1 },
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 11, RecordId = 2 }
        };
        state = Reducers.ReduceAppendTableEventsBatch(
            state,
            new AppendTableEventsBatchAction(
                new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>> { [logData.Id] = events })
            {
                VersionByLog = new Dictionary<EventLogId, int> { [logData.Id] = 0 }
            });

        var before = state.PerLogEvents[logData.Id];

        var result = Reducers.ReduceUpdateTable(
            state,
            new UpdateTableAction(logData.Id, events) { Version = 0 });

        Assert.Same(before, result.PerLogEvents[logData.Id]);
    }

    [Fact]
    public void ReduceUpdateTable_WhenListVersionSetByDisplayReady_SkipsResort()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var state = Reducers.ReduceAddTable(new LogTableState(), new AddTableAction(logData));
        state = state with { DisplayListVersion = 5 };

        var events = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 10, RecordId = 1 },
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 11, RecordId = 2 }
        };
        var fresh = SegmentedSortedList.CreateSorted(events, state.SortContext);

        state = Reducers.ReduceDisplayReady(
            state,
            new DisplayReadyAction
            {
                Lists = new Dictionary<EventLogId, SegmentedSortedList> { [logData.Id] = fresh },
                Version = 5
            });

        var installed = state.PerLogEvents[logData.Id];

        var result = Reducers.ReduceUpdateTable(
            state,
            new UpdateTableAction(logData.Id, events) { Version = 5 });

        Assert.Same(installed, result.PerLogEvents[logData.Id]);
    }

    [Fact]
    public void ReduceUpdateTable_WhenListVersionStaleFromMidLoadFilterChange_HealsToFinalizeSlice()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var state = Reducers.ReduceAddTable(new LogTableState(), new AddTableAction(logData));

        var streamed = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 10, RecordId = 1 },
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 11, RecordId = 2 }
        };
        state = state with { DisplayListVersion = 1 };

        state = Reducers.ReduceAppendTableEventsBatch(
            state,
            new AppendTableEventsBatchAction(
                new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>> { [logData.Id] = streamed })
            {
                VersionByLog = new Dictionary<EventLogId, int> { [logData.Id] = 0 }
            });

        var finalizeSlice = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 20, RecordId = 3 },
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 21, RecordId = 4 }
        };
        var result = Reducers.ReduceUpdateTable(
            state,
            new UpdateTableAction(logData.Id, finalizeSlice) { Version = 1 });

        Assert.Equal([3L, 4L], result.DisplayedEvents.Select(e => e.RecordId).OrderBy(r => r));
    }

    [Fact]
    public void ReduceUpdateTable_WhenOlderBatchAppendsAfterDisplayReadyTag_HealsAtFinalize()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var state = Reducers.ReduceAddTable(new LogTableState(), new AddTableAction(logData));
        state = state with { DisplayListVersion = 2 };

        var rebuilt = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 20, RecordId = 3 },
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 21, RecordId = 4 }
        };
        state = Reducers.ReduceDisplayReady(
            state,
            new DisplayReadyAction
            {
                Lists = new Dictionary<EventLogId, SegmentedSortedList>
                {
                    [logData.Id] = SegmentedSortedList.CreateSorted(rebuilt, state.SortContext)
                },
                Version = 2
            });
        Assert.Equal(2, state.PerLogListVersion[logData.Id]);

        state = Reducers.ReduceAppendTableEventsBatch(
            state,
            new AppendTableEventsBatchAction(
                new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>
                {
                    [logData.Id] = new List<ResolvedEvent>
                    {
                        new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 10, RecordId = 1 }
                    }
                })
            {
                VersionByLog = new Dictionary<EventLogId, int> { [logData.Id] = 1 }
            });
        Assert.Equal(1, state.PerLogListVersion[logData.Id]);

        var finalizeSlice = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 20, RecordId = 3 },
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 21, RecordId = 4 },
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 22, RecordId = 5 }
        };
        var result = Reducers.ReduceUpdateTable(
            state,
            new UpdateTableAction(logData.Id, finalizeSlice) { Version = 2 });

        Assert.Equal([3L, 4L, 5L], result.DisplayedEvents.Select(e => e.RecordId).OrderBy(r => r));
    }

    [Fact]
    public void ReduceUpdateTable_WhenSecondLogIsEmpty_ShouldKeepFirstLogEvents()
    {
        // Arrange - one log populated, one log empty (but not loading), ascending RecordId order
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel);
        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel);
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

        // Assert - canonical view contains only the populated log's events
        Assert.Equal(2, state.DisplayedEvents.Count);
        Assert.Equal(5L, state.DisplayedEvents[0].RecordId);
        Assert.Equal(7L, state.DisplayedEvents[1].RecordId);
    }

    [Fact]
    public void ReduceUpdateTable_WhenSecondLogPopulated_ShouldMergeIntoCanonicalInSortedOrder()
    {
        // Arrange - two logs with interleaved RecordIds, ascending RecordId order
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel);
        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel);
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

        // Act - UpdateTable maintains the canonical view atomically; no follow-up call needed.
        state = Reducers.ReduceUpdateTable(state, new UpdateTableAction(logData1.Id, eventsLog1));
        state = Reducers.ReduceUpdateTable(state, new UpdateTableAction(logData2.Id, eventsLog2));

        // Assert - canonical view interleaves both logs in RecordId order
        Assert.Equal(4, state.DisplayedEvents.Count);
        Assert.Equal(1L, state.DisplayedEvents[0].RecordId);
        Assert.Equal(2L, state.DisplayedEvents[1].RecordId);
        Assert.Equal(3L, state.DisplayedEvents[2].RecordId);
        Assert.Equal(4L, state.DisplayedEvents[3].RecordId);
        Assert.Equal(Constants.LogNameLog1, state.DisplayedEvents[0].OwningLog);
        Assert.Equal(Constants.LogNameLog2, state.DisplayedEvents[1].OwningLog);
    }

    [Fact]
    public void ReduceUpdateTable_WhenStreamingListHasNoVersionTag_RebuildsAtFinalize()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var state = Reducers.ReduceAddTable(new LogTableState(), new AddTableAction(logData));

        var events = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 10, RecordId = 1 },
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 11, RecordId = 2 }
        };
        state = Reducers.ReduceAppendTableEventsBatch(
            state,
            new AppendTableEventsBatchAction(
                new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>> { [logData.Id] = events }));

        Assert.False(state.PerLogListVersion.ContainsKey(logData.Id));

        var before = state.PerLogEvents[logData.Id];
        var result = Reducers.ReduceUpdateTable(
            state,
            new UpdateTableAction(logData.Id, events) { Version = 0 });

        Assert.NotSame(before, result.PerLogEvents[logData.Id]);
    }

    [Fact]
    public void ReduceUpdateTable_WhenTableNotFound_ShouldReturnStateUnchanged()
    {
        // Arrange - empty state, no tables
        var state = new LogTableState();
        var staleLogId = EventLogId.Create();
        var events = new List<ResolvedEvent> { FilterEventBuilder.CreateTestEvent(100) };
        var action = new UpdateTableAction(staleLogId, events);

        // Act - stale UpdateTable for a non-existent table
        var newState = Reducers.ReduceUpdateTable(state, action);

        // Assert - state unchanged, no exception thrown
        Assert.Same(state, newState);
    }

    [Fact]
    public void ReduceUpdateTable_WhenTimeCreatedDivergesFromRecordId_ShouldMergeByTimeCreated()
    {
        // Arrange - RecordIds ascending but TimeCreated descending: verifies sort is by timestamp
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

        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel);
        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel);
        var state = new LogTableState { IsDescending = false };
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData1));
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData2));

        // Act
        state = Reducers.ReduceUpdateTable(state, new UpdateTableAction(logData1.Id, log1Events));
        state = Reducers.ReduceUpdateTable(state, new UpdateTableAction(logData2.Id, log2Events));

        // Assert - canonical comes out time-ordered, not RecordId-ordered
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

    // Seeds PerLogEvents keyed by each event's OwningLog.
    private static ImmutableDictionary<EventLogId, SegmentedSortedList> BuildPerLog(
        IEnumerable<ResolvedEvent> events,
        IEnumerable<LogView> tables,
        ColumnName? orderBy = null,
        bool isDescending = true,
        ColumnName? groupBy = null,
        bool isGroupDescending = false)
    {
        var byLog = events.GroupBy(resolved => resolved.OwningLog).ToList();

        // Match the reducers' default: RecordId for one log, else DateAndTime.
        var context = new SortContext(
            ResolvedEventOrdering.ResolveDefaultOrderBy(orderBy, groupBy, byLog.Count),
            isDescending,
            groupBy,
            isGroupDescending);

        var idByName = tables.Where(table => !table.IsCombined).ToDictionary(table => table.LogName, table => table.Id);

        return byLog
            .ToImmutableDictionary(
                group => idByName.TryGetValue(group.Key, out var id) ? id : EventLogId.Create(),
                group => SegmentedSortedList.CreateSorted(group, context));
    }

    private static LogTableState SeedTabled(
        IReadOnlyList<ResolvedEvent> events,
        ColumnName? orderBy = null,
        bool isDescending = true,
        ColumnName? groupBy = null,
        bool isGroupDescending = false,
        ImmutableHashSet<string>? collapseOverrides = null)
    {
        var logData = new EventLogData(events[0].OwningLog, LogPathType.Channel);
        var state = Reducers.ReduceAddTable(new LogTableState(), new AddTableAction(logData));
        var tables = state.EventTables.Where(table => !table.IsCombined).ToList();

        return state with
        {
            OrderBy = orderBy,
            RequestedOrderBy = orderBy,
            IsDescending = isDescending,
            RequestedIsDescending = isDescending,
            GroupBy = groupBy,
            RequestedGroupBy = groupBy,
            IsGroupDescending = isGroupDescending,
            RequestedIsGroupDescending = isGroupDescending,
            PerLogEvents = BuildPerLog(events, tables, orderBy, isDescending, groupBy, isGroupDescending),
            GroupCollapseOverrides = collapseOverrides ?? ImmutableHashSet.Create<string>(StringComparer.Ordinal)
        };
    }

    private static LogTableState Settle(LogTableState state)
    {
        var context = state.SortContext;
        var lists = new Dictionary<EventLogId, SegmentedSortedList>(state.PerLogEvents.Count);

        foreach (var (logId, list) in state.PerLogEvents)
        {
            lists[logId] = SegmentedSortedList.CreateSorted(list, context);
        }

        return Reducers.ReduceDisplayReady(
            state,
            new DisplayReadyAction { Lists = lists, Version = state.DisplayListVersion });
    }
}
