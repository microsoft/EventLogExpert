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
        if (!state.ActiveLogs.Any(l => l.Name == action.NewEvent.OwningLog))
        {
            return state;
        }

        var newEvent = new[] { action.NewEvent };

        var newState = state;

        if (state.ContinuouslyUpdate)
        {
            newState = newState with { Events = newEvent.Concat(state.Events).ToList().AsReadOnly() };
        }
        else
        {
            var updatedBuffer = newEvent.Concat(state.NewEventBuffer).ToList().AsReadOnly();
            var full = updatedBuffer.Count >= MaxNewEvents;
            newState = newState with { NewEventBuffer = updatedBuffer, NewEventBufferIsFull = full };
        }

        if (!state.EventIds.Contains(action.NewEvent.Id))
        {
            newState = newState with { EventIds = state.EventIds.Add(action.NewEvent.Id) };
        }

        if (!state.EventProviderNames.Contains(action.NewEvent.Source))
        {
            newState = newState with { EventProviderNames = state.EventProviderNames.Add(action.NewEvent.Source) };
        }

        if (!state.TaskNames.Contains(action.NewEvent.TaskCategory))
        {
            newState = newState with { TaskNames = state.TaskNames.Add(action.NewEvent.TaskCategory) };
        }

        return newState;
    }

    [ReducerMethod]
    public static EventLogState ReduceLoadEvents(EventLogState state, EventLogAction.LoadEvents action)
    {
        return state with
        {
            Events = action.Events.Concat(state.Events)
                .OrderByDescending(e => e.TimeCreated)
                .ToList().AsReadOnly(),
            EventIds = state.EventIds.Union(action.AllEventIds),
            EventProviderNames = state.EventProviderNames.Union(action.AllProviderNames),
            TaskNames = state.TaskNames.Union(action.AllTaskNames)
        };
    }

    [ReducerMethod]
    public static EventLogState ReduceLoadNewEvents(EventLogState state, EventLogAction.LoadNewEvents action)
    {
        return state with
        {
            Events = state.NewEventBuffer.Concat(state.Events).OrderByDescending(e => e.TimeCreated).ToList().AsReadOnly(),
            NewEventBuffer = new List<DisplayEventModel>().AsReadOnly(),
            NewEventBufferIsFull = false
        };
    }

    [ReducerMethod]
    public static EventLogState ReduceOpenLog(EventLogState state, EventLogAction.OpenLog action)
    {
        return state with
        {
            ActiveLogs = state.ActiveLogs.Add(action.LogSpecifier)
        };
    }

    [ReducerMethod]
    public static EventLogState ReduceCloseLog(EventLogState state, EventLogAction.CloseLog action)
    {
        // If that was the only open log, do this the easy way
        if (state.ActiveLogs.Count == 1 && state.ActiveLogs[0].Name == action.LogName)
        {
            return state with
            {
                ActiveLogs = ImmutableList<LogSpecifier>.Empty,
                Events = new List<DisplayEventModel>().AsReadOnly(),
                NewEventBuffer = new List<DisplayEventModel>().AsReadOnly(),
                NewEventBufferIsFull = false,
                EventIds = ImmutableHashSet<int>.Empty,
                EventProviderNames = ImmutableHashSet<string>.Empty,
                TaskNames = ImmutableHashSet<string>.Empty
            };
        }

        var newState = state with
        {
            ActiveLogs = state.ActiveLogs
                .Where(l => l.Name != action.LogName)
                .ToImmutableList(),
            Events = state.Events
                .Where(e => e.OwningLog != action.LogName)
                .ToList().AsReadOnly(),
            NewEventBuffer = state.NewEventBuffer
                .Where(e => e.OwningLog != action.LogName)
                .ToList().AsReadOnly()
        };

        newState = RecalculateAvailableValueCollections(newState);

        newState = newState with { NewEventBufferIsFull = newState.NewEventBuffer.Count >= MaxNewEvents ? true : false };

        return newState;
    }

    [ReducerMethod]
    public static EventLogState ReduceCloseAll(EventLogState state, EventLogAction.CloseAll action)
    {
        return state with
        {
            ActiveLogs = ImmutableList<LogSpecifier>.Empty,
            Events = new List<DisplayEventModel>().AsReadOnly(),
            NewEventBuffer = new List<DisplayEventModel>().AsReadOnly(),
            NewEventBufferIsFull = false,
            EventIds = ImmutableHashSet<int>.Empty,
            EventProviderNames = ImmutableHashSet<string>.Empty,
            TaskNames = ImmutableHashSet<string>.Empty
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
            newState = newState with
            {
                Events = state.NewEventBuffer.Concat(state.Events)
                    .OrderByDescending(e => e.TimeCreated)
                    .ToList().AsReadOnly(),
                NewEventBuffer = new List<DisplayEventModel>().AsReadOnly(),
                NewEventBufferIsFull = false
            };
        }

        return newState;
    }

    [ReducerMethod]
    public static EventLogState ReduceSetEventsLoading(EventLogState state, EventLogAction.SetEventsLoading action) =>
        state with { EventsLoading = action.Count };

    private static EventLogState RecalculateAvailableValueCollections(EventLogState state)
    {
        var eventIds = new HashSet<int>();
        var providerNames = new HashSet<string>();
        var taskNames = new HashSet<string>();

        foreach (var e in state.Events)
        {
            eventIds.Add(e.Id);
            providerNames.Add(e.Source);
            taskNames.Add(e.TaskCategory);
        }

        return state with
        {
            EventIds = eventIds.ToImmutableHashSet(),
            EventProviderNames = providerNames.ToImmutableHashSet(),
            TaskNames = taskNames.ToImmutableHashSet()
        };
    }
}
