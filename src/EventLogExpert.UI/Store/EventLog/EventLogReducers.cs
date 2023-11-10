// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using Fluxor;
using System.Collections.Immutable;
using static EventLogExpert.UI.Store.EventLog.EventLogState;

namespace EventLogExpert.UI.Store.EventLog;

public class EventLogReducers
{
    /// <summary>The maximum number of new events we will hold in the state before we turn off the watcher.</summary>
    private static readonly int MaxNewEvents = 1000;

    [ReducerMethod]
    public static EventLogState ReduceAddEvent(EventLogState state, EventLogAction.AddEvent action)
    {
        // Sometimes the watcher doesn't stop firing events immediately. Let's
        // make sure the events being added are for a log that is still "open".
        if (!state.ActiveLogs.ContainsKey(action.NewEvent.OwningLog)) { return state; }

        var newEvent = new[] { action.NewEvent };

        var newState = state;

        if (state.ContinuouslyUpdate)
        {
            newState = newState with
            {
                ActiveLogs = DistributeEventsToManyLogs(
                    newState.ActiveLogs,
                    newEvent,
                    state.AppliedFilter,
                    state.SortDescending,
                    action.TraceLogger),
                CombinedEvents = AddEventsToCombinedLog(
                        state.CombinedEvents,
                        newEvent,
                        state.AppliedFilter,
                        state.SortDescending,
                        action.TraceLogger)
                    .ToList()
                    .AsReadOnly()
            };
        }
        else
        {
            var updatedBuffer = newEvent.Concat(state.NewEventBuffer).ToList().AsReadOnly();
            var full = updatedBuffer.Count >= MaxNewEvents;
            newState = newState with { NewEventBuffer = updatedBuffer, NewEventBufferIsFull = full };
        }

        return newState;
    }

    [ReducerMethod(typeof(EventLogAction.CloseAll))]
    public static EventLogState ReduceCloseAll(EventLogState state) => state with
    {
        ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty,
        CombinedEvents = new List<DisplayEventModel>().AsReadOnly(),
        NewEventBuffer = new List<DisplayEventModel>().AsReadOnly(),
        NewEventBufferIsFull = false
    };

    [ReducerMethod]
    public static EventLogState ReduceCloseLog(EventLogState state, EventLogAction.CloseLog action)
    {
        var newState = state with
        {
            ActiveLogs = state.ActiveLogs.Remove(action.LogName),
            CombinedEvents = state.CombinedEvents.Where(e => e.OwningLog != action.LogName).ToList().AsReadOnly(),
            NewEventBuffer = state.NewEventBuffer
                .Where(e => e.OwningLog != action.LogName)
                .ToList().AsReadOnly()
        };

        newState = newState with
        {
            NewEventBufferIsFull = newState.NewEventBuffer.Count >= MaxNewEvents
        };

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

        // Events collection is always ordered descending by record id
        var sortedEvents = action.Events.OrderByDescending(e => e.RecordId).ToList();
        // Filtered events reflects both the filter and sort choice.
        var filtered = IsFilteringEnabled(state.AppliedFilter) ?
            GetFilteredEvents(
                    sortedEvents,
                    state.AppliedFilter,
                    action.TraceLogger,
                    isDescending: state.SortDescending)
                .ToList() :
            sortedEvents;

        newLogsCollection = newLogsCollection.Add(
            action.LogName,
            new EventLogData(
                action.LogName,
                action.Type,
                sortedEvents.AsReadOnly(),
                filtered.AsReadOnly(),
                action.AllEventIds.ToImmutableHashSet(),
                action.AllActivityIds.ToImmutableHashSet(),
                action.AllProviderNames.ToImmutableHashSet(),
                action.AllTaskNames.ToImmutableHashSet(),
                action.AllKeywords.ToImmutableHashSet()
            ));

        var newCombinedEvents = CombineLogs(
            newLogsCollection.Values.Select(l => l.FilteredEvents),
            state.SortDescending,
            action.TraceLogger);

        return state with { ActiveLogs = newLogsCollection, CombinedEvents = newCombinedEvents.ToList().AsReadOnly() };
    }

    [ReducerMethod]
    public static EventLogState ReduceLoadNewEvents(EventLogState state, EventLogAction.LoadNewEvents action) =>
        ProcessNewEventBuffer(state, action.TraceLogger);

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
    public static EventLogState ReduceSelectLog(EventLogState state, EventLogAction.SelectLog action) =>
        state with { SelectedLogName = action.LogName };

    [ReducerMethod]
    public static EventLogState ReduceSetContinouslyUpdate(
        EventLogState state,
        EventLogAction.SetContinouslyUpdate action)
    {
        var newState = state with { ContinuouslyUpdate = action.ContinuouslyUpdate };

        if (action.ContinuouslyUpdate)
        {
            newState = ProcessNewEventBuffer(newState, action.TraceLogger);
        }

        return newState;
    }

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
        if (!HasFilteringChanged(action.EventFilter, state.AppliedFilter))
        {
            return state;
        }

        var newState = state;

