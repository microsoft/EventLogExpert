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
using System.Collections.Immutable;
using System.Threading.Channels;
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
    private static readonly int s_maxGlobalConcurrency = Math.Max(1, Environment.ProcessorCount - 1);
    private static readonly SemaphoreSlim s_resolutionThrottle = new(s_maxGlobalConcurrency, s_maxGlobalConcurrency);

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

            dispatcher.Dispatch(new EventLogAction.AddEventBuffered(updatedBuffer.AsReadOnly(), full));
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

    [EffectMethod]
    public Task HandleLoadEventsPartial(EventLogAction.LoadEventsPartial action, IDispatcher dispatcher)
    {
        var filteredEvents = _filterService.GetFilteredEvents(action.Events, _eventLogState.Value.AppliedFilter);

        dispatcher.Dispatch(new EventTableAction.AppendTableEvents(action.LogData.Id, filteredEvents));

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
        int resolved = 0;
        int lastPartialIndex = 0;
        int timerTick = 0;

        dispatcher.Dispatch(new EventTableAction.AddTable(logData));

        var channel = Channel.CreateBounded<EventRecord[]>(new BoundedChannelOptions(s_maxGlobalConcurrency * 2)
        {
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        List<DisplayEventModel> events = [];

        await using var timer = new Timer(
            _ =>
            {
                dispatcher.Dispatch(new StatusBarAction.SetEventsLoading(activityId, Volatile.Read(ref resolved), Volatile.Read(ref failed)));

                // Skip the immediate first tick (dueTime = 0) so the first partial
                // is dispatched after ~3 seconds of loading.
                if (Interlocked.Increment(ref timerTick) <= 1) { return; }

                List<DisplayEventModel> delta;

                lock (events)
                {
                    int fromIndex = Volatile.Read(ref lastPartialIndex);

                    if (events.Count <= fromIndex) { return; }

                    delta = events.GetRange(fromIndex, events.Count - fromIndex);
                    Volatile.Write(ref lastPartialIndex, events.Count);
                }

                dispatcher.Dispatch(new EventLogAction.LoadEventsPartial(logData, delta.AsReadOnly()));
            },
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(3));

        using var reader = new EventLogReader(action.LogName, action.PathType, filterState?.Value.IsXmlEnabled ?? false);

        // Producer: single thread reads batches from EventLogReader
        var producerTask = Task.Run(async () =>
        {
            try
            {
                while (reader.TryGetEvents(out EventRecord[] batch))
                {
                    action.Token.ThrowIfCancellationRequested();

                    if (batch.Length == 0) { continue; }

                    await channel.Writer.WriteAsync(batch, action.Token);
                }
            }
            catch (Exception ex)
            {
                channel.Writer.Complete(ex);

                throw;
            }

            channel.Writer.Complete();
        }, action.Token);

        try
        {
            // Consumers: parallel resolution of event batches from the channel.
            // The global semaphore limits total concurrent resolution threads across
            // all HandleOpenLog calls, preventing CPU saturation when loading multiple logs.
            await Parallel.ForEachAsync(
                channel.Reader.ReadAllAsync(action.Token),
                new ParallelOptions
                {
                    CancellationToken = action.Token,
                    MaxDegreeOfParallelism = s_maxGlobalConcurrency
                },
                async (batch, token) =>
                {
                    await s_resolutionThrottle.WaitAsync(token);

                    try
                    {
                        List<DisplayEventModel> localBatch = new(batch.Length);
                        int localResolved = 0;

                        foreach (var @event in batch)
                        {
                            token.ThrowIfCancellationRequested();

                            try
                            {
                                if (!@event.IsSuccess)
                                {
                                    Interlocked.Increment(ref failed);

                                    _logger?.Warn($"{@event.PathName}: Bad Event: {@event.Error}");

                                    continue;
                                }

                                localBatch.Add(eventResolver.ResolveEvent(@event));
                                localResolved++;
                            }
                            catch (Exception ex)
                            {
                                _logger?.Warn($"Failed to resolve RecordId: {@event.RecordId}, {ex.Message}");
                            }
                        }

                        if (localBatch.Count > 0)
                        {
                            lock (events) { events.AddRange(localBatch); }

                            Interlocked.Add(ref resolved, localResolved);
                        }
                    }
                    finally
                    {
                        s_resolutionThrottle.Release();
                    }
                });

            await producerTask;

            lastEvent = reader.LastBookmark;
        }
        catch (OperationCanceledException)
        {
            await StopProducerAsync(producerTask);

            dispatcher.Dispatch(new EventLogAction.CloseLog(logData.Id, logData.Name));
            dispatcher.Dispatch(new StatusBarAction.ClearStatus(activityId));

            return;
        }
        catch (Exception ex)
        {
            _logger?.Error($"Failed to load log {action.LogName}: {ex.Message}");

            await StopProducerAsync(producerTask);

            dispatcher.Dispatch(new EventLogAction.CloseLog(logData.Id, logData.Name));
            dispatcher.Dispatch(new StatusBarAction.ClearStatus(activityId));
            dispatcher.Dispatch(new StatusBarAction.SetResolverStatus($"Error: Failed to load {action.LogName}"));

            return;
        }

        events.Sort((a, b) => Comparer<long?>.Default.Compare(b.RecordId, a.RecordId));

        dispatcher.Dispatch(new EventLogAction.LoadEvents(logData, events.AsReadOnly()));

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

        return logData with { Events = eventsToAdd.AsReadOnly() };
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

    /// <summary>
    ///     Awaits the producer task, suppressing all exceptions.
    ///     The sole purpose is to ensure the producer has fully stopped before
    ///     the reader is disposed. Any meaningful errors are handled by the caller.
    /// </summary>
    private static async Task StopProducerAsync(Task producerTask)
    {
        try { await producerTask; }
        catch { /* Intentionally swallowed — caller handles error reporting */ }
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
