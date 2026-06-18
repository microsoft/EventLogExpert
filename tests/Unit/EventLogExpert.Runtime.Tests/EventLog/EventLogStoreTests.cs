// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Filtering.TestUtils.Constants;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.Tests.TestUtils.Constants;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.EventLog;

public sealed class EventLogStoreTests
{
    [Fact]
    public void BufferedEventsWorkflow_ShouldHandleCorrectly()
    {
        // Arrange
        var state = new EventLogState { ContinuouslyUpdate = false };

        var events = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(100),
            FilterEventBuilder.CreateTestEvent(200)
        };

        // Act - Buffer events
        state = Reducers.ReduceEventBuffered(state, new EventBufferedAction(events, false));

        // Assert
        Assert.Equal(2, state.NewEventBuffer.Count);
        Assert.False(state.NewEventBufferIsFull);

        // Act - Set continuously update
        state = Reducers.ReduceSetContinuouslyUpdate(state, new SetContinuouslyUpdateAction(true));

        // Assert
        Assert.True(state.ContinuouslyUpdate);
    }

    [Fact]
    public void EventLogAction_ApplyFilter_ShouldStoreFilter()
    {
        // Arrange
        var filter = new Filter(null, []);

        // Act
        var action = new ApplyFilterAction(filter);

        // Assert
        Assert.Equal(filter, action.Filter);
    }

    [Fact]
    public void EventLogAction_OpenLog_WithCancellationToken_ShouldStoreToken()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var action = new OpenLogAction(Constants.LogNameTestLog, LogPathType.File, cts.Token);

        // Assert
        Assert.Equal(cts.Token, action.Token);
    }

    [Fact]
    public void EventLogAction_SelectEvent_DefaultFlags_ShouldBeFalse()
    {
        // Arrange
        var selectedEvent = FilterEventBuilder.CreateTestEvent(100);

        // Act
        var action = new SelectEventAction(selectedEvent);

        // Assert
        Assert.False(action.IsMultiSelect);
        Assert.False(action.ShouldStaySelected);
    }

    [Fact]
    public void EventLogAction_SelectEvent_ShouldStoreEventAndSelectionFlags()
    {
        // Arrange
        var selectedEvent = FilterEventBuilder.CreateTestEvent(100);

        // Act
        var action = new SelectEventAction(selectedEvent, true, true);

        // Assert
        Assert.Equal(selectedEvent, action.SelectedEvent);
        Assert.True(action.IsMultiSelect);
        Assert.True(action.ShouldStaySelected);
    }

    [Fact]
    public void EventLogAction_SelectEvents_ShouldStoreMultipleEvents()
    {
        // Arrange
        var events = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(100),
            FilterEventBuilder.CreateTestEvent(200)
        };

        // Act
        var action = new SelectEventsAction(events);

        // Assert
        Assert.Equal(2, action.SelectedEvents.Count);
    }

    [Fact]
    public void EventLogAction_SetContinuouslyUpdate_ShouldStoreFlag()
    {
        // Act
        var actionTrue = new SetContinuouslyUpdateAction(true);
        var actionFalse = new SetContinuouslyUpdateAction(false);

        // Assert
        Assert.True(actionTrue.ContinuouslyUpdate);
        Assert.False(actionFalse.ContinuouslyUpdate);
    }

    [Fact]
    public void EventLogData_Constructor_ShouldSetProperties()
    {
        // Arrange
        var name = Constants.LogNameTestLog;
        var type = LogPathType.Channel;

        // Act
        var logData = new EventLogData(name, type);

        // Assert
        Assert.Equal(name, logData.Name);
        Assert.Equal(type, logData.Type);
    }

    [Fact]
    public void EventLogData_Id_ShouldBeUnique()
    {
        // Arrange & Act
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel);
        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel);

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

        state = Reducers.ReduceOpenLog(state,
            new OpenLogAction(Constants.LogNameTestLog, LogPathType.Channel));

        var logData = state.ActiveLogs[Constants.LogNameTestLog];

        var events = ImmutableArray.Create(
            FilterEventBuilder.CreateTestEvent(100),
            FilterEventBuilder.CreateTestEvent(200),
            FilterEventBuilder.CreateTestEvent(300)
        );

        // Act - Load events
        state = Reducers.ReduceLoadEvents(state, new LoadEventsAction(logData, events));

        // Act - Select event
        state = Reducers.ReduceSelectEvent(state, new SelectEventAction(events[0]));

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
        state = Reducers.ReduceOpenLog(state,
            new OpenLogAction(Constants.LogNameLog1, LogPathType.Channel));

        state = Reducers.ReduceOpenLog(state,
            new OpenLogAction(Constants.LogNameLog2, LogPathType.File));

        state = Reducers.ReduceOpenLog(state,
            new OpenLogAction(Constants.LogNameLog3, LogPathType.Channel));

        // Assert
        Assert.Equal(3, state.ActiveLogs.Count);

        // Act - Close one log
        var log2Id = state.ActiveLogs[Constants.LogNameLog2].Id;
        state = Reducers.ReduceCloseLog(state, new CloseLogAction(log2Id, Constants.LogNameLog2));

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
        var openAction = new OpenLogAction(Constants.LogNameTestLog, LogPathType.Channel);
        state = Reducers.ReduceOpenLog(state, openAction);

        Assert.Single(state.ActiveLogs);

        // Act - Close log
        var logId = state.ActiveLogs[Constants.LogNameTestLog].Id;
        var closeAction = new CloseLogAction(logId, Constants.LogNameTestLog);
        state = Reducers.ReduceCloseLog(state, closeAction);

        // Assert
        Assert.Empty(state.ActiveLogs);
    }

    [Fact]
    public void ReduceApplyFilter_WhenFilterChanged_ShouldUpdateFilter()
    {
        // Arrange
        var state = new EventLogState();

        var after = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var before = new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc);
        var newFilter = new Filter(new DateFilter { After = after, Before = before }, []);

        var action = new ApplyFilterAction(newFilter);

        // Act
        var newState = Reducers.ReduceApplyFilter(state, action);

        // Assert
        Assert.Equal(newFilter, newState.AppliedFilter);
    }

    [Fact]
    public void ReduceApplyFilter_WhenFilterUnchanged_ShouldReturnSameState()
    {
        // Arrange
        var filter = new Filter(null, []);
        var state = new EventLogState { AppliedFilter = filter };
        var action = new ApplyFilterAction(filter);

        // Act
        var newState = Reducers.ReduceApplyFilter(state, action);

        // Assert
        Assert.Same(state, newState);
    }

    [Fact]
    public void ReduceCloseAll_ShouldClearAllState()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);

        var state = new EventLogState
        {
            ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData),
            SelectedEvents = [FilterEventBuilder.CreateTestEvent(100)],
            NewEventBuffer = [FilterEventBuilder.CreateTestEvent(200)],
            NewEventBufferIsFull = true
        };

        // Act
        var newState = Reducers.ReduceCloseAll(state);

        // Assert
        Assert.Empty(newState.ActiveLogs);
        Assert.Empty(newState.SelectedEvents);
        Assert.Empty(newState.NewEventBuffer);
        Assert.False(newState.NewEventBufferIsFull);
    }

    [Fact]
    public void ReduceCloseAll_ShouldClearSelectedEvent()
    {
        // Arrange
        var first = FilterEventBuilder.CreateTestEvent(100);
        var state = new EventLogState { SelectedEvents = [first], SelectedEvent = first };

        // Act
        var newState = Reducers.ReduceCloseAll(state);

        // Assert
        Assert.Null(newState.SelectedEvent);
    }

    [Fact]
    public void ReduceCloseLog_ShouldFilterNewEventBuffer()
    {
        // Arrange
        var eventForLog1 = new ResolvedEvent(Constants.LogNameLog1, LogPathType.Channel)
        {
            Id = 100,
            Source = FilterTestConstants.EventSourceTestSource,
            Level = FilterTestConstants.EventLevelInformation
        };

        var eventForLog2 = new ResolvedEvent(Constants.LogNameLog2, LogPathType.Channel)
        {
            Id = 200,
            Source = FilterTestConstants.EventSourceTestSource,
            Level = FilterTestConstants.EventLevelInformation
        };

        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel);

        var state = new EventLogState
        {
            ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameLog1, logData1),
            NewEventBuffer = [eventForLog1, eventForLog2]
        };

        var action = new CloseLogAction(logData1.Id, Constants.LogNameLog1);

        // Act
        var newState = Reducers.ReduceCloseLog(state, action);

        // Assert
        Assert.Single(newState.NewEventBuffer);
        Assert.Equal(Constants.LogNameLog2, newState.NewEventBuffer.First().OwningLog);
    }

    [Fact]
    public void ReduceCloseLog_ShouldRemoveSpecifiedLog()
    {
        // Arrange
        var logData1 = new EventLogData(Constants.LogNameLog1, LogPathType.Channel);
        var logData2 = new EventLogData(Constants.LogNameLog2, LogPathType.Channel);

        var state = new EventLogState
        {
            ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty
                .Add(Constants.LogNameLog1, logData1)
                .Add(Constants.LogNameLog2, logData2)
        };

        var action = new CloseLogAction(logData1.Id, Constants.LogNameLog1);

        // Act
        var newState = Reducers.ReduceCloseLog(state, action);

        // Assert
        Assert.Single(newState.ActiveLogs);
        Assert.False(newState.ActiveLogs.ContainsKey(Constants.LogNameLog1));
        Assert.True(newState.ActiveLogs.ContainsKey(Constants.LogNameLog2));
    }

    [Fact]
    public void ReduceEventBuffered_ShouldUpdateBufferAndFullFlag()
    {
        // Arrange
        var state = new EventLogState();

        var events = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(100),
            FilterEventBuilder.CreateTestEvent(200)
        };

        var action = new EventBufferedAction(events, true);

        // Act
        var newState = Reducers.ReduceEventBuffered(state, action);

        // Assert
        Assert.Equal(2, newState.NewEventBuffer.Count);
        Assert.True(newState.NewEventBufferIsFull);
    }

    [Fact]
    public void ReduceEventBuffered_WhenNotFull_ShouldSetFullFlagFalse()
    {
        // Arrange
        var state = new EventLogState { NewEventBufferIsFull = true };
        var events = new List<ResolvedEvent> { FilterEventBuilder.CreateTestEvent(100) };
        var action = new EventBufferedAction(events, false);

        // Act
        var newState = Reducers.ReduceEventBuffered(state, action);

        // Assert
        Assert.False(newState.NewEventBufferIsFull);
    }

    [Fact]
    public void ReduceLoadEvents_WhenLogIdDoesNotMatch_ShouldReturnStateUnchanged()
    {
        // Arrange — open a log, then create stale logData with a different ID
        var state = new EventLogState();

        state = Reducers.ReduceOpenLog(state,
            new OpenLogAction(Constants.LogNameTestLog, LogPathType.Channel));

        // Create stale logData with a new ID (simulating a previous load instance)
        var staleLogData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var events = ImmutableArray.Create(FilterEventBuilder.CreateTestEvent(100));

        // Act — stale LoadEvents with mismatched ID
        var newState = Reducers.ReduceLoadEvents(state, new LoadEventsAction(staleLogData, events));

        // Assert - state unchanged, original log preserved with its ID
        var originalId = state.ActiveLogs[Constants.LogNameTestLog].Id;
        Assert.NotEqual(originalId, staleLogData.Id);
        Assert.Same(state, newState);
        Assert.Equal(originalId, newState.ActiveLogs[Constants.LogNameTestLog].Id);
    }

    [Fact]
    public void ReduceLoadEvents_WhenLogNotInActiveLogs_ShouldReturnStateUnchanged()
    {
        // Arrange — no logs open
        var state = new EventLogState();
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var events = ImmutableArray.Create(FilterEventBuilder.CreateTestEvent(100));

        // Act — stale LoadEvents arrives for a closed log
        var newState = Reducers.ReduceLoadEvents(state, new LoadEventsAction(logData, events));

        // Assert — state unchanged, log NOT resurrected
        Assert.Same(state, newState);
        Assert.Empty(newState.ActiveLogs);
    }

    [Fact]
    public void ReduceLoadEventsPartial_WhenLogIdDoesNotMatch_ShouldReturnStateUnchanged()
    {
        // Arrange
        var state = new EventLogState();

        state = Reducers.ReduceOpenLog(state,
            new OpenLogAction(Constants.LogNameTestLog, LogPathType.Channel));

        var staleLogData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var events = ImmutableArray.Create(FilterEventBuilder.CreateTestEvent(100));

        // Act — stale partial with mismatched ID
        var newState = Reducers.ReduceLoadEventsPartial(state,
            new LoadEventsPartialAction(staleLogData, events));

        // Assert — state unchanged, original log preserved with its ID
        var originalId = state.ActiveLogs[Constants.LogNameTestLog].Id;
        Assert.NotEqual(originalId, staleLogData.Id);
        Assert.Same(state, newState);
        Assert.Equal(originalId, newState.ActiveLogs[Constants.LogNameTestLog].Id);
    }

    [Fact]
    public void ReduceLoadEventsPartial_WhenLogNotInActiveLogs_ShouldReturnStateUnchanged()
    {
        // Arrange — no logs open
        var state = new EventLogState();
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var events = ImmutableArray.Create(FilterEventBuilder.CreateTestEvent(100));

        // Act
        var newState = Reducers.ReduceLoadEventsPartial(state,
            new LoadEventsPartialAction(logData, events));

        // Assert
        Assert.Same(state, newState);
    }

    [Fact]
    public void ReduceOpenLog_ShouldAddEmptyLogData()
    {
        // Arrange
        var state = new EventLogState();
        var action = new OpenLogAction(Constants.LogNameNewLog, LogPathType.Channel);

        // Act
        var newState = Reducers.ReduceOpenLog(state, action);

        // Assert
        Assert.Single(newState.ActiveLogs);
        Assert.True(newState.ActiveLogs.ContainsKey(Constants.LogNameNewLog));
    }

    [Fact]
    public void ReduceOpenLog_WhenLogAlreadyActive_ShouldReturnSameStateInstance()
    {
        // Arrange — duplicate OpenLog dispatches must be a no-op so re-opening an already
        // active log (drag/drop, command line, recent menu) cannot replace its EventLogData
        // (which would invalidate ongoing loads and reset the user's events).
        var state = new EventLogState();

        state = Reducers.ReduceOpenLog(state,
            new OpenLogAction(Constants.LogNameTestLog, LogPathType.Channel));

        var existingLogData = state.ActiveLogs[Constants.LogNameTestLog];

        // Act — dispatch the same OpenLog a second time.
        var newState = Reducers.ReduceOpenLog(state,
            new OpenLogAction(Constants.LogNameTestLog, LogPathType.Channel));

        // Assert — same state reference, same EventLogData reference (no replacement).
        Assert.Same(state, newState);
        Assert.Same(existingLogData, newState.ActiveLogs[Constants.LogNameTestLog]);
        Assert.Single(newState.ActiveLogs);
    }

    [Fact]
    public void ReduceOpenLog_WithFilePath_ShouldSetCorrectPathType()
    {
        // Arrange
        var state = new EventLogState();
        var action = new OpenLogAction(Constants.FilePathTestEvtx, LogPathType.File);

        // Act
        var newState = Reducers.ReduceOpenLog(state, action);

        // Assert
        Assert.Equal(LogPathType.File, newState.ActiveLogs[Constants.FilePathTestEvtx].Type);
    }

    [Fact]
    public void ReduceSelectEvent_OnToggleOff_ShouldKeepSelectedEvent()
    {
        // Arrange — Explorer-style: toggling off a row leaves the cursor on it.
        var first = FilterEventBuilder.CreateTestEvent(100);
        var second = FilterEventBuilder.CreateTestEvent(200);
        var state = new EventLogState { SelectedEvents = [first, second], SelectedEvent = first };
        var action = new SelectEventAction(second, true);

        // Act
        var newState = Reducers.ReduceSelectEvent(state, action);

        // Assert
        Assert.DoesNotContain(second, newState.SelectedEvents);
        Assert.Same(second, newState.SelectedEvent);
    }

    [Fact]
    public void ReduceSelectEvent_ShouldSetSelectedEventOnAdd()
    {
        // Arrange
        var first = FilterEventBuilder.CreateTestEvent(100);
        var second = FilterEventBuilder.CreateTestEvent(200);
        var state = new EventLogState { SelectedEvents = [first], SelectedEvent = first };
        var action = new SelectEventAction(second, true);

        // Act
        var newState = Reducers.ReduceSelectEvent(state, action);

        // Assert
        Assert.Contains(second, newState.SelectedEvents);
        Assert.Same(second, newState.SelectedEvent);
    }

    [Fact]
    public void ReduceSelectEvent_WhenEventNotSelected_ShouldAddEvent()
    {
        // Arrange
        var state = new EventLogState();
        var selectedEvent = FilterEventBuilder.CreateTestEvent(100);
        var action = new SelectEventAction(selectedEvent);

        // Act
        var newState = Reducers.ReduceSelectEvent(state, action);

        // Assert
        Assert.Single(newState.SelectedEvents);
        Assert.Contains(selectedEvent, newState.SelectedEvents);
    }

    [Fact]
    public void ReduceSelectEvent_WhenMultiSelect_ShouldAddToExisting()
    {
        // Arrange
        var existingEvent = FilterEventBuilder.CreateTestEvent(100);
        var state = new EventLogState { SelectedEvents = [existingEvent] };
        var newEvent = FilterEventBuilder.CreateTestEvent(200);
        var action = new SelectEventAction(newEvent, true);

        // Act
        var newState = Reducers.ReduceSelectEvent(state, action);

        // Assert
        Assert.Equal(2, newState.SelectedEvents.Count);
        Assert.Contains(existingEvent, newState.SelectedEvents);
        Assert.Contains(newEvent, newState.SelectedEvents);
    }

    [Fact]
    public void ReduceSelectEvent_WhenMultiSelectAndEventAlreadySelected_ShouldRemoveEvent()
    {
        // Arrange
        var selectedEvent = FilterEventBuilder.CreateTestEvent(100);
        var state = new EventLogState { SelectedEvents = [selectedEvent] };
        var action = new SelectEventAction(selectedEvent, true);

        // Act
        var newState = Reducers.ReduceSelectEvent(state, action);

        // Assert
        Assert.Empty(newState.SelectedEvents);
    }

    [Fact]
    public void ReduceSelectEvent_WhenNotMultiSelect_ShouldReplaceExisting()
    {
        // Arrange
        var existingEvent = FilterEventBuilder.CreateTestEvent(100);
        var state = new EventLogState { SelectedEvents = [existingEvent] };
        var newEvent = FilterEventBuilder.CreateTestEvent(200);
        var action = new SelectEventAction(newEvent);

        // Act
        var newState = Reducers.ReduceSelectEvent(state, action);

        // Assert
        Assert.Single(newState.SelectedEvents);
        Assert.Contains(newEvent, newState.SelectedEvents);
        Assert.DoesNotContain(existingEvent, newState.SelectedEvents);
    }

    [Fact]
    public void ReduceSelectEvent_WhenShouldStaySelected_ShouldOnlyUpdateSelectedEvent()
    {
        // Arrange — ShouldStaySelected (right-click on an already-selected row)
        // moves the focus cursor to that row but doesn't change the selection list.
        var selectedEvent = FilterEventBuilder.CreateTestEvent(100);
        var state = new EventLogState { SelectedEvents = [selectedEvent], SelectedEvent = null };
        var action = new SelectEventAction(selectedEvent, false, true);

        // Act
        var newState = Reducers.ReduceSelectEvent(state, action);

        // Assert
        Assert.Same(state.SelectedEvents, newState.SelectedEvents);
        Assert.Same(selectedEvent, newState.SelectedEvent);
    }

    [Fact]
    public void ReduceSelectEvent_WhenValueEqualButDifferentReference_ShouldNotMatchExisting()
    {
        // Arrange — simulates post-reload state where SelectedEvents holds a
        // stale reference and the user clicks a value-equal new instance.
        var staleReference = FilterEventBuilder.CreateTestEvent(100, recordId: 5);
        var freshReference = FilterEventBuilder.CreateTestEvent(100, recordId: 5);
        var state = new EventLogState { SelectedEvents = [staleReference] };
        var action = new SelectEventAction(freshReference, true);

        // Act
        var newState = Reducers.ReduceSelectEvent(state, action);

        // Assert — fresh reference should be added (not treated as already
        // selected), giving us two entries.
        Assert.Equal(2, newState.SelectedEvents.Count);
        Assert.Same(staleReference, newState.SelectedEvents[0]);
        Assert.Same(freshReference, newState.SelectedEvents[1]);
    }

    [Fact]
    public void ReduceSelectEvents_ShouldAddNewEventsOnly()
    {
        // Arrange
        var existingEvent = FilterEventBuilder.CreateTestEvent(100);
        var state = new EventLogState { SelectedEvents = [existingEvent] };

        var newEvents = new List<ResolvedEvent>
        {
            existingEvent,                          // Already selected
            FilterEventBuilder.CreateTestEvent(200) // New
        };

        var action = new SelectEventsAction(newEvents);

        // Act
        var newState = Reducers.ReduceSelectEvents(state, action);

        // Assert
        Assert.Equal(2, newState.SelectedEvents.Count);
    }

    [Fact]
    public void ReduceSelectEvents_ShouldPreserveSelectedEventIfStillPresent()
    {
        // Arrange
        var first = FilterEventBuilder.CreateTestEvent(100);
        var second = FilterEventBuilder.CreateTestEvent(200);
        var third = FilterEventBuilder.CreateTestEvent(300);
        var state = new EventLogState { SelectedEvents = [first, second], SelectedEvent = second };
        var action = new SelectEventsAction([second, third]);

        // Act
        var newState = Reducers.ReduceSelectEvents(state, action);

        // Assert — active was already in incoming so it stays.
        Assert.Same(second, newState.SelectedEvent);
    }

    [Fact]
    public void ReduceSelectEvents_WhenAllEventsAlreadySelected_ShouldNotAddDuplicates()
    {
        // Arrange
        var existingEvent = FilterEventBuilder.CreateTestEvent(100);
        var state = new EventLogState { SelectedEvents = [existingEvent] };
        var action = new SelectEventsAction([existingEvent]);

        // Act
        var newState = Reducers.ReduceSelectEvents(state, action);

        // Assert
        Assert.Single(newState.SelectedEvents);
    }

    [Fact]
    public void ReduceSelectEvents_WhenSelectedEventDropped_ShouldFallbackToLastIncoming()
    {
        // Arrange — additive SelectEvents with a null active event (the prior
        // selection had no focus) falls back to the last incoming event so the
        // restore path leaves the user with something focused.
        var second = FilterEventBuilder.CreateTestEvent(200);
        var third = FilterEventBuilder.CreateTestEvent(300);
        var state = new EventLogState { SelectedEvents = [], SelectedEvent = null };
        var action = new SelectEventsAction([second, third]);

        // Act
        var newState = Reducers.ReduceSelectEvents(state, action);

        // Assert
        Assert.Same(third, newState.SelectedEvent);
    }

    [Fact]
    public void ReduceSetContinuouslyUpdate_ShouldSetFlag()
    {
        // Arrange
        var state = new EventLogState { ContinuouslyUpdate = false };
        var action = new SetContinuouslyUpdateAction(true);

        // Act
        var newState = Reducers.ReduceSetContinuouslyUpdate(state, action);

        // Assert
        Assert.True(newState.ContinuouslyUpdate);
    }

    [Fact]
    public void ReduceSetSelectedEvents_ShouldReplaceSelectionPreservingOrder()
    {
        // Arrange
        var existingEvent = FilterEventBuilder.CreateTestEvent(100);
        var state = new EventLogState { SelectedEvents = [existingEvent] };
        var first = FilterEventBuilder.CreateTestEvent(200);
        var second = FilterEventBuilder.CreateTestEvent(300);
        var third = FilterEventBuilder.CreateTestEvent(400);
        var action = new SetSelectedEventsAction([first, second, third], third);

        // Act
        var newState = Reducers.ReduceSetSelectedEvents(state, action);

        // Assert — replaces existingEvent and preserves the caller-provided
        // selection order. The active/focused event is tracked separately via
        // EventLogState.SelectedEvent rather than inferred from the tail.
        Assert.Equal(3, newState.SelectedEvents.Count);
        Assert.Same(first, newState.SelectedEvents[0]);
        Assert.Same(second, newState.SelectedEvents[1]);
        Assert.Same(third, newState.SelectedEvents[2]);
    }

    [Fact]
    public void ReduceSetSelectedEvents_ShouldSetSelectedEventIndependentOfMembership()
    {
        // Arrange — active is not required to be a member of selection.
        var first = FilterEventBuilder.CreateTestEvent(100);
        var second = FilterEventBuilder.CreateTestEvent(200);
        var notInSelection = FilterEventBuilder.CreateTestEvent(300);
        var state = new EventLogState();
        var action = new SetSelectedEventsAction([first, second], notInSelection);

        // Act
        var newState = Reducers.ReduceSetSelectedEvents(state, action);

        // Assert
        Assert.Equal([first, second], newState.SelectedEvents);
        Assert.Same(notInSelection, newState.SelectedEvent);
    }

    [Fact]
    public void ReduceSetSelectedEvents_WhenInputContainsDuplicates_ShouldDistinctByReference()
    {
        // Arrange
        var first = FilterEventBuilder.CreateTestEvent(100);
        var second = FilterEventBuilder.CreateTestEvent(200);
        var state = new EventLogState();
        var action = new SetSelectedEventsAction([first, second, first], first);

        // Act
        var newState = Reducers.ReduceSetSelectedEvents(state, action);

        // Assert
        Assert.Equal(2, newState.SelectedEvents.Count);
        Assert.Same(first, newState.SelectedEvents[0]);
        Assert.Same(second, newState.SelectedEvents[1]);
    }

    [Fact]
    public void ReduceSetSelectedEvents_WhenInputIsEmpty_ShouldClearSelection()
    {
        // Arrange
        var state = new EventLogState { SelectedEvents = [FilterEventBuilder.CreateTestEvent(100)] };
        var action = new SetSelectedEventsAction([], null);

        // Act
        var newState = Reducers.ReduceSetSelectedEvents(state, action);

        // Assert
        Assert.Empty(newState.SelectedEvents);
    }

    [Fact]
    public void ReduceSetSelectedEvents_WhenOnlySelectedEventChanged_ShouldUpdateOnlySelectedEvent()
    {
        // Arrange
        var first = FilterEventBuilder.CreateTestEvent(100);
        var second = FilterEventBuilder.CreateTestEvent(200);
        var state = new EventLogState { SelectedEvents = [first, second], SelectedEvent = first };
        var action = new SetSelectedEventsAction([first, second], second);

        // Act
        var newState = Reducers.ReduceSetSelectedEvents(state, action);

        // Assert — selection list is preserved by reference (no churn) but active changed.
        Assert.Same(state.SelectedEvents, newState.SelectedEvents);
        Assert.Same(second, newState.SelectedEvent);
    }

    [Fact]
    public void ReduceSetSelectedEvents_WhenSelectionUnchanged_ShouldReturnSameStateReference()
    {
        // Arrange — EventTable.ShouldRender uses ReferenceEquals on
        // SelectedEvents to short-circuit re-renders, so the reducer must
        // return the same state when selection content is unchanged.
        var first = FilterEventBuilder.CreateTestEvent(100);
        var second = FilterEventBuilder.CreateTestEvent(200);
        var state = new EventLogState { SelectedEvents = [first, second], SelectedEvent = second };
        var action = new SetSelectedEventsAction([first, second], second);

        // Act
        var newState = Reducers.ReduceSetSelectedEvents(state, action);

        // Assert
        Assert.Same(state, newState);
    }
}
