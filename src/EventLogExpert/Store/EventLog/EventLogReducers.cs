// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using Fluxor;
using System.Collections.Immutable;
using static EventLogExpert.Store.EventLog.EventLogState;

namespace EventLogExpert.Store.EventLog;

public class EventLogReducers
{
    /// <summary>
    /// The maximum number of new events we will hold in the state
    /// before we turn off the watcher.
    /// </summary>
    private static readonly int MaxNewEvents = 1000;

    [ReducerMethod]
    public static EventLogState ReduceAddEvent(EventLogState state, EventLogAction.AddEvent action)
    {
        // Sometimes the watcher doesn't stop firing events immediately. Let's
        // make sure the events being added are for a log that is still "open".
        if (!state.ActiveLogs.ContainsKey(action.NewEvent.OwningLog))
        {
            return state;
        }

        var newEvent = new[] { action.NewEvent };

        var newState = state;

        if (state.ContinuouslyUpdate)
        {
            var oldLogData = state.ActiveLogs[action.NewEvent.OwningLog];
            var updatedLogData = AddEventsToLogData(oldLogData, newEvent);
            var updatedDictionary = state.ActiveLogs.Remove(action.NewEvent.OwningLog).Add(action.NewEvent.OwningLog, updatedLogData);
            newState = newState with { ActiveLogs = updatedDictionary };
        }
        else
        {
            var updatedBuffer = newEvent.Concat(state.NewEventBuffer).ToList().AsReadOnly();
            var full = updatedBuffer.Count >= MaxNewEvents;
            newState = newState with { NewEventBuffer = updatedBuffer, NewEventBufferIsFull = full };
        }

        return newState;
    }

    [ReducerMethod]
    public static EventLogState ReduceLoadEvents(EventLogState state, EventLogAction.LoadEvents action)
    {
        var newLogsCollection = state.ActiveLogs;

        if (state.ActiveLogs.ContainsKey(action.LogName))
        {
            newLogsCollection = state.ActiveLogs.Remove(action.LogName);
        }

        newLogsCollection = newLogsCollection.Add(action.LogName, new EventLogData
        (
            action.LogName,
            action.Type,
            action.Events.AsReadOnly(),
            action.AllEventIds.ToImmutableHashSet(),
            action.AllProviderNames.ToImmutableHashSet(),
            action.AllTaskNames.ToImmutableHashSet()
        ));

        return state with { ActiveLogs = newLogsCollection };
    }

    [ReducerMethod]
    public static EventLogState ReduceLoadNewEvents(EventLogState state, EventLogAction.LoadNewEvents action)
    {
        var newState = state with { ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty };

        foreach (var log in state.ActiveLogs.Values)
        {
            var newLogData = AddEventsToLogData(log, state.NewEventBuffer.Where(e => e.OwningLog == log.Name));
            newState = newState with { ActiveLogs = newState.ActiveLogs.Add(log.Name, newLogData) };
        }

        newState = newState with
        {
            NewEventBuffer = new List<DisplayEventModel>().AsReadOnly(),
            NewEventBufferIsFull = false
        };

        return newState;
    }

    [ReducerMethod]
    public static EventLogState ReduceOpenLog(EventLogState state, EventLogAction.OpenLog action)
    {
        return state with
        {
            ActiveLogs = state.ActiveLogs.Add(action.LogName, GetEmptyLogData(action.LogName, action.LogType))
        };
    }

    [ReducerMethod]
    public static EventLogState ReduceCloseLog(EventLogState state, EventLogAction.CloseLog action)
    {
        // If that was the only open log, do this the easy way

        var newState = state with
        {
            ActiveLogs = state.ActiveLogs.Remove(action.LogName),
            NewEventBuffer = state.NewEventBuffer
                .Where(e => e.OwningLog != action.LogName)
                .ToList().AsReadOnly()
        };

        newState = newState with { NewEventBufferIsFull = newState.NewEventBuffer.Count >= MaxNewEvents ? true : false };

        return newState;
    }

    [ReducerMethod]
    public static EventLogState ReduceCloseAll(EventLogState state, EventLogAction.CloseAll action)
    {
        return state with
        {
            ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty,
            NewEventBuffer = new List<DisplayEventModel>().AsReadOnly(),
            NewEventBufferIsFull = false
        };
    }

    [ReducerMethod]
    public static EventLogState ReduceSelectEvent(EventLogState state, EventLogAction.SelectEvent action)
    {
        if (state.SelectedEvent == action.SelectedEvent) { return state; }

        return state with { SelectedEvent = action.SelectedEvent };
    }

    [ReducerMethod]
    public static EventLogState ReduceSetContinouslyUpdate(EventLogState state, EventLogAction.SetContinouslyUpdate action)
    {
        var newState = state with { ContinuouslyUpdate = action.ContinuouslyUpdate };
        if (action.ContinuouslyUpdate)
        {
            newState = newState with { ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty };
            foreach (var log in state.ActiveLogs.Values)
            {
                newState = newState with
                {
                    ActiveLogs = newState.ActiveLogs.Add(log.Name,
                        AddEventsToLogData(state.ActiveLogs[log.Name], state.NewEventBuffer.Where(e => e.OwningLog == log.Name)))
                };
            }

            newState = newState with
            {
                NewEventBuffer = new List<DisplayEventModel>().AsReadOnly(),
                NewEventBufferIsFull = false
            };
        }

        return newState;
    }

    [ReducerMethod]
    public static EventLogState ReduceSetEventsLoading(EventLogState state, EventLogAction.SetEventsLoading action) =>
        state with { EventsLoading = action.Count };

    private static EventLogData AddEventsToLogData(EventLogData logData, IEnumerable<DisplayEventModel> eventsToAdd)
    {
        var newEvents = eventsToAdd.ToList();
        var updatedEvents = eventsToAdd.Concat(logData.Events).ToList().AsReadOnly();
        var updatedEventIds = logData.EventIds.Union(eventsToAdd.Select(e => e.Id));
        var updatedProviderNames = logData.EventProviderNames.Union(eventsToAdd.Select(e => e.Source));
        var updatedTaskNames = logData.TaskNames.Union(eventsToAdd.Select(e => e.TaskCategory));
        var updatedLogData = logData with
        {
            Events = updatedEvents,
            EventIds = updatedEventIds,
            EventProviderNames = updatedProviderNames,
            TaskNames = updatedTaskNames
        };

        return updatedLogData;
    }

    private static EventLogData GetEmptyLogData(string LogName, LogType LogType)
    {
        return new EventLogData(
            LogName,
            LogType,
            new List<DisplayEventModel>().AsReadOnly(),
            ImmutableHashSet<int>.Empty,
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);
    }
}
