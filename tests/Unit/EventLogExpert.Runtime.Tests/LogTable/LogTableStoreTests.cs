// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Filtering.TestUtils.Constants;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using EventLogExpert.Runtime.Tests.TestUtils.Constants;
using System.Collections.Immutable;
using ApplyFilterAction = EventLogExpert.Runtime.EventLog.ApplyFilterAction;
using CloseLogAction = EventLogExpert.Runtime.LogTable.CloseLogAction;
using LoadEventsAction = EventLogExpert.Runtime.EventLog.LoadEventsAction;
using LoadEventsPartialAction = EventLogExpert.Runtime.EventLog.LoadEventsPartialAction;
using Reducers = EventLogExpert.Runtime.LogTable.Reducers;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class LogTableStoreTests
{
    private static readonly ColumnDefaults s_columnDefaults = new();

    [Fact]
    public void EventTableAction_AddTable_ShouldStoreLogData()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);

        var action = new AddTableAction(logData);

        Assert.Equal(logData, action.LogData);
    }

    [Fact]
    public void EventTableAction_CloseLog_ShouldStoreLogId()
    {
        var logId = EventLogId.Create();

        var action = new CloseLogAction(logId);

        Assert.Equal(logId, action.LogId);
    }

    [Fact]
    public void EventTableAction_LoadColumnsCompleted_ShouldStoreColumns()
    {
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

        var action = new LoadColumnsCompletedAction(columns.ToImmutableDictionary(), widths.ToImmutableDictionary(), order);

        Assert.Equal(2, action.LoadedColumns.Count);
        Assert.True(action.LoadedColumns[ColumnName.Level]);
        Assert.True(action.LoadedColumns[ColumnName.DateAndTime]);
    }

    [Fact]
    public void EventTableAction_SetActiveTable_ShouldStoreLogId()
    {
        var logId = EventLogId.Create();

        var action = new SetActiveTableAction(logId);

        Assert.Equal(logId, action.LogId);
    }

    [Fact]
    public void EventTableAction_SetOrderBy_ShouldStoreColumnName()
    {
        var action = new SetOrderByAction(ColumnName.Level);

        Assert.Equal(ColumnName.Level, action.OrderBy);
    }

    [Fact]
    public void EventTableAction_SetOrderBy_WithNull_ShouldStoreNull()
    {
        var action = new SetOrderByAction(null);

        Assert.Null(action.OrderBy);
    }

    [Fact]
    public void EventTableAction_ToggleColumn_ShouldStoreColumnName()
    {
        var action = new ToggleColumnAction(ColumnName.Source);

        Assert.Equal(ColumnName.Source, action.ColumnName);
    }

    [Fact]
    public void IntegrationTest_ColumnManagement()
    {
        var state = new LogTableState();

        var columns = new Dictionary<ColumnName, bool>
        {
            { ColumnName.Level, true },
            { ColumnName.DateAndTime, true },
            { ColumnName.Source, false }
        };

        state = Reducers.ReduceLoadColumnsCompleted(
            state,
            new LoadColumnsCompletedAction(columns.ToImmutableDictionary(), ImmutableDictionary<ColumnName, int>.Empty, s_columnDefaults.ColumnOrder));

        Assert.Equal(3, state.Columns.Count);
        Assert.True(state.Columns[ColumnName.Level]);
        Assert.False(state.Columns[ColumnName.Source]);
    }

    [Fact]
    public void IntegrationTest_OpenMultipleLogsAndCloseOne()
    {
        var state = new LogTableState();
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel);
        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel);
        var logData3 = new EventLogData(Constants.LogNameLog3, LogPathType.Channel);

        state = Reducers.ReduceAddTable(state, new AddTableAction(logData1));
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData2));
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData3));

        Assert.Equal(4, state.EventTables.Count);
        Assert.Single(state.EventTables, t => t.IsCombined);

        state = Reducers.ReduceCloseLog(state, new CloseLogAction(logData2.Id));

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
        var state = new LogTableState();

        Assert.Empty(state.EventTables);
        Assert.Null(state.ActiveEventLogId);
        Assert.Empty(state.Columns);
        Assert.Empty(state.ColumnWidths);
        Assert.Empty(state.ColumnOrder);
        Assert.Null(state.OrderBy);
        Assert.True(state.IsDescending);
    }

    [Fact]
    public void LogView_ComputerName_WhenAlreadyLatched_ShouldNotBeOverwrittenByRawLoad()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));

        state = Reducers.ReduceLoadEventsPartial(
            state,
            new LoadEventsPartialAction(
                logData,
                [
                    new(Constants.LogNameTestLog, LogPathType.Channel)
                    {
                        Id = 10, RecordId = 1, ComputerName = FilterTestConstants.EventComputerServer01
                    }
                ]));

        state = Reducers.ReduceLoadEvents(
            state,
            new LoadEventsAction(
                logData,
                [
                    new(Constants.LogNameTestLog, LogPathType.Channel)
                    {
                        Id = 11, RecordId = 2, ComputerName = FilterTestConstants.EventComputerServer02
                    }
                ]));

        Assert.Equal(
            FilterTestConstants.EventComputerServer01,
            state.EventTables.First(t => t.Id == logData.Id).ComputerName);
    }

    [Fact]
    public void LogView_ComputerName_WhenIngestTargetsCombinedTab_ShouldNotLatch()
    {
        var combinedId = EventLogId.Create();
        var state = new LogTableState
        {
            EventTables = [new LogView(combinedId) { GroupId = LogTabGroupId.AllLogs, LogName = Constants.LogNameTestLog }]
        };

        var byLog = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>
        {
            [combinedId] =
            [
                new(Constants.LogNameTestLog, LogPathType.Channel)
                {
                    Id = 10, RecordId = 1, ComputerName = FilterTestConstants.EventComputerServer01
                }
            ]
        };

        var next = Reducers.ReduceIngestRawEvents(state, new IngestRawEventsAction(byLog, RawIngestMode.Append));

        Assert.Equal(string.Empty, next.EventTables.First(t => t.Id == combinedId).ComputerName);
    }

    [Fact]
    public void LogView_ComputerName_WhenIngestTargetsUnknownLog_ShouldLeaveTablesUntouched()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));

        var byLog = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>
        {
            [EventLogId.Create()] =
            [
                new(Constants.LogNameTestLog, LogPathType.Channel)
                {
                    Id = 10, RecordId = 1, ComputerName = FilterTestConstants.EventComputerServer01
                }
            ]
        };

        var next = Reducers.ReduceIngestRawEvents(state, new IngestRawEventsAction(byLog, RawIngestMode.Append));

        Assert.Equal(string.Empty, next.EventTables.First(t => t.Id == logData.Id).ComputerName);
    }

    [Fact]
    public void LogView_ComputerName_WhenLiveTailIngestArrives_ShouldLatch()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));

        var byLog = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>
        {
            [logData.Id] =
            [
                new(Constants.LogNameTestLog, LogPathType.Channel)
                {
                    Id = 10, RecordId = 1, ComputerName = FilterTestConstants.EventComputerServer01
                }
            ]
        };

        state = Reducers.ReduceIngestRawEvents(state, new IngestRawEventsAction(byLog, RawIngestMode.Append));

        Assert.Equal(
            FilterTestConstants.EventComputerServer01,
            state.EventTables.First(t => t.Id == logData.Id).ComputerName);
    }

    [Fact]
    public void LogView_ComputerName_WhenNoEvents_ShouldReturnEmpty()
    {
        var model = new LogView(EventLogId.Create());

        var computerName = model.ComputerName;

        Assert.Equal(string.Empty, computerName);
    }

    [Fact]
    public void LogView_ComputerName_WhenPartialRawLoadArrives_ShouldLatchBeforeFinalize()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));

        var partial = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel)
            {
                Id = 10, RecordId = 1, ComputerName = FilterTestConstants.EventComputerServer01
            }
        };

        state = Reducers.ReduceLoadEventsPartial(state, new LoadEventsPartialAction(logData, partial));

        Assert.Equal(
            FilterTestConstants.EventComputerServer01,
            state.EventTables.First(t => t.Id == logData.Id).ComputerName);
    }

    [Fact]
    public void LogView_ComputerName_WhenRawLoadCompletes_ShouldLatchWithoutDisplayFinalize()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));

        var events = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 10, RecordId = 1, ComputerName = string.Empty },
            new(Constants.LogNameTestLog, LogPathType.Channel)
            {
                Id = 11, RecordId = 2, ComputerName = FilterTestConstants.EventComputerServer01
            }
        };

        state = Reducers.ReduceLoadEvents(state, new LoadEventsAction(logData, events));

        Assert.Equal(
            FilterTestConstants.EventComputerServer01,
            state.EventTables.First(t => t.Id == logData.Id).ComputerName);
    }

    [Fact]
    public void LogView_IsLoading_WhenRawLoadCompletesWithNoEvents_ShouldStillClear()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));

        state = Reducers.ReduceLoadEvents(state, new LoadEventsAction(logData, []));

        Assert.False(state.EventTables.First(t => t.Id == logData.Id).IsLoading);
    }

    [Fact]
    public void LogView_IsLoading_WhenRawLoadCompletes_ShouldClearWithoutDisplayFinalize()
    {
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData));

        Assert.True(state.EventTables.First(t => t.Id == logData.Id).IsLoading);

        var events = new List<ResolvedEvent>
        {
            new(Constants.LogNameTestLog, LogPathType.Channel) { Id = 10, RecordId = 1 }
        };

        state = Reducers.ReduceLoadEvents(state, new LoadEventsAction(logData, events));

        Assert.False(state.EventTables.First(t => t.Id == logData.Id).IsLoading);
    }

    [Fact]
    public void LogView_ShouldHaveUniqueId()
    {
        var model1 = new LogView(EventLogId.Create());
        var model2 = new LogView(EventLogId.Create());

        Assert.NotEqual(model1.Id, model2.Id);
    }

    [Fact]
    public void ReduceAddTable_WhenCombinedExists_ShouldNotCreateAnotherCombined()
    {
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel);
        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData1));
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData2));

        var logData3 = new EventLogData(Constants.LogNameLog3, LogPathType.Channel);
        var action = new AddTableAction(logData3);

        var newState = Reducers.ReduceAddTable(state, action);

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
        var state = new LogTableState();
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var action = new AddTableAction(logData);

        var newState = Reducers.ReduceAddTable(state, action);

        Assert.True(newState.EventTables.First().IsLoading);
    }

    [Fact]
    public void ReduceAddTable_WhenFirstTable_ShouldSetAsActive()
    {
        var state = new LogTableState();
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var action = new AddTableAction(logData);

        var newState = Reducers.ReduceAddTable(state, action);

        Assert.Single(newState.EventTables);
        Assert.NotNull(newState.ActiveEventLogId);
        Assert.Equal(logData.Id, newState.EventTables.First().Id);
        Assert.Equal(logData.Id, newState.ActiveEventLogId);
    }

    [Fact]
    public void ReduceAddTable_WhenFirstTable_WithFilePath_ShouldSetFileName()
    {
        var state = new LogTableState();
        var logData = new EventLogData(Constants.FilePathTestEvtx, LogPathType.File);
        var action = new AddTableAction(logData);

        var newState = Reducers.ReduceAddTable(state, action);

        Assert.Equal(Constants.FilePathTestEvtx, newState.EventTables.First().FileName);
        Assert.Equal(LogPathType.File, newState.EventTables.First().LogPathType);
    }

    [Fact]
    public void ReduceAddTable_WhenFirstTable_WithLogName_ShouldNotSetFileName()
    {
        var state = new LogTableState();
        var logData = new EventLogData(Constants.LogNameApplication, LogPathType.Channel);
        var action = new AddTableAction(logData);

        var newState = Reducers.ReduceAddTable(state, action);

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
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData1));

        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel);
        var action = new AddTableAction(logData2);

        var newState = Reducers.ReduceAddTable(state, action);

        Assert.Equal(3, newState.EventTables.Count);
        Assert.Contains(newState.EventTables, t => t.IsCombined);
    }

    [Fact]
    public void ReduceAddTable_WhenSecondTable_ShouldSetCombinedAsActive()
    {
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel);
        var state = new LogTableState();
        state = Reducers.ReduceAddTable(state, new AddTableAction(logData1));

        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel);
        var action = new AddTableAction(logData2);

        var newState = Reducers.ReduceAddTable(state, action);

        var combinedTable = newState.EventTables.First(t => t.IsCombined);
        Assert.Equal(combinedTable.Id, newState.ActiveEventLogId);
    }

    [Fact]
    public void ReduceApplyFilter_PreservesRequestedSort()
    {
        var state = new LogTableState
        {
            RequestedOrderBy = ColumnName.Source,
            RequestedGroupBy = ColumnName.EventId,
            RequestedIsDescending = false,
            RequestedIsGroupDescending = true
        };

        var result = Reducers.ReduceApplyFilter(
            state,
            new ApplyFilterAction(new Filter(null, [])));

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
            GroupsCollapsedByDefault = true,
            GroupCollapseOverrides = ImmutableHashSet.Create("g")
        };

        var result = Reducers.ReduceCloseLog(state, new CloseLogAction(tableB.Id));

        Assert.Equal(tableA.Id, result.ActiveEventLogId);
        Assert.False(result.GroupsCollapsedByDefault);
        Assert.Empty(result.GroupCollapseOverrides);
    }

    [Fact]
    public void ReduceLoadColumnsCompleted_WhenGroupColumnHidden_AsksToUngroupWithoutMovingTheRowsYet()
    {
        var state = new LogTableState
        {
            GroupBy = ColumnName.Source,
            IsGroupDescending = true,
            RequestedGroupBy = ColumnName.Source,
            RequestedIsGroupDescending = true,
            GroupCollapseOverrides = ImmutableHashSet.Create("g"),
            OrderBy = ColumnName.EventId,
            IsDescending = false
        };

        var hiddenColumns = ImmutableDictionary<ColumnName, bool>.Empty
            .Add(ColumnName.Source, false)
            .Add(ColumnName.EventId, true);

        var result = Reducers.ReduceLoadColumnsCompleted(
            state,
            new LoadColumnsCompletedAction(hiddenColumns, ImmutableDictionary<ColumnName, int>.Empty, []));

        Assert.Null(result.RequestedGroupBy);
        Assert.False(result.RequestedIsGroupDescending);
        Assert.Equal(ColumnName.Source, result.GroupBy);
        Assert.True(result.HasPendingSortChange);
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
    public void ReduceLoadColumnsCompleted_WhenLiveAndRequestedGroupBothHidden_ClearsTheRequest()
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

        Assert.Null(result.RequestedGroupBy);
        Assert.False(result.RequestedIsGroupDescending);
        Assert.Equal(ColumnName.Source, result.GroupBy);
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

        Assert.Equal(ColumnName.Level, result.RequestedGroupBy);
        Assert.Equal(ColumnName.Source, result.GroupBy);
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
    public void ReduceLoadColumnsCompleted_WhenResetDefaultsHidesGroupColumn_AsksToUngroup()
    {
        var state = new LogTableState { GroupBy = ColumnName.ActivityId, RequestedGroupBy = ColumnName.ActivityId };

        var defaults = ImmutableDictionary<ColumnName, bool>.Empty
            .Add(ColumnName.Level, true)
            .Add(ColumnName.DateAndTime, true);

        var result = Reducers.ReduceLoadColumnsCompleted(
            state,
            new LoadColumnsCompletedAction(defaults, ImmutableDictionary<ColumnName, int>.Empty, []));

        Assert.Null(result.RequestedGroupBy);
        Assert.Equal(ColumnName.ActivityId, result.GroupBy);
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
    public void ReduceSetGroupBy_IsLightweight_SetsRequestedWithoutCommittingLive()
    {
        var state = SeedTabled(new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(id: 1, source: "A"),
            FilterEventBuilder.CreateTestEvent(id: 2, source: "B")
        });

        var result = Reducers.ReduceSetGroupBy(state, new SetGroupByAction(ColumnName.Source));

        Assert.Equal(ColumnName.Source, result.RequestedGroupBy);
        Assert.False(result.RequestedIsGroupDescending);
        Assert.Null(result.GroupBy);
    }

    [Fact]
    public void ReduceSetGroupBy_WhenAdopted_CommitsGroupResetsDirectionAndClearsStaleCollapse()
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
    }

    [Fact]
    public void ReduceSetGroupBy_WhenNullAndAdopted_ClearsCommittedGrouping()
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
    public void ReduceSetOrderBy_IsLightweight_SetsRequestedWithoutCommittingLive()
    {
        var state = SeedTabled(new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(id: 1, source: "A"),
            FilterEventBuilder.CreateTestEvent(id: 2, source: "B")
        });

        var result = Reducers.ReduceSetOrderBy(state, new SetOrderByAction(ColumnName.Source));

        Assert.Equal(ColumnName.Source, result.RequestedOrderBy);
        Assert.Null(result.OrderBy);
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
    public void ReduceToggleGroupSorting_IsLightweight_SetsRequestedWithoutCommittingLive()
    {
        var state = SeedTabled(
            new List<ResolvedEvent>
            {
                FilterEventBuilder.CreateTestEvent(id: 1, source: "A"),
                FilterEventBuilder.CreateTestEvent(id: 2, source: "B")
            },
            groupBy: ColumnName.Source);

        var result = Reducers.ReduceToggleGroupSorting(state);

        Assert.True(result.RequestedIsGroupDescending);
        Assert.False(result.IsGroupDescending);
        Assert.Equal(ColumnName.Source, result.GroupBy);
    }

    [Fact]
    public void ReduceToggleGroupSorting_WhenGroupedAndAdopted_FlipsCommittedDirection()
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
    }

    [Fact]
    public void ReduceToggleSorting_IsLightweight_SetsRequestedWithoutCommittingLive()
    {
        var state = SeedTabled(new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(id: 1, source: "A"),
            FilterEventBuilder.CreateTestEvent(id: 2, source: "B")
        });

        var result = Reducers.ReduceToggleSorting(state);

        Assert.False(result.RequestedIsDescending);
        Assert.True(result.IsDescending);
    }

    private static LogTableState AdoptRequestedOrdering(LogTableState state)
    {
        if (state.ActiveEventLogId is not { } activeId) { return state; }

        var ready = new OrderedViewReady(
            SnapshotVersion: state.LastPublishedSnapshotVersion + 1,
            Identity: state.ViewIdentity,
            Sequence: state.HighestInvalidationSequence,
            SingleLogId: activeId,
            InScope: [new LogGeneration(activeId, 0)],
            View: LogTableState.EmptyView,
            Config: state.SortContext,
            Filter: state.AppliedFilter);

        return Reducers.ReduceOrderedViewUpdated(state, new OrderedViewUpdatedAction(ready));
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
            GroupCollapseOverrides = collapseOverrides ?? ImmutableHashSet.Create<string>(StringComparer.Ordinal)
        };
    }

    private static LogTableState Settle(LogTableState state) => AdoptRequestedOrdering(state);
}
