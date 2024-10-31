// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Reader;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.EventTable;
using EventLogExpert.UI.Store.Settings;
using EventLogExpert.UI.Store.StatusBar;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Security.Principal;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.UI.Store.EventLog;

public sealed class EventLogEffects(
    IEventResolverCache resolverCache,
    IState<EventLogState> eventLogState,
    ILogWatcherService logWatcherService,
    IState<SettingsState> settingsState,
    IServiceScopeFactory serviceScopeFactory)
{
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

            var filteredActiveLogs = FilterMethods.FilterActiveLogs(activeLogs.Values, eventLogState.Value.AppliedFilter);

            dispatcher.Dispatch(new EventLogAction.AddEventSuccess(activeLogs));
            dispatcher.Dispatch(new EventTableAction.UpdateDisplayedEvents(filteredActiveLogs));
        }
        else
        {
            var updatedBuffer = newEvent.Concat(eventLogState.Value.NewEventBuffer).ToList().AsReadOnly();
            var full = updatedBuffer.Count >= EventLogState.MaxNewEvents;

            dispatcher.Dispatch(new EventLogAction.AddEventBuffered(updatedBuffer, full));
        }

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(EventLogAction.CloseAll))]
    public Task HandleCloseAll(IDispatcher dispatcher)
    {
        //logWatcherService.RemoveAll();

        dispatcher.Dispatch(new EventTableAction.CloseAll());
        dispatcher.Dispatch(new StatusBarAction.CloseAll());

        resolverCache.ClearAll();

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleCloseLog(EventLogAction.CloseLog action, IDispatcher dispatcher)
    {
        //logWatcherService.RemoveLog(action.LogName);

        dispatcher.Dispatch(new EventTableAction.CloseLog(action.LogId));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleLoadEvents(EventLogAction.LoadEvents action, IDispatcher dispatcher)
    {
        var filteredEvents = FilterMethods.GetFilteredEvents(action.Events, eventLogState.Value.AppliedFilter);

        dispatcher.Dispatch(new EventTableAction.UpdateTable(action.LogData.Id, filteredEvents));

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
        using var serviceScope = serviceScopeFactory.CreateScope();

        var eventResolver = serviceScope.ServiceProvider.GetService<IEventResolver>();
        ITraceLogger? logger = null;

        if (eventResolver is null)
        {
            dispatcher.Dispatch(new StatusBarAction.SetResolverStatus("Error: No event resolver available"));

            return;
        }

        if (!eventLogState.Value.ActiveLogs.TryGetValue(action.LogName, out var logData))
        {
            dispatcher.Dispatch(new StatusBarAction.SetResolverStatus($"Error: Failed to open {action.LogName}"));

            return;
        }

        var activityId = Guid.NewGuid();

        dispatcher.Dispatch(new EventTableAction.AddTable(logData));

        DisplayEventModel? lastEvent = null;
        ConcurrentQueue<DisplayEventModel> events = new();

        await using Timer timer = new(
            _ => { dispatcher.Dispatch(new StatusBarAction.SetEventsLoading(activityId, events.Count)); },
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1));

        using var reader = new EventLogReader(action.LogName, action.PathType);

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
                                if (@event.RecordId is null) { continue; }

                                eventResolver.ResolveProviderDetails(@event, action.LogName);

                                events.Enqueue(
                                    new DisplayEventModel(action.LogName)
                                    {
                                        ActivityId = @event.ActivityId,
                                        ComputerName = resolverCache.GetValue(@event.ComputerName),
                                        Description = resolverCache.GetDescription(eventResolver.ResolveDescription(@event)),
                                        Id = @event.Id,
                                        KeywordsDisplayNames = eventResolver.GetKeywordsFromBitmask(@event)
                                            .Select(resolverCache.GetValue).ToList(),
                                        Level = Severity.GetString(@event.Level),
                                        LogName = resolverCache.GetValue(@event.LogName),
                                        ProcessId = @event.ProcessId,
                                        RecordId = @event.RecordId,
                                        Source = resolverCache.GetValue(@event.ProviderName),
                                        TaskCategory = resolverCache.GetValue(eventResolver.ResolveTaskName(@event)),
                                        ThreadId = @event.ThreadId,
                                        TimeCreated = @event.TimeCreated.ToUniversalTime(),
                                        UserId = @event.UserId ?? new SecurityIdentifier(WellKnownSidType.NullSid, null),
                                        Xml = settingsState.Value.Config.IsXmlEnabled ? eventResolver.GetXml(@event) : string.Empty
                                    });
                            }
                            catch (Exception ex)
                            {
                                logger ??= serviceScope.ServiceProvider.GetService<ITraceLogger>();

                                logger?.Trace($"Failed to resolve RecordId: {@event.RecordId}, {ex.Message}",
                                    LogLevel.Error);
                            }
                        }
                    }

                    return ValueTask.CompletedTask;
                });
        }
        catch (TaskCanceledException)
        {
            dispatcher.Dispatch(new EventLogAction.CloseLog(logData.Id, logData.Name));
            dispatcher.Dispatch(new StatusBarAction.ClearStatus(activityId));

            return;
        }

        dispatcher.Dispatch(new EventLogAction.LoadEvents(logData, events.ToList().AsReadOnly()));

        dispatcher.Dispatch(new StatusBarAction.SetEventsLoading(activityId, 0));

        //if (action.LogType == LogType.Live)
        //{
        //    logWatcherService.AddLog(action.LogName, lastEvent?.Bookmark);
        //}

        dispatcher.Dispatch(new StatusBarAction.SetResolverStatus(string.Empty));
    }

    [EffectMethod]
    public Task HandleSetContinuouslyUpdate(EventLogAction.SetContinuouslyUpdate action, IDispatcher dispatcher)
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
        var filteredActiveLogs = FilterMethods.FilterActiveLogs(eventLogState.Value.ActiveLogs.Values, action.EventFilter);

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

        var updatedLogData = logData with { Events = newEvents };

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

        var filteredActiveLogs = FilterMethods.FilterActiveLogs(activeLogs.Values, state.AppliedFilter);

        dispatcher.Dispatch(new EventTableAction.UpdateDisplayedEvents(filteredActiveLogs));
        dispatcher.Dispatch(new EventLogAction.AddEventSuccess(activeLogs));
        dispatcher.Dispatch(new EventLogAction.AddEventBuffered(new List<DisplayEventModel>().AsReadOnly(), false));
    }
}
