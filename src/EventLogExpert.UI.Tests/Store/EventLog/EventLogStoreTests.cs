// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Tests.TestUtils;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.Store.EventLog;

public sealed class EventLogStoreTests
{
    [Fact]
    public void BufferedEventsWorkflow_ShouldHandleCorrectly()
    {
        // Arrange
        var state = new EventLogState { ContinuouslyUpdate = false };

        var events = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(100),
            EventUtils.CreateTestEvent(200)
        };

        // Act - Buffer events
        state = EventLogReducers.ReduceAddEventBuffered(state, new EventLogAction.AddEventBuffered(events, false));

        // Assert
        Assert.Equal(2, state.NewEventBuffer.Count);
        Assert.False(state.NewEventBufferIsFull);

        // Act - Set continuously update
        state = EventLogReducers.ReduceSetContinuouslyUpdate(state, new EventLogAction.SetContinuouslyUpdate(true));

        // Assert
        Assert.True(state.ContinuouslyUpdate);
    }

    [Fact]
    public void EventLogAction_AddEvent_ShouldStoreNewEvent()
    {
        // Arrange
        var newEvent = EventUtils.CreateTestEvent(100);

        // Act
        var action = new EventLogAction.AddEvent(newEvent);

        // Assert
        Assert.Equal(newEvent, action.NewEvent);
    }

    [Fact]
    public void EventLogAction_AddEventBuffered_ShouldStoreBufferAndFullFlag()
    {
        // Arrange
        var events = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(100),
            EventUtils.CreateTestEvent(200)
        };

        // Act
        var action = new EventLogAction.AddEventBuffered(events, true);

        // Assert
        Assert.Equal(2, action.UpdatedBuffer.Count);
        Assert.True(action.IsFull);
    }

    [Fact]
    public void EventLogAction_AddEventSuccess_ShouldStoreActiveLogs()
    {
        // Arrange
        var logData = new EventLogData("TestLog", PathType.LogName, []);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        // Act
        var action = new EventLogAction.AddEventSuccess(activeLogs);

        // Assert
        Assert.Single(action.ActiveLogs);
        Assert.True(action.ActiveLogs.ContainsKey(Constants.LogNameTestLog));
    }

    [Fact]
    public void EventLogAction_CloseLog_ShouldStoreLogIdAndName()
    {
        // Arrange
        var logId = EventLogId.Create();
        var logName = Constants.LogNameTestLog;

        // Act
        var action = new EventLogAction.CloseLog(logId, logName);

        // Assert
        Assert.Equal(logId, action.LogId);
        Assert.Equal(logName, action.LogName);
    }

    [Fact]
    public void EventLogAction_LoadEvents_ShouldStoreLogDataAndEvents()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, []);

        var events = ImmutableArray.Create(EventUtils.CreateTestEvent(100), EventUtils.CreateTestEvent(200));

        // Act
        var action = new EventLogAction.LoadEvents(logData, events);

        // Assert
        Assert.Equal(logData, action.LogData);
        Assert.Equal(2, action.Events.Length);
    }

    [Fact]
    public void EventLogAction_OpenLog_ShouldStoreLogNameAndPathType()
    {
        // Arrange
        var logName = Constants.LogNameApplication;
        var pathType = PathType.LogName;

        // Act
        var action = new EventLogAction.OpenLog(logName, pathType);

        // Assert
        Assert.Equal(logName, action.LogName);
        Assert.Equal(pathType, action.PathType);
    }

    [Fact]
    public void EventLogAction_OpenLog_WithCancellationToken_ShouldStoreToken()
    {
        // Arrange
        var cts = new CancellationTokenSource();

        // Act
        var action = new EventLogAction.OpenLog(Constants.LogNameTestLog, PathType.FilePath, cts.Token);

        // Assert
        Assert.Equal(cts.Token, action.Token);
    }

    [Fact]
    public void EventLogAction_SelectEvent_DefaultFlags_ShouldBeFalse()
    {
        // Arrange
        var selectedEvent = EventUtils.CreateTestEvent(100);

        // Act
        var action = new EventLogAction.SelectEvent(selectedEvent);

        // Assert
        Assert.False(action.IsMultiSelect);
        Assert.False(action.ShouldStaySelected);
    }

    [Fact]
    public void EventLogAction_SelectEvent_ShouldStoreEventAndSelectionFlags()
    {
        // Arrange
        var selectedEvent = EventUtils.CreateTestEvent(100);

        // Act
        var action = new EventLogAction.SelectEvent(selectedEvent, true, true);

        // Assert
        Assert.Equal(selectedEvent, action.SelectedEvent);
        Assert.True(action.IsMultiSelect);
        Assert.True(action.ShouldStaySelected);
    }

    [Fact]
    public void EventLogAction_SelectEvents_ShouldStoreMultipleEvents()
    {
        // Arrange
        var events = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(100),
            EventUtils.CreateTestEvent(200)
        };

        // Act
        var action = new EventLogAction.SelectEvents(events);

        // Assert
        Assert.Equal(2, action.SelectedEvents.Count());
    }

    [Fact]
    public void EventLogAction_SetContinuouslyUpdate_ShouldStoreFlag()
    {
        // Act
        var actionTrue = new EventLogAction.SetContinuouslyUpdate(true);
        var actionFalse = new EventLogAction.SetContinuouslyUpdate(false);

        // Assert
        Assert.True(actionTrue.ContinuouslyUpdate);
        Assert.False(actionFalse.ContinuouslyUpdate);
    }

    [Fact]
    public void EventLogAction_SetFilters_ShouldStoreEventFilter()
    {
        // Arrange
        var eventFilter = new EventFilter(null, []);

        // Act
        var action = new EventLogAction.SetFilters(eventFilter);

        // Assert
        Assert.Equal(eventFilter, action.EventFilter);
    }

    [Fact]
    public void EventLogData_Constructor_ShouldSetProperties()
    {
        // Arrange
        var name = Constants.LogNameTestLog;
        var type = PathType.LogName;
        var events = new List<DisplayEventModel> { EventUtils.CreateTestEvent(100) };

        // Act
        var logData = new EventLogData(name, type, events);

        // Assert
        Assert.Equal(name, logData.Name);
        Assert.Equal(type, logData.Type);
        Assert.Single(logData.Events);
    }

    [Fact]
    public void EventLogData_GetCategoryValues_ForId_ShouldReturnDistinctIds()
    {
        // Arrange
        var events = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(100),
            EventUtils.CreateTestEvent(100),
            EventUtils.CreateTestEvent(200)
        };

        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, events);

        // Act
        var values = logData.GetCategoryValues(FilterCategory.Id).ToList();

        // Assert
        Assert.Equal(2, values.Count);
        Assert.Contains(Constants.FilterValue100, values);
        Assert.Contains(Constants.FilterValue200, values);
    }

    [Fact]
    public void EventLogData_GetCategoryValues_ForLevel_ShouldReturnAllSeverityLevels()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, []);

        // Act
        var values = logData.GetCategoryValues(FilterCategory.Level).ToList();

        // Assert
        Assert.Equal(Enum.GetNames<SeverityLevel>().Length, values.Count);
    }

    [Fact]
    public void EventLogData_GetCategoryValues_ForSource_ShouldReturnDistinctSources()
    {
        // Arrange
        var events = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(100),
            EventUtils.CreateTestEvent(200),
            EventUtils.CreateTestEvent(300, Constants.EventSourceOtherSource)
        };

        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, events);

        // Act
        var values = logData.GetCategoryValues(FilterCategory.Source).ToList();

        // Assert
        Assert.Equal(2, values.Count);
    }

    [Fact]
    public void EventLogData_GetCategoryValues_ForUnknownCategory_ShouldReturnEmpty()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, [EventUtils.CreateTestEvent(100)]);

        // Act
        var values = logData.GetCategoryValues((FilterCategory)999).ToList();

        // Assert
        Assert.Empty(values);
    }

    [Fact]
    public void EventLogData_Id_ShouldBeUnique()
    {
        // Arrange & Act
        var logData1 = new EventLogData(Constants.LogNameLog1, PathType.LogName, []);
        var logData2 = new EventLogData(Constants.LogNameLog2, PathType.LogName, []);

        // Assert
        Assert.NotEqual(logData1.Id, logData2.Id);
    }

    [Fact]
    public void EventLogState_DefaultState_ShouldHaveCorrectDefaults()
    {
        // Arrange & Act
        var state = new EventLogState();

        // Assert
        Assert.Empty(state.ActiveLogs);
        Assert.Null(state.AppliedFilter.DateFilter);
        Assert.Empty(state.AppliedFilter.Filters);
        Assert.False(state.ContinuouslyUpdate);
        Assert.Empty(state.NewEventBuffer);
        Assert.False(state.NewEventBufferIsFull);
        Assert.Empty(state.SelectedEvents);
    }

    [Fact]
    public void EventLogState_MaxNewEvents_ShouldReturn1000()
    {
        // Act
        var maxEvents = EventLogState.MaxNewEvents;

        // Assert
        Assert.Equal(Constants.MaxNewEvents, maxEvents);
    }

    [Fact]
    public void LoadEventsAndSelect_ShouldWorkCorrectly()
    {
        // Arrange
        var state = new EventLogState();

        state = EventLogReducers.ReduceOpenLog(state,
            new EventLogAction.OpenLog(Constants.LogNameTestLog, PathType.LogName));

        var logData = state.ActiveLogs[Constants.LogNameTestLog];

        var events = ImmutableArray.Create(
            EventUtils.CreateTestEvent(100),
            EventUtils.CreateTestEvent(200),
            EventUtils.CreateTestEvent(300)
        );

        // Act - Load events
        state = EventLogReducers.ReduceLoadEvents(state, new EventLogAction.LoadEvents(logData, events));

        // Assert
        Assert.Equal(3, state.ActiveLogs[Constants.LogNameTestLog].Events.Count);

        // Act - Select event
        state = EventLogReducers.ReduceSelectEvent(state, new EventLogAction.SelectEvent(events[0]));

        // Assert
        Assert.Single(state.SelectedEvents);
        Assert.Equal(events[0], state.SelectedEvents[0]);
    }

    [Fact]
    public void MultipleLogOperations_ShouldMaintainCorrectState()
    {
        // Arrange
        var state = new EventLogState();

        // Act - Open multiple logs
        state = EventLogReducers.ReduceOpenLog(state,
            new EventLogAction.OpenLog(Constants.LogNameLog1, PathType.LogName));

        state = EventLogReducers.ReduceOpenLog(state,
            new EventLogAction.OpenLog(Constants.LogNameLog2, PathType.FilePath));

        state = EventLogReducers.ReduceOpenLog(state,
            new EventLogAction.OpenLog(Constants.LogNameLog3, PathType.LogName));

        // Assert
        Assert.Equal(3, state.ActiveLogs.Count);

        // Act - Close one log
        var log2Id = state.ActiveLogs[Constants.LogNameLog2].Id;
        state = EventLogReducers.ReduceCloseLog(state, new EventLogAction.CloseLog(log2Id, Constants.LogNameLog2));

        // Assert
        Assert.Equal(2, state.ActiveLogs.Count);
        Assert.False(state.ActiveLogs.ContainsKey(Constants.LogNameLog2));
        Assert.True(state.ActiveLogs.ContainsKey(Constants.LogNameLog1));
        Assert.True(state.ActiveLogs.ContainsKey(Constants.LogNameLog3));
    }

    [Fact]
    public void OpenAndCloseLog_ShouldMaintainStateConsistency()
    {
        // Arrange
        var state = new EventLogState();

        // Act - Open log
        var openAction = new EventLogAction.OpenLog(Constants.LogNameTestLog, PathType.LogName);
        state = EventLogReducers.ReduceOpenLog(state, openAction);

        Assert.Single(state.ActiveLogs);

        // Act - Close log
        var logId = state.ActiveLogs[Constants.LogNameTestLog].Id;
        var closeAction = new EventLogAction.CloseLog(logId, Constants.LogNameTestLog);
        state = EventLogReducers.ReduceCloseLog(state, closeAction);

        // Assert
        Assert.Empty(state.ActiveLogs);
    }

    [Fact]
    public void ReduceAddEventBuffered_ShouldUpdateBufferAndFullFlag()
    {
        // Arrange
        var state = new EventLogState();

        var events = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(100),
            EventUtils.CreateTestEvent(200)
        };

        var action = new EventLogAction.AddEventBuffered(events, true);

        // Act
        var newState = EventLogReducers.ReduceAddEventBuffered(state, action);

        // Assert
        Assert.Equal(2, newState.NewEventBuffer.Count);
        Assert.True(newState.NewEventBufferIsFull);
    }

    [Fact]
    public void ReduceAddEventBuffered_WhenNotFull_ShouldSetFullFlagFalse()
    {
        // Arrange
        var state = new EventLogState { NewEventBufferIsFull = true };
        var events = new List<DisplayEventModel> { EventUtils.CreateTestEvent(100) };
        var action = new EventLogAction.AddEventBuffered(events, false);

        // Act
        var newState = EventLogReducers.ReduceAddEventBuffered(state, action);

        // Assert
        Assert.False(newState.NewEventBufferIsFull);
    }

    [Fact]
    public void ReduceAddEventSuccess_ShouldUpdateActiveLogs()
    {
        // Arrange
        var state = new EventLogState();
        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, []);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);
        var action = new EventLogAction.AddEventSuccess(activeLogs);

        // Act
        var newState = EventLogReducers.ReduceAddEventSuccess(state, action);

        // Assert
        Assert.Single(newState.ActiveLogs);
        Assert.True(newState.ActiveLogs.ContainsKey(Constants.LogNameTestLog));
    }

    [Fact]
    public void ReduceCloseAll_ShouldClearAllState()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, []);

        var state = new EventLogState
        {
            ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData),
            SelectedEvents = [EventUtils.CreateTestEvent(100)],
            NewEventBuffer = [EventUtils.CreateTestEvent(200)],
            NewEventBufferIsFull = true
        };

        // Act
        var newState = EventLogReducers.ReduceCloseAll(state);

        // Assert
        Assert.Empty(newState.ActiveLogs);
        Assert.Empty(newState.SelectedEvents);
        Assert.Empty(newState.NewEventBuffer);
        Assert.False(newState.NewEventBufferIsFull);
    }

    [Fact]
    public void ReduceCloseLog_ShouldFilterNewEventBuffer()
    {
        // Arrange
        var eventForLog1 = new DisplayEventModel(Constants.LogNameLog1, PathType.LogName)
        {
            Id = 100,
            Source = Constants.EventSourceTestSource,
            Level = Constants.EventLevelInformation
        };

        var eventForLog2 = new DisplayEventModel(Constants.LogNameLog2, PathType.LogName)
        {
            Id = 200,
            Source = Constants.EventSourceTestSource,
            Level = Constants.EventLevelInformation
        };

        var logData1 = new EventLogData(Constants.LogNameLog1, PathType.LogName, []);

        var state = new EventLogState
        {
            ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameLog1, logData1),
            NewEventBuffer = [eventForLog1, eventForLog2]
        };

        var action = new EventLogAction.CloseLog(logData1.Id, Constants.LogNameLog1);

        // Act
        var newState = EventLogReducers.ReduceCloseLog(state, action);

        // Assert
        Assert.Single(newState.NewEventBuffer);
        Assert.Equal(Constants.LogNameLog2, newState.NewEventBuffer.First().OwningLog);
    }

    [Fact]
    public void ReduceCloseLog_ShouldRemoveSpecifiedLog()
    {
        // Arrange
        var logData1 = new EventLogData(Constants.LogNameLog1, PathType.LogName, []);
        var logData2 = new EventLogData(Constants.LogNameLog2, PathType.LogName, []);

        var state = new EventLogState
        {
            ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty
                .Add(Constants.LogNameLog1, logData1)
                .Add(Constants.LogNameLog2, logData2)
        };

        var action = new EventLogAction.CloseLog(logData1.Id, Constants.LogNameLog1);

        // Act
        var newState = EventLogReducers.ReduceCloseLog(state, action);

        // Assert
        Assert.Single(newState.ActiveLogs);
        Assert.False(newState.ActiveLogs.ContainsKey(Constants.LogNameLog1));
        Assert.True(newState.ActiveLogs.ContainsKey(Constants.LogNameLog2));
    }

    [Fact]
    public void ReduceLoadEvents_ShouldUpdateLogWithEvents()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, []);

        var state = new EventLogState
        {
            ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData)
        };

        var events = ImmutableArray.Create(EventUtils.CreateTestEvent(100), EventUtils.CreateTestEvent(200));

        var action = new EventLogAction.LoadEvents(logData, events);

        // Act
        var newState = EventLogReducers.ReduceLoadEvents(state, action);

        // Assert
        Assert.Equal(2, newState.ActiveLogs[Constants.LogNameTestLog].Events.Count);
    }

    [Fact]
    public void ReduceLoadEvents_ShouldIsolateStateFromOriginalList()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, []);

        var state = new EventLogState
        {
            ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData)
        };

        var events = ImmutableArray.Create(EventUtils.CreateTestEvent(100), EventUtils.CreateTestEvent(200));

        var action = new EventLogAction.LoadEvents(logData, events);

        // Act
        var newState = EventLogReducers.ReduceLoadEvents(state, action);

        // ImmutableArray is inherently isolated — creating a new one doesn't affect the state
        var extendedEvents = events.Add(EventUtils.CreateTestEvent(300));

        // Assert - state should not reflect the extension
        Assert.Equal(2, newState.ActiveLogs[Constants.LogNameTestLog].Events.Count);
        Assert.Equal(3, extendedEvents.Length);
    }

    [Fact]
    public void ReduceOpenLog_ShouldAddEmptyLogData()
    {
        // Arrange
        var state = new EventLogState();
        var action = new EventLogAction.OpenLog(Constants.LogNameNewLog, PathType.LogName);

        // Act
        var newState = EventLogReducers.ReduceOpenLog(state, action);

        // Assert
        Assert.Single(newState.ActiveLogs);
        Assert.True(newState.ActiveLogs.ContainsKey(Constants.LogNameNewLog));
        Assert.Empty(newState.ActiveLogs[Constants.LogNameNewLog].Events);
    }

    [Fact]
    public void ReduceOpenLog_WithFilePath_ShouldSetCorrectPathType()
    {
        // Arrange
        var state = new EventLogState();
        var action = new EventLogAction.OpenLog(Constants.FilePathTestEvtx, PathType.FilePath);

        // Act
        var newState = EventLogReducers.ReduceOpenLog(state, action);

        // Assert
        Assert.Equal(PathType.FilePath, newState.ActiveLogs[Constants.FilePathTestEvtx].Type);
    }

    [Fact]
    public void ReduceSelectEvent_WhenEventNotSelected_ShouldAddEvent()
    {
        // Arrange
        var state = new EventLogState();
        var selectedEvent = EventUtils.CreateTestEvent(100);
        var action = new EventLogAction.SelectEvent(selectedEvent);

        // Act
        var newState = EventLogReducers.ReduceSelectEvent(state, action);

        // Assert
        Assert.Single(newState.SelectedEvents);
        Assert.Contains(selectedEvent, newState.SelectedEvents);
    }

    [Fact]
    public void ReduceSelectEvent_WhenMultiSelect_ShouldAddToExisting()
    {
        // Arrange
        var existingEvent = EventUtils.CreateTestEvent(100);
        var state = new EventLogState { SelectedEvents = [existingEvent] };
        var newEvent = EventUtils.CreateTestEvent(200);
        var action = new EventLogAction.SelectEvent(newEvent, true);

        // Act
        var newState = EventLogReducers.ReduceSelectEvent(state, action);

        // Assert
        Assert.Equal(2, newState.SelectedEvents.Count);
        Assert.Contains(existingEvent, newState.SelectedEvents);
        Assert.Contains(newEvent, newState.SelectedEvents);
    }

    [Fact]
    public void ReduceSelectEvent_WhenMultiSelectAndEventAlreadySelected_ShouldRemoveEvent()
    {
        // Arrange
        var selectedEvent = EventUtils.CreateTestEvent(100);
        var state = new EventLogState { SelectedEvents = [selectedEvent] };
        var action = new EventLogAction.SelectEvent(selectedEvent, true);

        // Act
        var newState = EventLogReducers.ReduceSelectEvent(state, action);

        // Assert
        Assert.Empty(newState.SelectedEvents);
    }

    [Fact]
    public void ReduceSelectEvent_WhenNotMultiSelect_ShouldReplaceExisting()
    {
        // Arrange
        var existingEvent = EventUtils.CreateTestEvent(100);
        var state = new EventLogState { SelectedEvents = [existingEvent] };
        var newEvent = EventUtils.CreateTestEvent(200);
        var action = new EventLogAction.SelectEvent(newEvent);

        // Act
        var newState = EventLogReducers.ReduceSelectEvent(state, action);

        // Assert
        Assert.Single(newState.SelectedEvents);
        Assert.Contains(newEvent, newState.SelectedEvents);
        Assert.DoesNotContain(existingEvent, newState.SelectedEvents);
    }

    [Fact]
    public void ReduceSelectEvent_WhenShouldStaySelected_ShouldNotModifyState()
    {
        // Arrange
        var selectedEvent = EventUtils.CreateTestEvent(100);
        var state = new EventLogState { SelectedEvents = [selectedEvent] };
        var action = new EventLogAction.SelectEvent(selectedEvent, false, true);

        // Act
        var newState = EventLogReducers.ReduceSelectEvent(state, action);

        // Assert
        Assert.Same(state, newState);
    }

    [Fact]
    public void ReduceSelectEvents_ShouldAddNewEventsOnly()
    {
        // Arrange
        var existingEvent = EventUtils.CreateTestEvent(100);
        var state = new EventLogState { SelectedEvents = [existingEvent] };

        var newEvents = new List<DisplayEventModel>
        {
            existingEvent,                  // Already selected
            EventUtils.CreateTestEvent(200) // New
        };

        var action = new EventLogAction.SelectEvents(newEvents);

        // Act
        var newState = EventLogReducers.ReduceSelectEvents(state, action);

        // Assert
        Assert.Equal(2, newState.SelectedEvents.Count);
    }

    [Fact]
    public void ReduceSelectEvents_WhenAllEventsAlreadySelected_ShouldNotAddDuplicates()
    {
        // Arrange
        var existingEvent = EventUtils.CreateTestEvent(100);
        var state = new EventLogState { SelectedEvents = [existingEvent] };
        var action = new EventLogAction.SelectEvents([existingEvent]);

        // Act
        var newState = EventLogReducers.ReduceSelectEvents(state, action);

        // Assert
        Assert.Single(newState.SelectedEvents);
    }

    [Fact]
    public void ReduceSetContinuouslyUpdate_ShouldSetFlag()
    {
        // Arrange
        var state = new EventLogState { ContinuouslyUpdate = false };
        var action = new EventLogAction.SetContinuouslyUpdate(true);

        // Act
        var newState = EventLogReducers.ReduceSetContinuouslyUpdate(state, action);

        // Assert
        Assert.True(newState.ContinuouslyUpdate);
    }

    [Fact]
    public void ReduceSetFilters_WhenFilterChanged_ShouldUpdateFilter()
    {
        // Arrange
        var state = new EventLogState();

        var after = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var before = new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc);
        var newFilter = new EventFilter(new FilterDateModel { After = after, Before = before }, []);

        var action = new EventLogAction.SetFilters(newFilter);

        // Act
        var newState = EventLogReducers.ReduceSetFilters(state, action);

        // Assert
        Assert.Equal(newFilter, newState.AppliedFilter);
    }

    [Fact]
    public void ReduceSetFilters_WhenFilterUnchanged_ShouldReturnSameState()
    {
        // Arrange
        var filter = new EventFilter(null, []);
        var state = new EventLogState { AppliedFilter = filter };
        var action = new EventLogAction.SetFilters(filter);

        // Act
        var newState = EventLogReducers.ReduceSetFilters(state, action);

        // Assert
        Assert.Same(state, newState);
    }
}
