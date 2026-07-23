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
    private static readonly EventLogId s_selectionLogId = EventLogId.Create();

    [Fact]
    public void BufferedEventsWorkflow_ShouldHandleCorrectly()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);

        var state = new EventLogState
        {
            ContinuouslyUpdate = false,
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty
                .Add(Constants.LogNameTestLog, new OpenLogInfo(logData.Id, LogPathType.Channel))
        };

        // Act: buffer two events additively (newest first).
        state = Reducers.ReduceAddEvent(state, new AddEventAction(FilterEventBuilder.CreateTestEvent(100, logName: Constants.LogNameTestLog)));
        state = Reducers.ReduceAddEvent(state, new AddEventAction(FilterEventBuilder.CreateTestEvent(200, logName: Constants.LogNameTestLog)));

        // Assert
        Assert.Equal(2, state.NewEventBuffer.Count);
        Assert.False(state.NewEventBufferIsFull);

        // Act: Set continuously update
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
        var selection = Entry(0);

        // Act
        var action = new SelectEventAction(selection);

        // Assert
        Assert.False(action.IsMultiSelect);
        Assert.False(action.ShouldStaySelected);
    }

    [Fact]
    public void EventLogAction_SelectEvent_ShouldStoreEventAndSelectionFlags()
    {
        // Arrange
        var selection = Entry(0);

        // Act
        var action = new SelectEventAction(selection, true, true);

        // Assert
        Assert.Equal(selection, action.Selection);
        Assert.True(action.IsMultiSelect);
        Assert.True(action.ShouldStaySelected);
    }

    [Fact]
    public void EventLogAction_SelectEvents_ShouldStoreMultipleEvents()
    {
        // Arrange
        var selections = new List<SelectionEntry>
        {
            Entry(0),
            Entry(1)
        };

        // Act
        var action = new SelectEventsAction(selections);

        // Assert
        Assert.Equal(2, action.Selection.Count);
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
        Assert.Equal(0, state.OpenLogCount);
        Assert.Null(state.AppliedFilter.DateFilter);
        Assert.Empty(state.AppliedFilter.Filters);
        Assert.False(state.ContinuouslyUpdate);
        Assert.Empty(state.NewEventBuffer);
        Assert.False(state.NewEventBufferIsFull);
        Assert.Empty(state.Selection);
        Assert.Null(state.Focus);
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

        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel) { Id = state.OpenLogs[Constants.LogNameTestLog].Id };

        var events = ImmutableArray.Create(
            FilterEventBuilder.CreateTestEvent(100),
            FilterEventBuilder.CreateTestEvent(200),
            FilterEventBuilder.CreateTestEvent(300)
        );

        // Act: Load events
        state = Reducers.ReduceLoadEvents(state, new LoadEventsAction(logData, events));

        // Act: Select event
        var selection = Entry(logData.Id, index: 0);
        state = Reducers.ReduceSelectEvent(state, new SelectEventAction(selection));

        // Assert
        Assert.Single(state.Selection);
        Assert.Equal(selection, state.Selection[0]);
    }

    [Fact]
    public void MultipleLogOperations_ShouldMaintainCorrectState()
    {
        // Arrange
        var state = new EventLogState();

        // Act: Open multiple logs
        state = Reducers.ReduceOpenLog(state,
            new OpenLogAction(Constants.LogNameLog1, LogPathType.Channel));

        state = Reducers.ReduceOpenLog(state,
            new OpenLogAction(Constants.LogNameLog2, LogPathType.File));

        state = Reducers.ReduceOpenLog(state,
            new OpenLogAction(Constants.LogNameLog3, LogPathType.Channel));

        // Assert
        Assert.Equal(3, state.OpenLogCount);

        // Act: Close one log
        var log2Id = state.OpenLogs[Constants.LogNameLog2].Id;
        state = Reducers.ReduceCloseLog(state, new CloseLogAction(log2Id, Constants.LogNameLog2));

        // Assert
        Assert.Equal(2, state.OpenLogCount);
        Assert.False(state.IsLogOpen(Constants.LogNameLog2));
        Assert.True(state.IsLogOpen(Constants.LogNameLog1));
        Assert.True(state.IsLogOpen(Constants.LogNameLog3));
    }

    [Fact]
    public void OpenAndCloseLog_ShouldMaintainStateConsistency()
    {
        // Arrange
        var state = new EventLogState();

        // Act: Open log
        var openAction = new OpenLogAction(Constants.LogNameTestLog, LogPathType.Channel);
        state = Reducers.ReduceOpenLog(state, openAction);

        Assert.Equal(1, state.OpenLogCount);

        // Act: Close log
        var logId = state.OpenLogs[Constants.LogNameTestLog].Id;
        var closeAction = new CloseLogAction(logId, Constants.LogNameTestLog);
        state = Reducers.ReduceCloseLog(state, closeAction);

        // Assert
        Assert.Equal(0, state.OpenLogCount);
    }

    [Fact]
    public void ReduceAddEvent_TwoSequentialEvents_BothPreservedNewestFirst()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var state = new EventLogState
        {
            ContinuouslyUpdate = false,
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty
                .Add(Constants.LogNameTestLog, new OpenLogInfo(logData.Id, LogPathType.Channel))
        };

        var first = FilterEventBuilder.CreateTestEvent(100, logName: Constants.LogNameTestLog);
        var second = FilterEventBuilder.CreateTestEvent(200, logName: Constants.LogNameTestLog);

        // Act: each add composes against current state, so the second cannot clobber the first (the pre-fix whole-buffer
        // effect write could).
        state = Reducers.ReduceAddEvent(state, new AddEventAction(first));
        state = Reducers.ReduceAddEvent(state, new AddEventAction(second));

        // Assert
        Assert.Equal(2, state.NewEventBuffer.Count);
        Assert.Same(second, state.NewEventBuffer[0]);
        Assert.Same(first, state.NewEventBuffer[1]);
    }

    [Fact]
    public void ReduceAddEvent_WhenContinuouslyUpdating_DoesNotBuffer()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var state = new EventLogState
        {
            ContinuouslyUpdate = true,
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty
                .Add(Constants.LogNameTestLog, new OpenLogInfo(logData.Id, LogPathType.Channel))
        };

        // Act
        var result = Reducers.ReduceAddEvent(
            state, new AddEventAction(FilterEventBuilder.CreateTestEvent(100, logName: Constants.LogNameTestLog)));

        // Assert: buffering is the live-tail effect's job when continuously updating, not the reducer's.
        Assert.Same(state, result);
        Assert.Empty(result.NewEventBuffer);
    }

    [Fact]
    public void ReduceAddEvent_WhenLogNotOpen_DoesNotBuffer()
    {
        // Arrange
        var state = new EventLogState { ContinuouslyUpdate = false };

        // Act
        var result = Reducers.ReduceAddEvent(
            state, new AddEventAction(FilterEventBuilder.CreateTestEvent(100, logName: Constants.LogNameTestLog)));

        // Assert
        Assert.Same(state, result);
        Assert.Empty(result.NewEventBuffer);
    }

    [Fact]
    public void ReduceAddEvent_WhenLogOpenAndNotContinuouslyUpdating_PrependsEventAndRecomputesFull()
    {
        // Arrange: buffer already at MaxNewEvents - 1 so the next add flips the full flag.
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var existing = Enumerable.Range(0, EventLogState.MaxNewEvents - 1)
            .Select(i => FilterEventBuilder.CreateTestEvent(i, logName: Constants.LogNameTestLog))
            .ToList();

        var state = new EventLogState
        {
            ContinuouslyUpdate = false,
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty
                .Add(Constants.LogNameTestLog, new OpenLogInfo(logData.Id, LogPathType.Channel)),
            NewEventBuffer = existing
        };

        var newEvent = FilterEventBuilder.CreateTestEvent(9999, logName: Constants.LogNameTestLog);

        // Act
        var result = Reducers.ReduceAddEvent(state, new AddEventAction(newEvent));

        // Assert: prepended (newest first) and the full flag recomputed.
        Assert.Equal(EventLogState.MaxNewEvents, result.NewEventBuffer.Count);
        Assert.Same(newEvent, result.NewEventBuffer[0]);
        Assert.True(result.NewEventBufferIsFull);
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
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty.Add(Constants.LogNameTestLog, new OpenLogInfo(logData.Id, LogPathType.Channel)),
            Selection = [Entry(0)],
            NewEventBuffer = [FilterEventBuilder.CreateTestEvent(200)],
            NewEventBufferIsFull = true
        };

        // Act
        var newState = Reducers.ReduceCloseAll(state);

        // Assert
        Assert.Equal(0, newState.OpenLogCount);
        Assert.Empty(newState.Selection);
        Assert.Empty(newState.NewEventBuffer);
        Assert.False(newState.NewEventBufferIsFull);
    }

    [Fact]
    public void ReduceCloseAll_ShouldClearFocus()
    {
        // Arrange
        var first = Entry(0);
        var state = new EventLogState { Selection = [first], Focus = first };

        // Act
        var newState = Reducers.ReduceCloseAll(state);

        // Assert
        Assert.Null(newState.Focus);
    }

    [Fact]
    public void ReduceCloseLog_ShouldDropSelectionsAndFocusForClosedLog()
    {
        // Arrange: a reload closes and reopens a log; selections addressing the
        // closed log's generation must be dropped (their handles reference a
        // now-defunct generation) while selections for other open logs survive.
        var closingLogId = EventLogId.Create();
        var otherLogId = EventLogId.Create();
        var closingSelection = Entry(closingLogId, index: 0);
        var survivingSelection = Entry(otherLogId, index: 0);

        var state = new EventLogState
        {
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty
                .Add(Constants.LogNameLog1, new OpenLogInfo(closingLogId, LogPathType.Channel))
                .Add(Constants.LogNameLog2, new OpenLogInfo(otherLogId, LogPathType.Channel)),
            Selection = [closingSelection, survivingSelection],
            Focus = closingSelection
        };

        var action = new CloseLogAction(closingLogId, Constants.LogNameLog1);

        // Act
        var newState = Reducers.ReduceCloseLog(state, action);

        // Assert: only the closed log's selection is dropped; focus clears because
        // it belonged to the closed log; the other log's selection is preserved.
        Assert.Single(newState.Selection);
        Assert.Equal(survivingSelection, newState.Selection[0]);
        Assert.Null(newState.Focus);
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
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty.Add(Constants.LogNameLog1, new OpenLogInfo(logData1.Id, LogPathType.Channel)),
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
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty
                .Add(Constants.LogNameLog1, new OpenLogInfo(logData1.Id, LogPathType.Channel))
                .Add(Constants.LogNameLog2, new OpenLogInfo(logData2.Id, LogPathType.Channel))
        };

        var action = new CloseLogAction(logData1.Id, Constants.LogNameLog1);

        // Act
        var newState = Reducers.ReduceCloseLog(state, action);

        // Assert
        Assert.Equal(1, newState.OpenLogCount);
        Assert.False(newState.IsLogOpen(Constants.LogNameLog1));
        Assert.True(newState.IsLogOpen(Constants.LogNameLog2));
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

        // Act: stale partial with mismatched ID
        var newState = Reducers.ReduceLoadEventsPartial(state,
            new LoadEventsPartialAction(staleLogData, events));

        // Assert: state unchanged, original log preserved with its ID
        var originalId = state.OpenLogs[Constants.LogNameTestLog].Id;
        Assert.NotEqual(originalId, staleLogData.Id);
        Assert.Same(state, newState);
        Assert.Equal(originalId, newState.OpenLogs[Constants.LogNameTestLog].Id);
    }

    [Fact]
    public void ReduceLoadEventsPartial_WhenLogNotInOpenLogs_ShouldReturnStateUnchanged()
    {
        // Arrange: no logs open
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
    public void ReduceLoadEvents_WhenLogIdDoesNotMatch_ShouldReturnStateUnchanged()
    {
        // Arrange: open a log, then create stale logData with a different ID
        var state = new EventLogState();

        state = Reducers.ReduceOpenLog(state,
            new OpenLogAction(Constants.LogNameTestLog, LogPathType.Channel));

        // Create stale logData with a new ID (simulating a previous load instance)
        var staleLogData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var events = ImmutableArray.Create(FilterEventBuilder.CreateTestEvent(100));

        // Act: stale LoadEvents with mismatched ID
        var newState = Reducers.ReduceLoadEvents(state, new LoadEventsAction(staleLogData, events));

        // Assert: state unchanged, original log preserved with its ID
        var originalId = state.OpenLogs[Constants.LogNameTestLog].Id;
        Assert.NotEqual(originalId, staleLogData.Id);
        Assert.Same(state, newState);
        Assert.Equal(originalId, newState.OpenLogs[Constants.LogNameTestLog].Id);
    }

    [Fact]
    public void ReduceLoadEvents_WhenLogNotInOpenLogs_ShouldReturnStateUnchanged()
    {
        // Arrange: no logs open
        var state = new EventLogState();
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);
        var events = ImmutableArray.Create(FilterEventBuilder.CreateTestEvent(100));

        // Act: stale LoadEvents arrives for a closed log
        var newState = Reducers.ReduceLoadEvents(state, new LoadEventsAction(logData, events));

        // Assert: state unchanged, log NOT resurrected
        Assert.Same(state, newState);
        Assert.Equal(0, newState.OpenLogCount);
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
        Assert.Equal(1, newState.OpenLogCount);
        Assert.True(newState.IsLogOpen(Constants.LogNameNewLog));
    }

    [Fact]
    public void ReduceOpenLog_WhenLogAlreadyActive_ShouldReturnSameStateInstance()
    {
        // Arrange: duplicate OpenLog dispatches must be a no-op so re-opening an already
        // active log (drag/drop, command line, recent menu) cannot replace its EventLogData
        // (which would invalidate ongoing loads and reset the user's events).
        var state = new EventLogState();

        state = Reducers.ReduceOpenLog(state,
            new OpenLogAction(Constants.LogNameTestLog, LogPathType.Channel));

        var existingLogId = state.OpenLogs[Constants.LogNameTestLog].Id;

        // Act: dispatch the same OpenLog a second time.
        var newState = Reducers.ReduceOpenLog(state,
            new OpenLogAction(Constants.LogNameTestLog, LogPathType.Channel));

        // Assert: same state reference, same EventLogData reference (no replacement).
        Assert.Same(state, newState);
        Assert.Equal(existingLogId, newState.OpenLogs[Constants.LogNameTestLog].Id);
        Assert.Equal(1, newState.OpenLogCount);
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
        Assert.Equal(LogPathType.File, newState.OpenLogs[Constants.FilePathTestEvtx].Type);
    }

    [Fact]
    public void ReduceSelectEvent_OnToggleOff_ShouldKeepFocus()
    {
        // Arrange: Explorer-style: toggling off a row leaves the cursor on it.
        var first = Entry(0);
        var second = Entry(1);
        var state = new EventLogState { Selection = [first, second], Focus = first };
        var action = new SelectEventAction(second, true);

        // Act
        var newState = Reducers.ReduceSelectEvent(state, action);

        // Assert
        Assert.DoesNotContain(second, newState.Selection);
        Assert.Equal(second, newState.Focus);
    }

    [Fact]
    public void ReduceSelectEvent_ShouldSetFocusOnAdd()
    {
        // Arrange
        var first = Entry(0);
        var second = Entry(1);
        var state = new EventLogState { Selection = [first], Focus = first };
        var action = new SelectEventAction(second, true);

        // Act
        var newState = Reducers.ReduceSelectEvent(state, action);

        // Assert
        Assert.Contains(second, newState.Selection);
        Assert.Equal(second, newState.Focus);
    }

    [Fact]
    public void ReduceSelectEvent_WhenEventNotSelected_ShouldAddEvent()
    {
        // Arrange
        var state = new EventLogState();
        var selection = Entry(0);
        var action = new SelectEventAction(selection);

        // Act
        var newState = Reducers.ReduceSelectEvent(state, action);

        // Assert
        Assert.Single(newState.Selection);
        Assert.Contains(selection, newState.Selection);
    }

    [Fact]
    public void ReduceSelectEvent_WhenMultiSelectAndEventAlreadySelected_ShouldRemoveEvent()
    {
        // Arrange
        var selection = Entry(0);
        var state = new EventLogState { Selection = [selection] };
        var action = new SelectEventAction(selection, true);

        // Act
        var newState = Reducers.ReduceSelectEvent(state, action);

        // Assert
        Assert.Empty(newState.Selection);
    }

    [Fact]
    public void ReduceSelectEvent_WhenMultiSelect_ShouldAddToExisting()
    {
        // Arrange
        var existing = Entry(0);
        var state = new EventLogState { Selection = [existing] };
        var incoming = Entry(1);
        var action = new SelectEventAction(incoming, true);

        // Act
        var newState = Reducers.ReduceSelectEvent(state, action);

        // Assert
        Assert.Equal(2, newState.Selection.Count);
        Assert.Contains(existing, newState.Selection);
        Assert.Contains(incoming, newState.Selection);
    }

    [Fact]
    public void ReduceSelectEvent_WhenNotMultiSelect_ShouldReplaceExisting()
    {
        // Arrange
        var existing = Entry(0);
        var state = new EventLogState { Selection = [existing] };
        var incoming = Entry(1);
        var action = new SelectEventAction(incoming);

        // Act
        var newState = Reducers.ReduceSelectEvent(state, action);

        // Assert
        Assert.Single(newState.Selection);
        Assert.Contains(incoming, newState.Selection);
        Assert.DoesNotContain(existing, newState.Selection);
    }

    [Fact]
    public void ReduceSelectEvent_WhenSameRowAcrossGenerations_ShouldNotMatchExisting()
    {
        // Arrange: simulates post-reload state where Selection holds a stale
        // handle (a prior generation) and the user clicks the same row in the
        // freshly reloaded generation. OriginHandle is generation-stamped, so the
        // two handles are distinct even though they address the same logical row.
        var stale = Entry(index: 0, generation: 0);
        var fresh = Entry(index: 0, generation: 1);
        var state = new EventLogState { Selection = [stale] };
        var action = new SelectEventAction(fresh, true);

        // Act
        var newState = Reducers.ReduceSelectEvent(state, action);

        // Assert: fresh handle should be added (not treated as already
        // selected), giving us two entries.
        Assert.Equal(2, newState.Selection.Count);
        Assert.Equal(stale, newState.Selection[0]);
        Assert.Equal(fresh, newState.Selection[1]);
    }

    [Fact]
    public void ReduceSelectEvent_WhenShouldStaySelected_ShouldOnlyUpdateFocus()
    {
        // Arrange: ShouldStaySelected (right-click on an already-selected row)
        // moves the focus cursor to that row but doesn't change the selection list.
        var selection = Entry(0);
        var state = new EventLogState { Selection = [selection], Focus = null };
        var action = new SelectEventAction(selection, false, true);

        // Act
        var newState = Reducers.ReduceSelectEvent(state, action);

        // Assert
        Assert.Same(state.Selection, newState.Selection);
        Assert.Equal(selection, newState.Focus);
    }

    [Fact]
    public void ReduceSelectEvents_ShouldAddNewEventsOnly()
    {
        // Arrange
        var existing = Entry(0);
        var state = new EventLogState { Selection = [existing] };

        var incoming = new List<SelectionEntry>
        {
            existing,   // Already selected
            Entry(1)    // New
        };

        var action = new SelectEventsAction(incoming);

        // Act
        var newState = Reducers.ReduceSelectEvents(state, action);

        // Assert
        Assert.Equal(2, newState.Selection.Count);
    }

    [Fact]
    public void ReduceSelectEvents_ShouldPreserveFocusIfStillPresent()
    {
        // Arrange
        var first = Entry(0);
        var second = Entry(1);
        var third = Entry(2);
        var state = new EventLogState { Selection = [first, second], Focus = second };
        var action = new SelectEventsAction([second, third]);

        // Act
        var newState = Reducers.ReduceSelectEvents(state, action);

        // Assert: active was already in incoming so it stays.
        Assert.Equal(second, newState.Focus);
    }

    [Fact]
    public void ReduceSelectEvents_WhenAllEventsAlreadySelected_ShouldNotAddDuplicates()
    {
        // Arrange
        var existing = Entry(0);
        var state = new EventLogState { Selection = [existing] };
        var action = new SelectEventsAction([existing]);

        // Act
        var newState = Reducers.ReduceSelectEvents(state, action);

        // Assert
        Assert.Single(newState.Selection);
    }

    [Fact]
    public void ReduceSelectEvents_WhenFocusDropped_ShouldFallbackToLastIncoming()
    {
        // Arrange: additive SelectEvents with a null focus (the prior selection
        // had no focus) falls back to the last incoming entry so the restore path
        // leaves the user with something focused.
        var second = Entry(1);
        var third = Entry(2);
        var state = new EventLogState { Selection = [], Focus = null };
        var action = new SelectEventsAction([second, third]);

        // Act
        var newState = Reducers.ReduceSelectEvents(state, action);

        // Assert
        Assert.Equal(third, newState.Focus);
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
        var existing = Entry(0);
        var state = new EventLogState { Selection = [existing] };
        var first = Entry(1);
        var second = Entry(2);
        var third = Entry(3);
        var action = new SetSelectedEventsAction([first, second, third], third);

        // Act
        var newState = Reducers.ReduceSetSelectedEvents(state, action);

        // Assert: replaces the existing entry and preserves the caller-provided
        // selection order. The focus is tracked separately via EventLogState.Focus
        // rather than inferred from the tail.
        Assert.Equal(3, newState.Selection.Count);
        Assert.Equal(first, newState.Selection[0]);
        Assert.Equal(second, newState.Selection[1]);
        Assert.Equal(third, newState.Selection[2]);
    }

    [Fact]
    public void ReduceSetSelectedEvents_ShouldSetFocusIndependentOfMembership()
    {
        // Arrange: focus is not required to be a member of the selection.
        var first = Entry(0);
        var second = Entry(1);
        var notInSelection = Entry(2);
        var state = new EventLogState();
        var action = new SetSelectedEventsAction([first, second], notInSelection);

        // Act
        var newState = Reducers.ReduceSetSelectedEvents(state, action);

        // Assert
        Assert.Equal(2, newState.Selection.Count);
        Assert.Equal(first, newState.Selection[0]);
        Assert.Equal(second, newState.Selection[1]);
        Assert.Equal(notInSelection, newState.Focus);
    }

    [Fact]
    public void ReduceSetSelectedEvents_WhenInputContainsDuplicates_ShouldDistinctByOriginHandle()
    {
        // Arrange
        var first = Entry(0);
        var second = Entry(1);
        var state = new EventLogState();
        var action = new SetSelectedEventsAction([first, second, first], first);

        // Act
        var newState = Reducers.ReduceSetSelectedEvents(state, action);

        // Assert
        Assert.Equal(2, newState.Selection.Count);
        Assert.Equal(first, newState.Selection[0]);
        Assert.Equal(second, newState.Selection[1]);
    }

    [Fact]
    public void ReduceSetSelectedEvents_WhenInputIsEmpty_ShouldClearSelection()
    {
        // Arrange
        var state = new EventLogState { Selection = [Entry(0)] };
        var action = new SetSelectedEventsAction([], null);

        // Act
        var newState = Reducers.ReduceSetSelectedEvents(state, action);

        // Assert
        Assert.Empty(newState.Selection);
    }

    [Fact]
    public void ReduceSetSelectedEvents_WhenOnlyFocusChanged_ShouldUpdateOnlyFocus()
    {
        // Arrange
        var first = Entry(0);
        var second = Entry(1);
        var state = new EventLogState { Selection = [first, second], Focus = first };
        var action = new SetSelectedEventsAction([first, second], second);

        // Act
        var newState = Reducers.ReduceSetSelectedEvents(state, action);

        // Assert: selection list is preserved by reference (no churn) but focus changed.
        Assert.Same(state.Selection, newState.Selection);
        Assert.Equal(second, newState.Focus);
    }

    [Fact]
    public void ReduceSetSelectedEvents_WhenSelectionUnchanged_ShouldReturnSameStateReference()
    {
        // Arrange: the reducer must return the same state reference when the
        // selection content and focus are both unchanged (compared by OriginHandle
        // identity), so subscribers that short-circuit on state identity do not
        // re-render on a no-op SetSelectedEvents.
        var first = Entry(0);
        var second = Entry(1);
        var state = new EventLogState { Selection = [first, second], Focus = second };
        var action = new SetSelectedEventsAction([first, second], second);

        // Act
        var newState = Reducers.ReduceSetSelectedEvents(state, action);

        // Assert
        Assert.Same(state, newState);
    }

    [Fact]
    public void ReduceSetSelectedEvents_WithNullReloadKeyEntries_ShouldDedupeByOriginHandle()
    {
        // Arrange: null-RecordId rows cannot form a ReloadKey (ReloadKey == null),
        // but they are still first-class selections identified solely by OriginHandle.
        // Distinct handles coexist; a repeated handle is deduped; order is preserved.
        var firstNullKey = Entry(0);
        var secondNullKey = Entry(1);
        var state = new EventLogState();
        var action = new SetSelectedEventsAction([firstNullKey, secondNullKey, firstNullKey], secondNullKey);

        // Act
        var newState = Reducers.ReduceSetSelectedEvents(state, action);

        // Assert
        Assert.Equal(2, newState.Selection.Count);
        Assert.Equal(firstNullKey, newState.Selection[0]);
        Assert.Equal(secondNullKey, newState.Selection[1]);
        Assert.All(newState.Selection, entry => Assert.Null(entry.ReloadKey));
        Assert.Equal(secondNullKey, newState.Focus);
    }

    private static SelectionEntry Entry(int index, int generation = 0) =>
        Entry(s_selectionLogId, index, generation);

    private static SelectionEntry Entry(EventLogId logId, int index, int generation = 0)
    {
        var handle = new EventLocator(logId, generation, index);

        return new SelectionEntry(handle, handle, null);
    }
}
