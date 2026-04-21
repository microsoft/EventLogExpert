// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
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
            NewEventBuffer = [],
            NewEventBufferIsFull = false
        };

    [ReducerMethod]
    public static EventLogState ReduceCloseLog(EventLogState state, EventLogAction.CloseLog action)
    {
        var newEventBuffer = state.NewEventBuffer
            .Where(e => e.OwningLog != action.LogName)
            .ToList();

        // Drop selections belonging to the closed log so stale references don't linger
        // in SelectedEvents. Without this, a reload (which closes and re-opens the log
        // to load XML) would leave stale entries that prevent the highlight refresh
        // when the new instances are restored via SelectEvents.
        var newSelectedEvents = state.SelectedEvents
            .RemoveAll(e => string.Equals(e.OwningLog, action.LogName, StringComparison.Ordinal));

        return state with
        {
            ActiveLogs = state.ActiveLogs.Remove(action.LogName),
            NewEventBuffer = newEventBuffer,
            NewEventBufferIsFull = newEventBuffer.Count >= EventLogState.MaxNewEvents,
            SelectedEvents = newSelectedEvents
        };
    }

    [ReducerMethod]
    public static EventLogState ReduceLoadEvents(EventLogState state, EventLogAction.LoadEvents action)
    {
        if (!state.ActiveLogs.TryGetValue(action.LogData.Name, out var existing) ||
            existing.Id != action.LogData.Id)
        {
            return state;
        }

        return UpdateActiveLog(state, action.LogData, action.Events);
    }

    [ReducerMethod]
    public static EventLogState ReduceLoadEventsPartial(EventLogState state, EventLogAction.LoadEventsPartial action)
    {
        if (!state.ActiveLogs.TryGetValue(action.LogData.Name, out var existingLog) ||
            existingLog.Id != action.LogData.Id)
        {
            return state;
        }

        var merged = new List<DisplayEventModel>(existingLog.Events.Count + action.Events.Count);
        merged.AddRange(existingLog.Events);
        merged.AddRange(action.Events);

        return state with
        {
            ActiveLogs = state.ActiveLogs
                .Remove(action.LogData.Name)
                .Add(action.LogData.Name, existingLog with { Events = merged.AsReadOnly() })
        };
    }

    [ReducerMethod]
    public static EventLogState ReduceOpenLog(EventLogState state, EventLogAction.OpenLog action) =>
        state with
        {
            ActiveLogs = state.ActiveLogs.Add(action.LogName, GetEmptyLogData(action.LogName, action.PathType))
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
    public static EventLogState ReduceSetContinuouslyUpdate(
        EventLogState state,
        EventLogAction.SetContinuouslyUpdate action) =>
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

    private static EventLogData GetEmptyLogData(string logName, PathType pathType) =>
        new(logName, pathType, new List<DisplayEventModel>().AsReadOnly());

    private static EventLogState UpdateActiveLog(
        EventLogState state,
        EventLogData logData,
        IReadOnlyList<DisplayEventModel> events) =>
        state with
        {
            ActiveLogs = state.ActiveLogs
                .Remove(logData.Name)
                .Add(logData.Name, logData with { Events = events })
        };
}
