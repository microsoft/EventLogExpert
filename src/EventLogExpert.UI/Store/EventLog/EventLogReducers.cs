// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;
using Fluxor;
using System;
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
    public static EventLogState ReduceLoadEvents(EventLogState state, EventLogAction.LoadEvents action)
    {
        var newLogsCollection = state.ActiveLogs;

        if (newLogsCollection.ContainsKey(action.LogName))
        {
            newLogsCollection = newLogsCollection.Remove(action.LogName);
        }

        // Events collection is always ordered descending by record id
        var sortedEvents = action.Events.SortEvents(null, true).ToList();

        return state with
        {
            ActiveLogs = newLogsCollection.Add(
                action.LogName,
                new EventLogData(
                    action.LogName,
                    action.Type,
                    sortedEvents.AsReadOnly(),
                    action.AllEventIds.ToImmutableHashSet(),
                    action.AllActivityIds.ToImmutableHashSet(),
                    action.AllProviderNames.ToImmutableHashSet(),
                    action.AllTaskNames.ToImmutableHashSet(),
                    action.AllKeywords.ToImmutableHashSet()
                ))
        };
    }

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
        return state with { EventsLoading = CommonLoadingReducer(state.EventsLoading, action.ActivityId, action.Count) };
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

    [ReducerMethod]
    public static EventLogState ReduceSetXmlLoading(EventLogState state, EventLogAction.SetXmlLoading action)
    {
        return state with { XmlLoading = CommonLoadingReducer(state.XmlLoading, action.ActivityId, action.Count) };
    }

    private static ImmutableDictionary<Guid, int> CommonLoadingReducer(ImmutableDictionary<Guid, int> loadingEntries, Guid activityId, int count)
    {
        if (loadingEntries.ContainsKey(activityId))
        {
            loadingEntries = loadingEntries.Remove(activityId);
        }

        if (count == 0)
        {
            return loadingEntries;
        }

        return loadingEntries.Add(activityId, count);
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
