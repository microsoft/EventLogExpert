// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Store.EventLog;

public class EventLogReducers
{
    /// <summary>
    /// The maximum number of new events we will hold in the state
    /// before we turn off the watcher.
    /// </summary>
    private static readonly int MaxNewEvents = 5000;

    [ReducerMethod]
    public static EventLogState ReduceAddEvent(EventLogState state, EventLogAction.AddEvent action)
    {
        var newEvent = new List<DisplayEventModel>
        {
            action.NewEvent
        };

        var newState = state;

        if (state.ContinuouslyUpdate)
        {
            newState = newState with { Events = newEvent.Concat(state.Events).ToImmutableList() };
        }
        else
        {
            var updatedBuffer = newEvent.Concat(state.NewEventBuffer.Events).ToImmutableList();
            var full = updatedBuffer.Count >= MaxNewEvents;
            newState = newState with { NewEventBuffer = new(updatedBuffer, full) };

            if (full && state.Watcher != null && state.Watcher.IsWatching)
            {
                state.Watcher.StopWatching();
            }
        }

        return newState;
    }

    [ReducerMethod(typeof(EventLogAction.ClearEvents))]
    public static EventLogState ReduceClearEvents(EventLogState state) => 
        state with { Events = ImmutableList<DisplayEventModel>.Empty };

    [ReducerMethod]
    public static EventLogState ReduceLoadEvents(EventLogState state, EventLogAction.LoadEvents action) =>
        state with { Events = action.Events.ToImmutableList(), Watcher = action.Watcher };

    [ReducerMethod]
    public static EventLogState ReduceLoadNewEvents(EventLogState state, EventLogAction.LoadNewEvents action)
    {
        var newState = state with
        {
            Events = state.NewEventBuffer.Events.Concat(state.Events).ToImmutableList(),
            NewEventBuffer = new(ImmutableList<DisplayEventModel>.Empty, false)
        };

        if (state.Watcher != null && !state.Watcher.IsWatching)
        {
            state.Watcher.StartWatching();
        }

        return newState;
    }

    [ReducerMethod]
    public static EventLogState ReduceOpenLog(EventLogState state, EventLogAction.OpenLog action)
    {
        state.Watcher?.StopWatching();

        return new() { ActiveLog = action.LogSpecifier, ContinuouslyUpdate = state.ContinuouslyUpdate };
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
                Events = state.NewEventBuffer.Events.Concat(state.Events).ToImmutableList(),
                NewEventBuffer = new(ImmutableList<DisplayEventModel>.Empty, false)
            };
        }

        return newState;
    }
}
