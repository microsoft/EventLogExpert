// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using Fluxor;
using System.Collections.Immutable;
using System.Linq.Dynamic.Core;
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
            newState = newState with
            {
                ActiveLogs = DistributeEventsToManyLogs(newState.ActiveLogs, newEvent, state.AppliedFilter, state.SortDescending),
                CombinedEvents = AddEventsToCombinedLog(state.CombinedEvents, newEvent, state.AppliedFilter, state.SortDescending)
                    .ToList().AsReadOnly()
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

    [ReducerMethod]
    public static EventLogState ReduceLoadEvents(EventLogState state, EventLogAction.LoadEvents action)
    {
        var newLogsCollection = state.ActiveLogs;

        if (state.ActiveLogs.ContainsKey(action.LogName))
        {
            newLogsCollection = state.ActiveLogs.Remove(action.LogName);
        }

        var filtered = GetFilteredEvents(action.Events, state.AppliedFilter, out var needsSort);
        // We always sort in LoadEvents, so we don't care about needsSort here.
        filtered = SortOneLog(filtered, state.SortDescending);

        newLogsCollection = newLogsCollection.Add(action.LogName, new EventLogData
        (
            action.LogName,
            action.Type,
            // Events collection is always ordered descending by record id
            action.Events.OrderByDescending(e => e.RecordId).ToList().AsReadOnly(),
            // Filtered events reflects both the filter and sort choice.
            filtered.ToList().AsReadOnly(),
            action.AllEventIds.ToImmutableHashSet(),
            action.AllProviderNames.ToImmutableHashSet(),
            action.AllTaskNames.ToImmutableHashSet(),
            action.AllKeywords.ToImmutableHashSet()
        ));

        var newCombinedEvents = CombineLogs(newLogsCollection.Values.Select(l => l.FilteredEvents), state.SortDescending);

        return state with { ActiveLogs = newLogsCollection, CombinedEvents = newCombinedEvents.ToList().AsReadOnly() };
    }

    [ReducerMethod]
    public static EventLogState ReduceLoadNewEvents(EventLogState state, EventLogAction.LoadNewEvents action)
    {
        return ProcessNewEventBuffer(state);
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
        var newState = state with
        {
            ActiveLogs = state.ActiveLogs.Remove(action.LogName),
            CombinedEvents = state.CombinedEvents.Where(e => e.OwningLog != action.LogName).ToList().AsReadOnly(),
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
            CombinedEvents = new List<DisplayEventModel>().AsReadOnly(),
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
    public static EventLogState ReduceSelectLog(EventLogState state, EventLogAction.SelectLog action)
    {
        return state with { SelectedLogName = action.LogName };
    }

    [ReducerMethod]
    public static EventLogState ReduceSetContinouslyUpdate(EventLogState state, EventLogAction.SetContinouslyUpdate action)
    {
        var newState = state with { ContinuouslyUpdate = action.ContinuouslyUpdate };
        if (action.ContinuouslyUpdate)
        {
            newState = ProcessNewEventBuffer(newState);
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
        else
        {
            return state with { EventsLoading = newEventsLoading.Add(action.ActivityId, action.Count) };
        }
    }

    [ReducerMethod]
    public static EventLogState ReduceSetFilters(EventLogState state, EventLogAction.SetFilters action)
    {
        var filterChanged = false;

        if (action.EventFilter.AdvancedFilter != state.AppliedFilter.AdvancedFilter ||
            action.EventFilter.DateFilter != state.AppliedFilter.DateFilter ||
            action.EventFilter.Filters.Count != state.AppliedFilter.Filters.Count)
        {
            filterChanged = true;
        }
        else
        {
            for (var i = 0; i < action.EventFilter.Filters.Count; i++)
            {
                var actionFilterGroup = action.EventFilter.Filters[i];
                var stateFilterGroup = state.AppliedFilter.Filters[i];
                if (!actionFilterGroup.SequenceEqual(stateFilterGroup))
                {
                    filterChanged = true;
                    break;
                }
            }
        }

        if (!filterChanged)
        {
            return state;
        }

        var newState = state;

        foreach (var entry in state.ActiveLogs.Values)
        {
            EventLogData newLogData;
            if (action.EventFilter.DateFilter is null && action.EventFilter.AdvancedFilter == "" && action.EventFilter.Filters is null)
            {
                newLogData = entry with { FilteredEvents = entry.Events };
            }
            else
            {
                var filteredEvents = GetFilteredEvents(entry.Events, action.EventFilter, out var needsSort);
                if (needsSort)
                {
                    filteredEvents = SortOneLog(filteredEvents, state.SortDescending);
                }

                newLogData = entry with { FilteredEvents = filteredEvents.ToList().AsReadOnly() };
            }

            newState = newState with
            {
                ActiveLogs = newState.ActiveLogs.Remove(entry.Name).Add(entry.Name, newLogData)
            };
        }

        var newCombinedEvents = CombineLogs(newState.ActiveLogs.Values.Select(l => l.FilteredEvents), state.SortDescending);

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
        if (action.SortDescending == state.SortDescending) return state;

        var newActiveLogs = state.ActiveLogs;

        foreach (var logData in state.ActiveLogs.Values)
        {
            newActiveLogs = newActiveLogs.Remove(logData.Name).Add(logData.Name, logData with
            {
                FilteredEvents = SortOneLog(logData.FilteredEvents, action.SortDescending).ToList().AsReadOnly()
            }); ;
        }

        var newCombinedEvents = CombineLogs(newActiveLogs.Values.Select(l => l.FilteredEvents), action.SortDescending);

        return state with
        {
            ActiveLogs = newActiveLogs,
            CombinedEvents = newCombinedEvents.ToList().AsReadOnly(),
            SortDescending = action.SortDescending
        };
    }

    /// <summary>
    /// Add new events to a "combined" log view
    /// </summary>
    /// <param name="combinedLog"></param>
    /// <param name="eventsToAdd">
    ///     It is assumed that these events are already sorted in descending order
    ///     from newest to oldest. This value should be coming from NewEventBuffer,
    ///     where new events are inserted at the top of the list as they come in.
    /// </param>
    /// <param name="filter"></param>
    /// <param name="sortDescending"></param>
    /// <returns></returns>
    private static IEnumerable<DisplayEventModel> AddEventsToCombinedLog(IEnumerable<DisplayEventModel> combinedLog, IEnumerable<DisplayEventModel> eventsToAdd, EventFilter filter, bool sortDescending)
    {
        var newEventsMatchingFilter = GetFilteredEvents(eventsToAdd, filter, out var needsSort);

        if (newEventsMatchingFilter.Count() > 1 && needsSort)
        {
            newEventsMatchingFilter = sortDescending ? newEventsMatchingFilter.OrderByDescending(e => e.TimeCreated) : newEventsMatchingFilter.OrderBy(e => e.TimeCreated);
        }

        return sortDescending ? newEventsMatchingFilter.Concat(combinedLog) : combinedLog.Concat(newEventsMatchingFilter);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="logData">This log should already be sorted appropriately. We do not sort this here.</param>
    /// <param name="eventsToAdd">These do not need to be sorted already.</param>
    /// <param name="filter"></param>
    /// <param name="sortDescending"></param>
    /// <returns></returns>
    private static EventLogData AddEventsToOneLog(EventLogData logData, IEnumerable<DisplayEventModel> eventsToAdd, EventFilter filter, bool sortDescending)
    {
        var eventsToAddDescending = eventsToAdd.OrderByDescending(e => e.RecordId);

        var filteredEventsToAdd = GetFilteredEvents(eventsToAdd, filter, out var needsSort);
        var sortedFilteredEventsToAdd = SortOneLog(filteredEventsToAdd, sortDescending);

        // Events collection is always sorted descending by record ID.
        var updatedEvents = eventsToAddDescending.Concat(logData.Events);
        var updatedFilteredEvents = sortDescending ? sortedFilteredEventsToAdd.Concat(logData.FilteredEvents) : logData.FilteredEvents.Concat(sortedFilteredEventsToAdd);
        var updatedEventIds = logData.EventIds.Union(eventsToAdd.Select(e => e.Id));
        var updatedProviderNames = logData.EventProviderNames.Union(eventsToAdd.Select(e => e.Source));
        var updatedTaskNames = logData.TaskNames.Union(eventsToAdd.Select(e => e.TaskCategory));
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
    /// This should be used to combine events from multiple logs into a
    /// combined log. The sort key changes depending on how many logs are
    /// present.
    /// </summary>
    /// <param name="eventData"></param>
    /// <param name="sortDescending"></param>
    /// <returns></returns>
    private static IEnumerable<DisplayEventModel> CombineLogs(IEnumerable<IEnumerable<DisplayEventModel>> eventData, bool sortDescending)
    {
        Utils.Trace($"{nameof(CombineLogs)} was called for {eventData.Count()} logs.");

        if (eventData.Count() == 1)
        {
            if (sortDescending)
            {
                return eventData.First().OrderByDescending(e => e.RecordId);
            }
            else
            {
                return eventData.First().OrderBy(e => e.RecordId);
            }
        }
        else
        {
            if (sortDescending)
            {
                return eventData.SelectMany(l => l).OrderByDescending(e => e.TimeCreated);
            }
            else
            {
                return eventData.SelectMany(l => l).OrderBy(e => e.TimeCreated);
            }
        }
    }

    private static ImmutableDictionary<string, EventLogData> DistributeEventsToManyLogs(
        ImmutableDictionary<string, EventLogData> logsToUpdate, 
        IEnumerable<DisplayEventModel> eventsToDistribute, 
        EventFilter filter, 
        bool sortDescending)
    {
        var newLogs = logsToUpdate;

        foreach (var log in logsToUpdate.Values)
        {
            var newEventsForThisLog = eventsToDistribute.Where(e => e.OwningLog == log.Name);
            if (newEventsForThisLog.Any())
            {
                var newLogData = AddEventsToOneLog(log, newEventsForThisLog, filter, sortDescending);
                newLogs = newLogs.Remove(log.Name).Add(log.Name, newLogData);
            }
        }

        return newLogs;
    }

    private static EventLogData GetEmptyLogData(string LogName, LogType LogType)
    {
        return new EventLogData(
            LogName,
            LogType,
            new List<DisplayEventModel>().AsReadOnly(),
            new List<DisplayEventModel>().AsReadOnly(),
            ImmutableHashSet<int>.Empty,
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="events"></param>
    /// <param name="eventFilter"></param>
    /// <param name="needsSort">
    ///     Some filters cause a call to AsParallel which jumbles the order of events.
    ///     This lets the caller know that this has occurred.
    /// </param>
    /// <returns></returns>
    private static IEnumerable<DisplayEventModel> GetFilteredEvents(IEnumerable<DisplayEventModel> events, EventFilter eventFilter, out bool needsSort)
    {
        Utils.Trace($"{nameof(GetFilteredEvents)} was called to filter {events.Count()} events.");

        needsSort = false;

        IQueryable<DisplayEventModel> filteredEvents = events.AsQueryable();

        if (eventFilter.DateFilter is not null)
        {
            filteredEvents = filteredEvents.Where(e =>
                e.TimeCreated >= eventFilter.DateFilter.After &&
                e.TimeCreated <= eventFilter.DateFilter.Before);
        }

        if (eventFilter.Filters.Any())
        {
            filteredEvents = filteredEvents.AsParallel()
                .Where(e => eventFilter.Filters
                    .All(filter => filter
                        .Any(comp => comp(e))))
                .AsQueryable();

            needsSort = true;
        }

        if (!string.IsNullOrEmpty(eventFilter.AdvancedFilter))
        {
            filteredEvents = filteredEvents.Where(EventLogExpertCustomTypeProvider.ParsingConfig, eventFilter.AdvancedFilter);
        }

        return filteredEvents;
    }

    private static EventLogState ProcessNewEventBuffer(EventLogState state)
    {
        var newState = state with
        {
            ActiveLogs = DistributeEventsToManyLogs(state.ActiveLogs, state.NewEventBuffer, state.AppliedFilter, state.SortDescending)
        };

        var newCombinedEvents = CombineLogs(newState.ActiveLogs.Values.Select(l => l.FilteredEvents), state.SortDescending);

        newState = newState with
        {
            CombinedEvents = newCombinedEvents.ToList().AsReadOnly(),
            NewEventBuffer = new List<DisplayEventModel>().AsReadOnly(),
            NewEventBufferIsFull = false
        };

        return newState;
    }

    /// <summary>
    /// This should only be used when all events are from the same log.
    /// </summary>
    /// <param name="events"></param>
    /// <param name="sortDescending"></param>
    /// <returns></returns>
    private static IEnumerable<DisplayEventModel> SortOneLog(IEnumerable<DisplayEventModel> events, bool sortDescending)
    {
        Utils.Trace($"{nameof(SortOneLog)} was called.");

        if (sortDescending) return events.OrderByDescending(e => e.RecordId);

        return events.OrderBy(e => e.RecordId);
    }
}
