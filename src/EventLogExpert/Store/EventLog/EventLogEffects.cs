// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.EventResolvers;
using EventLogExpert.Library.Helpers;
using EventLogExpert.Library.Models;
using Fluxor;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Store.EventLog;

public class EventLogEffects
{
    private readonly ITraceLogger _debugLogger;
    private readonly ILogWatcherService _logWatcherService;
    private readonly IEventResolver _eventResolver;

    public EventLogEffects(IEventResolver eventResolver, ITraceLogger debugLogger, ILogWatcherService logWatcherService)
    {
        _eventResolver = eventResolver;
        _debugLogger = debugLogger;
        _logWatcherService = logWatcherService;
    }

    [EffectMethod]
    public async Task HandleOpenLogAction(EventLogAction.OpenLog action, IDispatcher dispatcher)
    {
        EventLogReader reader;

        if (action.LogType == EventLogState.LogType.Live)
        {
            reader = new EventLogReader(action.LogName, PathType.LogName);
        }
        else
        {
            reader = new EventLogReader(action.LogName, PathType.FilePath);
        }

        // Do this on a background thread so we don't hang the UI
        await Task.Run(() =>
        {
            var activityId = Guid.NewGuid();

            var sw = new Stopwatch();
            sw.Start();

            List<DisplayEventModel> events = new();
            HashSet<int> eventIdsAll = new();
            HashSet<string> eventProviderNamesAll = new();
            HashSet<string> eventTaskNamesAll = new();
            HashSet<string> eventKeywordNamesAll = new();
            EventRecord lastEvent = null!;

            while (reader.ReadEvent() is { } e)
            {
                lastEvent = e;
                var resolved = _eventResolver.Resolve(e, action.LogName);
                eventIdsAll.Add(resolved.Id);
                eventProviderNamesAll.Add(resolved.Source);
                eventTaskNamesAll.Add(resolved.TaskCategory);
                eventKeywordNamesAll.UnionWith(resolved.KeywordsDisplayNames);

                events.Add(resolved);

                if (sw.ElapsedMilliseconds > 1000)
                {
                    sw.Restart();
                    dispatcher.Dispatch(new EventLogAction.SetEventsLoading(activityId, events.Count));
                }
            }

            events.Reverse();

            dispatcher.Dispatch(new EventLogAction.LoadEvents(
                action.LogName,
                action.LogType,
                events,
                eventIdsAll.ToImmutableList(),
                eventProviderNamesAll.ToImmutableList(),
                eventTaskNamesAll.ToImmutableList(),
                eventKeywordNamesAll.ToImmutableList()));

            dispatcher.Dispatch(new EventLogAction.SetEventsLoading(activityId, 0));

            if (action.LogType == EventLogState.LogType.Live)
            {
                _logWatcherService.AddLog(action.LogName, lastEvent?.Bookmark);
            }
        });
    }

    [EffectMethod]
    public async Task HandleCloseLogAction(EventLogAction.CloseLog action, IDispatcher dispatcher)
    {
        _logWatcherService.RemoveLog(action.LogName);
    }

    [EffectMethod]
    public async Task HandleCloseAllAction(EventLogAction.CloseAll action, IDispatcher dispatcher)
    {
        _logWatcherService.RemoveAll();
    }
}
