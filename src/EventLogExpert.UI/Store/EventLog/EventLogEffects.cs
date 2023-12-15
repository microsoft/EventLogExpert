// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.EventTable;
using EventLogExpert.UI.Store.StatusBar;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.UI.Store.EventLog;

public sealed class EventLogEffects(
    IState<EventLogState> eventLogState,
    ILogWatcherService logWatcherService,
    IServiceProvider serviceProvider)
{
    [EffectMethod(typeof(EventLogAction.CloseAll))]
    public static Task HandleCloseAll(IDispatcher dispatcher)
    {
        dispatcher.Dispatch(new EventTableAction.CloseAll());

        return Task.CompletedTask;
    }

    [EffectMethod]
    public static Task HandleCloseLog(EventLogAction.CloseLog action, IDispatcher dispatcher)
    {
        dispatcher.Dispatch(new EventTableAction.CloseLog(action.LogName));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleAddEvent(EventLogAction.AddEvent action, IDispatcher dispatcher)
    {
        // Sometimes the watcher doesn't stop firing events immediately. Let's
        // make sure the events being added are for a log that is still "open".
        if (!eventLogState.Value.ActiveLogs.ContainsKey(action.NewEvent.OwningLog)) { return Task.CompletedTask; }

        var newEvent = new[]
        {
            action.NewEvent
        };

        if (eventLogState.Value.ContinuouslyUpdate)
        {
            var activeLogs = DistributeEventsToManyLogs(eventLogState.Value.ActiveLogs, newEvent);

            var filteredActiveLogs = FilterMethods.FilterActiveLogs(activeLogs, eventLogState.Value.AppliedFilter);

            dispatcher.Dispatch(new EventLogAction.AddEventSuccess(activeLogs));
            dispatcher.Dispatch(new EventTableAction.UpdateDisplayedEvents(filteredActiveLogs));
        }
        else
        {
            var updatedBuffer = newEvent.Concat(eventLogState.Value.NewEventBuffer).ToList().AsReadOnly();
            var full = updatedBuffer.Count >= eventLogState.Value.MaxNewEvents;

            dispatcher.Dispatch(new EventLogAction.AddEventBuffered(updatedBuffer, full));
        }

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(EventLogAction.CloseAll))]
    public Task HandleCloseAllAction(IDispatcher dispatcher)
    {
        logWatcherService.RemoveAll();

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleCloseLogAction(EventLogAction.CloseLog action, IDispatcher dispatcher)
    {
        logWatcherService.RemoveLog(action.LogName);

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleLoadEvents(EventLogAction.LoadEvents action, IDispatcher dispatcher)
    {
        var newLogsCollection = eventLogState.Value.ActiveLogs;

        if (newLogsCollection.ContainsKey(action.LogName))
        {
            newLogsCollection = newLogsCollection.Remove(action.LogName);
        }

        // Events collection is always ordered descending by record id
        var sortedEvents = action.Events.SortEvents().ToList();

        newLogsCollection = newLogsCollection.Add(
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
            ));

        var filteredActiveLogs = FilterMethods.FilterActiveLogs(newLogsCollection, eventLogState.Value.AppliedFilter);

        dispatcher.Dispatch(new EventLogAction.LoadEventsSuccess(newLogsCollection));
        dispatcher.Dispatch(new EventTableAction.UpdateDisplayedEvents(filteredActiveLogs));

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(EventLogAction.LoadNewEvents))]
    public Task HandleLoadNewEvents(IDispatcher dispatcher)
    {
        ProcessNewEventBuffer(eventLogState.Value, dispatcher);

        return Task.CompletedTask;
    }

    [EffectMethod]
    public async Task HandleOpenLog(EventLogAction.OpenLog action, IDispatcher dispatcher)
    {
        EventLogReader reader = action.LogType == LogType.Live ?
            new EventLogReader(action.LogName, PathType.LogName) :
            new EventLogReader(action.LogName, PathType.FilePath);

        dispatcher.Dispatch(new EventTableAction.NewTable(
            action.LogType == LogType.Live ? null : action.LogName,
            action.LogName,
            action.LogType));

        // Do this on a background thread so we don't hang the UI
        await Task.Run(() =>
            {
                IEventResolver? eventResolver;

                try
                {
                    eventResolver = serviceProvider.GetService<IEventResolver>();
                }
                catch (Exception ex)
                {
                    dispatcher.Dispatch(new StatusBarAction.SetResolverStatus($"{ex.GetType}: {ex.Message}"));
                    return;
                }

                if (eventResolver == null)
                {
                    dispatcher.Dispatch(new StatusBarAction.SetResolverStatus("Error: No event resolver available."));
                    return;
                }

                try
                {
                    var activityId = Guid.NewGuid();

                    var sw = new Stopwatch();
                    sw.Start();

                    List<DisplayEventModel> events = [];
                    HashSet<int> eventIdsAll = [];
                    HashSet<Guid?> eventActivityIdsAll = [];
                    HashSet<string> eventProviderNamesAll = [];
                    HashSet<string> eventTaskNamesAll = [];
                    HashSet<string> eventKeywordNamesAll = [];
                    EventRecord lastEvent = null!;

                    while (reader.ReadEvent() is { } e)
                    {
                        lastEvent = e;
                        var resolved = eventResolver.Resolve(e, action.LogName);
                        eventIdsAll.Add(resolved.Id);
                        eventActivityIdsAll.Add(resolved.ActivityId);
                        eventProviderNamesAll.Add(resolved.Source);
                        eventTaskNamesAll.Add(resolved.TaskCategory);
                        eventKeywordNamesAll.UnionWith(resolved.KeywordsDisplayNames);

                        events.Add(resolved);

                        if (sw.ElapsedMilliseconds > 2000)
                        {
                            sw.Restart();
                            dispatcher.Dispatch(new EventLogAction.SetEventsLoading(activityId, events.Count));
                        }
                    }

                    dispatcher.Dispatch(new EventLogAction.LoadEvents(
                        action.LogName,
                        action.LogType,
                        events,
                        eventIdsAll.ToImmutableList(),
                        eventActivityIdsAll.ToImmutableList(),
                        eventProviderNamesAll.ToImmutableList(),
                        eventTaskNamesAll.ToImmutableList(),
                        eventKeywordNamesAll.ToImmutableList()));

                    dispatcher.Dispatch(new EventTableAction.ToggleLoading(action.LogName));

                    dispatcher.Dispatch(new EventLogAction.SetEventsLoading(activityId, 0));

                    if (action.LogType == LogType.Live)
                    {
                        logWatcherService.AddLog(action.LogName, lastEvent?.Bookmark);
                    }
                }
                finally
                {
                    eventResolver.Dispose();
                }
            },
            new CancellationToken());
    }

    [EffectMethod]
    public Task HandleSetContinouslyUpdate(EventLogAction.SetContinouslyUpdate action, IDispatcher dispatcher)
    {
        if (action.ContinuouslyUpdate)
        {
            ProcessNewEventBuffer(eventLogState.Value, dispatcher);
        }

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleSetFilters(EventLogAction.SetFilters action, IDispatcher dispatcher)
    {
        if (!FilterMethods.HasFilteringChanged(action.EventFilter, eventLogState.Value.AppliedFilter))
        {
            return Task.CompletedTask;
        }

        var filteredActiveLogs = FilterMethods.FilterActiveLogs(eventLogState.Value.ActiveLogs, action.EventFilter);

        dispatcher.Dispatch(new EventLogAction.SetFiltersSuccess(action.EventFilter));
        dispatcher.Dispatch(new EventTableAction.UpdateDisplayedEvents(filteredActiveLogs));

        return Task.CompletedTask;
    }

    /// <summary>Adds new events to the currently opened log</summary>
    private static EventLogData AddEventsToOneLog(EventLogData logData, IEnumerable<DisplayEventModel> eventsToAdd)
    {
        var newEvents = eventsToAdd
            .Concat(logData.Events)
            .ToList()
            .AsReadOnly();

        var updatedEventIds = logData.EventIds.Union(newEvents.Select(e => e.Id));
        var updatedProviderNames = logData.EventProviderNames.Union(newEvents.Select(e => e.Source));
        var updatedTaskNames = logData.TaskNames.Union(newEvents.Select(e => e.TaskCategory));

        var updatedLogData = logData with
        {
            Events = newEvents,
            EventIds = updatedEventIds,
            EventProviderNames = updatedProviderNames,
            TaskNames = updatedTaskNames
        };

        return updatedLogData;
    }

    private static ImmutableDictionary<string, EventLogData> DistributeEventsToManyLogs(
        ImmutableDictionary<string, EventLogData> logsToUpdate,
        IEnumerable<DisplayEventModel> eventsToDistribute)
    {
        var newLogs = logsToUpdate;
        var events = eventsToDistribute.ToList();

        foreach (var log in logsToUpdate.Values)
        {
            var newEventsForThisLog = events.Where(e => e.OwningLog == log.Name).ToList();

            if (newEventsForThisLog.Count <= 0) { continue; }

            var newLogData = AddEventsToOneLog(log, newEventsForThisLog);
            newLogs = newLogs.Remove(log.Name).Add(log.Name, newLogData);
        }

        return newLogs;
    }

    private static void ProcessNewEventBuffer(EventLogState state, IDispatcher dispatcher)
    {
        var activeLogs = DistributeEventsToManyLogs(state.ActiveLogs, state.NewEventBuffer);

        var filteredActiveLogs = FilterMethods.FilterActiveLogs(activeLogs, state.AppliedFilter);

        dispatcher.Dispatch(new EventLogAction.AddEventSuccess(activeLogs));
        dispatcher.Dispatch(new EventTableAction.UpdateDisplayedEvents(filteredActiveLogs));
        dispatcher.Dispatch(new EventLogAction.AddEventBuffered(new List<DisplayEventModel>().AsReadOnly(), false));
    }
}
