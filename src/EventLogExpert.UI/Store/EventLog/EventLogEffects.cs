// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.EventTable;
using EventLogExpert.UI.Store.Settings;
using EventLogExpert.UI.Store.StatusBar;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.Eventing.Reader;
using System.Xml.Linq;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.UI.Store.EventLog;

public sealed class EventLogEffects(
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
        logWatcherService.RemoveAll();

        dispatcher.Dispatch(new EventTableAction.CloseAll());
        dispatcher.Dispatch(new StatusBarAction.CloseAll());

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleCloseLog(EventLogAction.CloseLog action, IDispatcher dispatcher)
    {
        logWatcherService.RemoveLog(action.LogName);

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

        EventLogQuery eventLog = action.LogType == LogType.Live ?
            new EventLogQuery(action.LogName, PathType.LogName) :
            new EventLogQuery(action.LogName, PathType.FilePath);

        var activityId = Guid.NewGuid();

        dispatcher.Dispatch(new EventTableAction.AddTable(logData));

        EventRecord? lastEvent = null;

        const int batchSize = 200;
        bool doneReading = false;
        ConcurrentQueue<EventRecord> records = new();
        ConcurrentQueue<DisplayEventModel> events = new();

        await using Timer timer = new(
            _ => { dispatcher.Dispatch(new StatusBarAction.SetEventsLoading(activityId, events.Count)); },
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1));

        try
        {
            // Don't need to wait on this since we are waiting for doneReading in the resolver tasks
            _ = Task.Run(() =>
            {
                using var reader = new EventLogReader(eventLog);

                int count = 0;

                while (reader.ReadEvent() is { } e)
                {
                    action.Token.ThrowIfCancellationRequested();

                    if (count % batchSize == 0)
                    {
                        records.Enqueue(e);
                    }

                    count++;

                    lastEvent = e;
                }

                doneReading = true;
            },
            action.Token);

            await Parallel.ForEachAsync(
                Enumerable.Range(1, 8),
                action.Token,
                (_, token) =>
                {
                    using var reader = new EventLogReader(eventLog);

                    while (records.TryDequeue(out EventRecord? @event) || !doneReading)
                    {
                        token.ThrowIfCancellationRequested();

                        if (@event is null) { continue; }

                        reader.Seek(@event.Bookmark);

                        for (int i = 0; i < batchSize; i++)
                        {
                            @event = reader.ReadEvent();

                            if (@event is null) { break; }

                            try
                            {
                                eventResolver.ResolveProviderDetails(@event, action.LogName);

                                events.Enqueue(
                                    new DisplayEventModel(action.LogName)
                                    {
                                        ActivityId = @event.ActivityId,
                                        ComputerName = logData.ValueCache.Get(@event.MachineName),
                                        Description = logData.ValueCache.Get(eventResolver.ResolveDescription(@event)),
                                        Id = @event.Id,
                                        KeywordsDisplayNames = eventResolver.GetKeywordsFromBitmask(@event)
                                            .Select(keyword => logData.ValueCache.Get(keyword)).ToList(),
                                        Level = Severity.GetString(@event.Level),
                                        LogName = logData.ValueCache.Get(@event.LogName),
                                        ProcessId = @event.ProcessId,
                                        RecordId = @event.RecordId,
                                        Source = logData.ValueCache.Get(@event.ProviderName),
                                        TaskCategory = logData.ValueCache.Get(eventResolver.ResolveTaskName(@event)),
                                        ThreadId = @event.ThreadId,
                                        TimeCreated = @event.TimeCreated!.Value.ToUniversalTime(),
                                        UserId = @event.UserId,
                                        Xml = settingsState.Value.Config.IsXmlEnabled ? @event.ToXml() : null
                                    });
                            }
                            catch (Exception ex) when (ex is EventLogInvalidDataException)
                            {
                                logger ??= serviceScope.ServiceProvider.GetService<ITraceLogger>();

                                logger?.Trace(
                                    $"Invalid data in RecordId: {
                                        XElement.Parse(@event.Bookmark.BookmarkXml)
                                            .Descendants()
                                            .Attributes()
                                            .Where(a => a.Name == "RecordId")
                                            .Select(a => a.Value)
                                            .FirstOrDefault()}",
                                    LogLevel.Error);
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

        if (action.LogType == LogType.Live)
        {
            logWatcherService.AddLog(action.LogName, lastEvent?.Bookmark);
        }

        dispatcher.Dispatch(new StatusBarAction.SetResolverStatus(string.Empty));
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