        foreach (var entry in state.ActiveLogs.Values)
        {
            EventLogData newLogData;

            if (!IsFilteringEnabled(action.EventFilter))
            {
                newLogData = entry with { FilteredEvents = entry.Events };
            }
            else
            {
                newLogData = entry with
                {
                    FilteredEvents = GetFilteredEvents(
                            entry.Events,
                            action.EventFilter,
                            action.TraceLogger,
                            isDescending: state.SortDescending)
                        .ToList()
                        .AsReadOnly()
                };
            }

            newState = newState with
            {
                ActiveLogs = newState.ActiveLogs
                    .Remove(entry.Name)
                    .Add(entry.Name, newLogData)
            };
        }

        var newCombinedEvents = CombineLogs(
            newState.ActiveLogs.Values.Select(l => l.FilteredEvents),
            state.SortDescending,
            action.TraceLogger);

        newState = newState with
        {
            CombinedEvents = newCombinedEvents.ToList().AsReadOnly(),
            AppliedFilter = action.EventFilter
        };

        return newState;
    }

    [ReducerMethod]
    public static EventLogState ReduceSetSortDescending(EventLogState state, EventLogAction.SetSortDescending action)
    {
        if (action.SortDescending == state.SortDescending) { return state; }

        var newActiveLogs = state.ActiveLogs;

        foreach (var logData in state.ActiveLogs.Values)
        {
            newActiveLogs = newActiveLogs
                .Remove(logData.Name)
                .Add(logData.Name,
                    logData with
                    {
                        FilteredEvents = logData.FilteredEvents
                            .SortEvents(isDescending: action.SortDescending)
                            .ToList()
                            .AsReadOnly()
                    });
        }

        var newCombinedEvents = CombineLogs(newActiveLogs.Values.Select(l => l.FilteredEvents), action.SortDescending, action.TraceLogger);

        return state with
        {
            ActiveLogs = newActiveLogs,
            CombinedEvents = newCombinedEvents.ToList().AsReadOnly(),
            SortDescending = action.SortDescending
        };
    }

    /// <summary>Add new events to a "combined" log view</summary>
    /// <param name="combinedLog"></param>
    /// <param name="eventsToAdd">
    ///     It is assumed that these events are already sorted in descending order from newest to oldest.
    ///     This value should be coming from NewEventBuffer, where new events are inserted at the top of the list as they come
    ///     in.
    /// </param>
    /// <param name="filter"></param>
    /// <param name="isDescending"></param>
    /// <param name="traceLogger"></param>
    /// <returns></returns>
    private static IEnumerable<DisplayEventModel> AddEventsToCombinedLog(
        IEnumerable<DisplayEventModel> combinedLog,
        IEnumerable<DisplayEventModel> eventsToAdd,
        EventFilter filter,
        bool isDescending,
        ITraceLogger traceLogger)
    {
        var newEventsMatchingFilter = GetFilteredEvents(eventsToAdd, filter, traceLogger, isDescending: isDescending);

        return isDescending ? newEventsMatchingFilter.Concat(combinedLog) : combinedLog.Concat(newEventsMatchingFilter);
    }

    /// <summary></summary>
    /// <param name="logData">This log should already be sorted appropriately. We do not sort this here.</param>
    /// <param name="eventsToAdd">These do not need to be sorted already.</param>
    /// <param name="filter"></param>
    /// <param name="isDescending"></param>
    /// <param name="traceLogger"></param>
    /// <returns></returns>
    private static EventLogData AddEventsToOneLog(
        EventLogData logData,
        IEnumerable<DisplayEventModel> eventsToAdd,
        EventFilter filter,
        bool isDescending,
        ITraceLogger traceLogger)
    {
        var newEvents = eventsToAdd.OrderByDescending(e => e.RecordId).ToList();
        var filteredEvents = GetFilteredEvents(newEvents, filter, traceLogger, isDescending: isDescending);

        // Events collection is always sorted descending by record ID.
        var updatedEvents = newEvents.Concat(logData.Events);

        var updatedFilteredEvents = isDescending ?
            filteredEvents.Concat(logData.FilteredEvents) :
            logData.FilteredEvents.Concat(filteredEvents);

        var updatedEventIds = logData.EventIds.Union(newEvents.Select(e => e.Id));
        var updatedProviderNames = logData.EventProviderNames.Union(newEvents.Select(e => e.Source));
        var updatedTaskNames = logData.TaskNames.Union(newEvents.Select(e => e.TaskCategory));

        var updatedLogData = logData with
        {
            Events = updatedEvents.ToList().AsReadOnly(),
            FilteredEvents = updatedFilteredEvents.ToList().AsReadOnly(),
            EventIds = updatedEventIds,
            EventProviderNames = updatedProviderNames,
            TaskNames = updatedTaskNames
        };

        return updatedLogData;
    }

    /// <summary>
    ///     This should be used to combine events from multiple logs into a combined log. The sort key changes depending
    ///     on how many logs are present.
    /// </summary>
    private static IEnumerable<DisplayEventModel> CombineLogs(
        IEnumerable<IEnumerable<DisplayEventModel>> eventData,
        bool isDescending,
        ITraceLogger traceLogger)
    {
        var events = eventData.ToList();

        traceLogger.Trace($"{nameof(CombineLogs)} was called for {events.Count} logs.");

        if (events.Count > 1)
        {
            return isDescending ?
                events.SelectMany(l => l).OrderByDescending(e => e.TimeCreated) :
                events.SelectMany(l => l).OrderBy(e => e.TimeCreated);
        }

        // If we only have one log open, the events are already sorted.
        return events.First();
    }

    private static ImmutableDictionary<string, EventLogData> DistributeEventsToManyLogs(
        ImmutableDictionary<string, EventLogData> logsToUpdate,
        IEnumerable<DisplayEventModel> eventsToDistribute,
        EventFilter filter,
        bool isDescending,
        ITraceLogger traceLogger)
    {
        var newLogs = logsToUpdate;
        var events = eventsToDistribute.ToList();

        foreach (var log in logsToUpdate.Values)
        {
            var newEventsForThisLog = events.Where(e => e.OwningLog == log.Name).ToList();

            if (newEventsForThisLog.Any())
            {
                var newLogData = AddEventsToOneLog(log, newEventsForThisLog, filter, isDescending, traceLogger);
                newLogs = newLogs.Remove(log.Name).Add(log.Name, newLogData);
            }
        }

        return newLogs;
    }

    private static EventLogData GetEmptyLogData(string logName, LogType logType) => new(
        logName,
        logType,
        new List<DisplayEventModel>().AsReadOnly(),
        new List<DisplayEventModel>().AsReadOnly(),
        ImmutableHashSet<int>.Empty,
        ImmutableHashSet<Guid?>.Empty,
        ImmutableHashSet<string>.Empty,
        ImmutableHashSet<string>.Empty,
        ImmutableHashSet<string>.Empty);

    /// <summary>Filters a list of <paramref name="events" /> based on configured <paramref name="eventFilter" /></summary>
    /// <param name="events"></param>
    /// <param name="eventFilter"></param>
    /// <param name="traceLogger"></param>
    /// <param name="orderBy"></param>
    /// <param name="isDescending"></param>
    /// <returns>A collection of filtered events that are sorted by RecordId unless otherwise specified</returns>
    private static IEnumerable<DisplayEventModel> GetFilteredEvents(
        IEnumerable<DisplayEventModel> events,
        EventFilter eventFilter,
        ITraceLogger traceLogger,
        ColumnName? orderBy = null,
        bool isDescending = false)
    {
        traceLogger.Trace($"{nameof(GetFilteredEvents)} was called to filter {events.Count()} events.");

        IQueryable<DisplayEventModel> filteredEvents = events.AsQueryable();

        List<Func<DisplayEventModel, bool>> filters = new();

        if (eventFilter.DateFilter?.IsEnabled is true)
        {
            filteredEvents = filteredEvents.Where(e =>
                e.TimeCreated >= eventFilter.DateFilter.After &&
                e.TimeCreated <= eventFilter.DateFilter.Before);
        }

        if (eventFilter.AdvancedFilter?.IsEnabled is true)
        {
            filteredEvents = filteredEvents.Where(e => eventFilter.AdvancedFilter.Comparison(e));
        }

        if (eventFilter.Filters.Any())
        {
            filters.Add(e => eventFilter.Filters
                .All(filter => filter.Comparison(e)));
        }

        if (eventFilter.CachedFilters.Any())
        {
            filters.Add(e => eventFilter.CachedFilters
                .All(filter => filter.Comparison(e)));
        }

        // Only sort if we have to, due to use of AsParallel
        return filters.Any() ?
            filteredEvents.AsParallel()
                .Where(e => filters
                    .All(filter => filter(e)))
                .SortEvents(orderBy, isDescending) :
            filteredEvents;
    }

    private static bool HasFilteringChanged(EventFilter updated, EventFilter original) =>
        updated.AdvancedFilter?.Equals(original.AdvancedFilter) is false ||
        updated.DateFilter?.Equals(original.DateFilter) is false ||
        updated.CachedFilters.Equals(original.CachedFilters) is false ||
        updated.Filters.Equals(original.Filters) is false;

    private static bool IsFilteringEnabled(EventFilter eventFilter) =>
        eventFilter.AdvancedFilter?.IsEnabled is true ||
        eventFilter.CachedFilters.Any() ||
        eventFilter.DateFilter?.IsEnabled is true ||
        eventFilter.Filters.Any();

    private static EventLogState ProcessNewEventBuffer(EventLogState state, ITraceLogger traceLogger)
    {
        var newState = state with
        {
            ActiveLogs = DistributeEventsToManyLogs(
                state.ActiveLogs,
                state.NewEventBuffer,
                state.AppliedFilter,
                state.SortDescending,
                traceLogger)
        };

        var newCombinedEvents = CombineLogs(
            newState.ActiveLogs.Values.Select(l => l.FilteredEvents),
            state.SortDescending,
            traceLogger);

        newState = newState with
        {
            CombinedEvents = newCombinedEvents.ToList().AsReadOnly(),
            NewEventBuffer = new List<DisplayEventModel>().AsReadOnly(),
            NewEventBufferIsFull = false
        };

        return newState;
    }
}
