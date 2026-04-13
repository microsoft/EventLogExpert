// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.EventTable;
using EventLogExpert.UI.Store.FilterPane;
using EventLogExpert.UI.Store.StatusBar;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.UI.Store.EventLog;

public sealed class EventLogEffects(
    IState<EventLogState> eventLogState,
    IFilterService filterService,
    ITraceLogger logger,
    ILogWatcherService logWatcherService,
    IEventResolverCache resolverCache,
    IServiceScopeFactory serviceScopeFactory)
{
    private readonly IState<EventLogState> _eventLogState = eventLogState;
    private readonly IFilterService _filterService = filterService;
    private readonly ITraceLogger _logger = logger;
    private readonly ILogWatcherService _logWatcherService = logWatcherService;
    private readonly IEventResolverCache _resolverCache = resolverCache;
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;

    [EffectMethod]
    public Task HandleAddEvent(EventLogAction.AddEvent action, IDispatcher dispatcher)
    {
        // Sometimes the watcher doesn't stop firing events immediately. Let's
        // make sure the events being added are for a log that is still "open".
        if (!_eventLogState.Value.ActiveLogs.ContainsKey(action.NewEvent.OwningLog)) { return Task.CompletedTask; }

        if (_eventLogState.Value.ContinuouslyUpdate)
        {
            var activeLogs = DistributeEventsToManyLogs(
                _eventLogState.Value.ActiveLogs,
                [action.NewEvent]);

            var filteredActiveLogs = _filterService.FilterActiveLogs(activeLogs.Values, _eventLogState.Value.AppliedFilter);

            dispatcher.Dispatch(new EventLogAction.AddEventSuccess(activeLogs));
            dispatcher.Dispatch(new EventTableAction.UpdateDisplayedEvents(filteredActiveLogs));
        }
        else
        {
            var updatedBuffer = new List<DisplayEventModel>(_eventLogState.Value.NewEventBuffer.Count + 1)
            {
                action.NewEvent
            };

            updatedBuffer.AddRange(_eventLogState.Value.NewEventBuffer);

            var full = updatedBuffer.Count >= EventLogState.MaxNewEvents;

            dispatcher.Dispatch(new EventLogAction.AddEventBuffered(updatedBuffer, full));
        }

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(EventLogAction.CloseAll))]
    public Task HandleCloseAll(IDispatcher dispatcher)
    {
        _logWatcherService.RemoveAll();

        dispatcher.Dispatch(new EventTableAction.CloseAll());
        dispatcher.Dispatch(new StatusBarAction.CloseAll());

        _resolverCache.ClearAll();

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleCloseLog(EventLogAction.CloseLog action, IDispatcher dispatcher)
    {
        _logWatcherService.RemoveLog(action.LogName);

        dispatcher.Dispatch(new EventTableAction.CloseLog(action.LogId));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleLoadEvents(EventLogAction.LoadEvents action, IDispatcher dispatcher)
    {
        var filteredEvents = _filterService.GetFilteredEvents(action.Events, _eventLogState.Value.AppliedFilter);

        dispatcher.Dispatch(new EventTableAction.UpdateTable(action.LogData.Id, filteredEvents));

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(EventLogAction.LoadNewEvents))]
    public Task HandleLoadNewEvents(IDispatcher dispatcher)
    {
        ProcessNewEventBuffer(_eventLogState.Value, dispatcher);

        return Task.CompletedTask;
    }

    [EffectMethod]
    public async Task HandleOpenLog(EventLogAction.OpenLog action, IDispatcher dispatcher)
    {
        using var serviceScope = _serviceScopeFactory.CreateScope();

        var eventResolver = serviceScope.ServiceProvider.GetService<IEventResolver>();

        if (eventResolver is null)
        {
            dispatcher.Dispatch(new StatusBarAction.SetResolverStatus("Error: No event resolver available"));

            return;
        }

        if (!_eventLogState.Value.ActiveLogs.TryGetValue(action.LogName, out var logData))
        {
            dispatcher.Dispatch(new StatusBarAction.SetResolverStatus($"Error: Failed to open {action.LogName}"));

            return;
        }

        var filterState = serviceScope.ServiceProvider.GetService<IState<FilterPaneState>>();

        var activityId = Guid.NewGuid();
        string? lastEvent;
        int failed = 0;

        dispatcher.Dispatch(new EventTableAction.AddTable(logData));

        ConcurrentQueue<DisplayEventModel> events = new();

        await using Timer timer = new(
            _ => { dispatcher.Dispatch(new StatusBarAction.SetEventsLoading(activityId, events.Count, failed)); },
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1));

        using var reader = new EventLogReader(action.LogName, action.PathType, filterState?.Value.IsXmlEnabled ?? false);

        try
        {
            await Parallel.ForEachAsync(
                Enumerable.Range(1, 8),
                action.Token,
                (_, token) =>
                {
                    while (reader.TryGetEvents(out EventRecord[]? eventRecords))
                    {
                        token.ThrowIfCancellationRequested();

                        if (eventRecords.Length == 0) { continue; }

                        foreach (var @event in eventRecords)
                        {
                            try
                            {
                                if (!@event.IsSuccess) {
                                    Interlocked.Increment(ref failed);

                                    _logger?.Warn($"{@event.PathName}: Bad Event: {@event.Error}");

                                    continue;
                                }

                                events.Enqueue(eventResolver.ResolveEvent(@event));
                            }
                            catch (Exception ex)
                            {
                                _logger?.Warn($"Failed to resolve RecordId: {@event.RecordId}, {ex.Message}");
                            }
                        }
                    }

                    return ValueTask.CompletedTask;
                });

            lastEvent = reader.LastBookmark;
        }
        catch (TaskCanceledException)
        {
            dispatcher.Dispatch(new EventLogAction.CloseLog(logData.Id, logData.Name));
            dispatcher.Dispatch(new StatusBarAction.ClearStatus(activityId));

            return;
        }

        dispatcher.Dispatch(
            new EventLogAction.LoadEvents(
                logData,
                [.. events.OrderByDescending(e => e.RecordId)]));

        dispatcher.Dispatch(new StatusBarAction.SetEventsLoading(activityId, 0, 0));

        if (action.PathType == PathType.LogName)
        {
            _logWatcherService.AddLog(action.LogName, lastEvent);
        }

        dispatcher.Dispatch(new StatusBarAction.SetResolverStatus(string.Empty));
    }

    [EffectMethod]
    public Task HandleSetContinuouslyUpdate(EventLogAction.SetContinuouslyUpdate action, IDispatcher dispatcher)
    {
        if (action.ContinuouslyUpdate)
        {
            ProcessNewEventBuffer(_eventLogState.Value, dispatcher);
        }

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleSetFilters(EventLogAction.SetFilters action, IDispatcher dispatcher)
    {
        var filteredActiveLogs = _filterService.FilterActiveLogs(_eventLogState.Value.ActiveLogs.Values, action.EventFilter);

        dispatcher.Dispatch(new EventTableAction.UpdateDisplayedEvents(filteredActiveLogs));

        return Task.CompletedTask;
    }

    /// <summary>Adds new events to the currently opened log</summary>
    private static EventLogData AddEventsToOneLog(EventLogData logData, List<DisplayEventModel> eventsToAdd)
    {
        eventsToAdd.AddRange(logData.Events);

        return logData with { Events = eventsToAdd };
    }

    private static ImmutableDictionary<string, EventLogData> DistributeEventsToManyLogs(
        ImmutableDictionary<string, EventLogData> logsToUpdate,
        IEnumerable<DisplayEventModel> eventsToDistribute)
    {
        // Group events by owning log once to avoid repeated enumeration
        var eventsByLog = new Dictionary<string, List<DisplayEventModel>>();

        foreach (var e in eventsToDistribute)
        {
            if (!logsToUpdate.ContainsKey(e.OwningLog)) { continue; }

            if (!eventsByLog.TryGetValue(e.OwningLog, out var list))
            {
                list = [];
                eventsByLog[e.OwningLog] = list;
            }

            list.Add(e);
        }

        var newLogs = logsToUpdate;

        foreach (var (logName, newEvents) in eventsByLog)
        {
            var log = logsToUpdate[logName];
            var newLogData = AddEventsToOneLog(log, newEvents);
            newLogs = newLogs.SetItem(logName, newLogData);
        }

        return newLogs;
    }

    private void ProcessNewEventBuffer(EventLogState state, IDispatcher dispatcher)
    {
        var activeLogs = DistributeEventsToManyLogs(state.ActiveLogs, state.NewEventBuffer);

        var filteredActiveLogs = _filterService.FilterActiveLogs(activeLogs.Values, state.AppliedFilter);

        dispatcher.Dispatch(new EventTableAction.UpdateDisplayedEvents(filteredActiveLogs));
        dispatcher.Dispatch(new EventLogAction.AddEventSuccess(activeLogs));
        dispatcher.Dispatch(new EventLogAction.AddEventBuffered([], false));
    }
}
