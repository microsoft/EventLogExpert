// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.EventLog;

public sealed class EventLogReducers
{
    [ReducerMethod]
    public static EventLogState ReduceAddEventBuffered(EventLogState state, EventLogAction.AddEventBuffered action) =>
        state with { NewEventBuffer = action.UpdatedBuffer, NewEventBufferIsFull = action.IsFull };

    [ReducerMethod]
    public static EventLogState ReduceAddEventSuccess(EventLogState state, EventLogAction.AddEventSuccess action) =>
        state with { ActiveLogs = action.ActiveLogs };

    [ReducerMethod(typeof(EventLogAction.CloseAll))]
    public static EventLogState ReduceCloseAll(EventLogState state) =>
        state with
        {
            ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty,
            SelectedEvents = [],
            NewEventBuffer = new List<DisplayEventModel>().AsReadOnly(),
            NewEventBufferIsFull = false
        };

    [ReducerMethod]
    public static EventLogState ReduceCloseLog(EventLogState state, EventLogAction.CloseLog action)
    {
        var newEventBuffer = state.NewEventBuffer
            .Where(e => e.OwningLog != action.LogName)
            .ToList()
            .AsReadOnly();

        return state with
        {
            ActiveLogs = state.ActiveLogs.Remove(action.LogName),
            NewEventBuffer = newEventBuffer,
            NewEventBufferIsFull = newEventBuffer.Count >= EventLogState.MaxNewEvents
        };
    }

    [ReducerMethod]
    public static EventLogState ReduceLoadEvents(EventLogState state, EventLogAction.LoadEvents action)
    {
        var newLogsCollection = state.ActiveLogs.Remove(action.LogData.Name);

        return state with
        {
            ActiveLogs = newLogsCollection.Add(
                action.LogData.Name,
                action.LogData with
                {
                    Events = action.Events,
                    EventIds = action.AllEventIds,
                    EventActivityIds = action.AllActivityIds,
                    EventProviderNames = action.AllProviderNames,
                    TaskNames = action.AllTaskNames,
                    KeywordNames = action.AllKeywords
                })
        };
    }

    [ReducerMethod]
    public static EventLogState ReduceOpenLog(EventLogState state, EventLogAction.OpenLog action) =>
        state with
        {
            ActiveLogs = state.ActiveLogs.Add(action.LogName, GetEmptyLogData(action.LogName, action.LogType))
        };

    [ReducerMethod]
    public static EventLogState ReduceSelectEvent(EventLogState state, EventLogAction.SelectEvent action)
    {
        if (!state.SelectedEvents.Contains(action.SelectedEvent))
        {
            return state with
            {
                SelectedEvents = action.IsMultiSelect ?
                    state.SelectedEvents.Add(action.SelectedEvent) : [action.SelectedEvent]
            };
        }

        if (action is { IsMultiSelect: true, ShouldStaySelected: false })
        {
            return state with { SelectedEvents = state.SelectedEvents.Remove(action.SelectedEvent) };
        }

        return action.ShouldStaySelected ? state : state with { SelectedEvents = [action.SelectedEvent] };
    }

    [ReducerMethod]
    public static EventLogState ReduceSelectEvents(EventLogState state, EventLogAction.SelectEvents action)
    {
        List<DisplayEventModel> eventsToAdd = [];

        foreach (var selectedEvent in action.SelectedEvents)
        {
            if (!state.SelectedEvents.Contains(selectedEvent))
            {
                eventsToAdd.Add(selectedEvent);
            }
        }

        return state with { SelectedEvents = state.SelectedEvents.AddRange(eventsToAdd) };
    }

    [ReducerMethod]
    public static EventLogState ReduceSetContinouslyUpdate(
        EventLogState state,
        EventLogAction.SetContinouslyUpdate action) =>
        state with { ContinuouslyUpdate = action.ContinuouslyUpdate };

    [ReducerMethod]
    public static EventLogState ReduceSetFilters(EventLogState state, EventLogAction.SetFilters action)
    {
        if (!FilterMethods.HasFilteringChanged(action.EventFilter, state.AppliedFilter))
        {
            return state;
        }

        return state with { AppliedFilter = action.EventFilter };
    }

    private static EventLogData GetEmptyLogData(string logName, LogType logType) =>
        new(logName, logType, new List<DisplayEventModel>().AsReadOnly(), [], [], [], [], []);
}
