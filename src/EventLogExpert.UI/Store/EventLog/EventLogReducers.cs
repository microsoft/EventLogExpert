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
    public static EventLogState ReduceCloseAll(EventLogState state) => state with
    {
        ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty,
        SelectedEvent = null,
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
            NewEventBufferIsFull = newEventBuffer.Count >= state.MaxNewEvents
        };
    }

    [ReducerMethod]
    public static EventLogState ReduceLoadEventsSuccess(EventLogState state, EventLogAction.LoadEventsSuccess action) =>
        state with { ActiveLogs = action.ActiveLogs };

    [ReducerMethod]
    public static EventLogState ReduceOpenLog(EventLogState state, EventLogAction.OpenLog action) => state with
    {
        ActiveLogs = state.ActiveLogs.Add(action.LogName, GetEmptyLogData(action.LogName, action.LogType))
    };

    [ReducerMethod]
    public static EventLogState ReduceSelectEvent(EventLogState state, EventLogAction.SelectEvent action)
    {
        if (state.SelectedEvent == action.SelectedEvent) { return state; }

        return state with { SelectedEvent = action.SelectedEvent };
    }

    [ReducerMethod]
    public static EventLogState ReduceSetContinouslyUpdate(
        EventLogState state,
        EventLogAction.SetContinouslyUpdate action) => state with { ContinuouslyUpdate = action.ContinuouslyUpdate };

    [ReducerMethod]
    public static EventLogState ReduceSetEventsLoading(EventLogState state, EventLogAction.SetEventsLoading action)
    {
        var newEventsLoading = state.EventsLoading;

        if (newEventsLoading.ContainsKey(action.ActivityId))
        {
            newEventsLoading = newEventsLoading.Remove(action.ActivityId);
        }

        if (action.Count == 0)
        {
            return state with { EventsLoading = newEventsLoading };
        }

        return state with { EventsLoading = newEventsLoading.Add(action.ActivityId, action.Count) };
    }

    [ReducerMethod]
    public static EventLogState ReduceSetFilters(EventLogState state, EventLogAction.SetFilters action)
    {
        if (!FilterMethods.HasFilteringChanged(action.EventFilter, state.AppliedFilter))
        {
            return state;
        }

        return state with { AppliedFilter = action.EventFilter };
    }

    private static EventLogData GetEmptyLogData(string logName, LogType logType) => new(
        logName,
        logType,
        new List<DisplayEventModel>().AsReadOnly(),
        ImmutableHashSet<int>.Empty,
        ImmutableHashSet<Guid?>.Empty,
        ImmutableHashSet<string>.Empty,
        ImmutableHashSet<string>.Empty,
        ImmutableHashSet<string>.Empty);
}
