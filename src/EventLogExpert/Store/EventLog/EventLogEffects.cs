// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.EventResolvers;
using EventLogExpert.Library.Helpers;
using EventLogExpert.Library.Models;
using EventLogExpert.Store.StatusBar;
using Fluxor;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Store.EventLog;

public class EventLogEffects
{
    private readonly ITraceLogger _debugLogger;
    private readonly IEventResolver _eventResolver;

    public EventLogEffects(IEventResolver eventResolver, ITraceLogger debugLogger)
    {
        _eventResolver = eventResolver;
        _debugLogger = debugLogger;
    }

    [EffectMethod]
    public async Task HandleOpenLogAction(EventLogAction.OpenLog action, IDispatcher dispatcher)
    {
        dispatcher.Dispatch(new EventLogAction.ClearEvents());

        EventLogReader reader;

        if (action.LogSpecifier.LogType == EventLogState.LogType.Live)
        {
            reader = new EventLogReader(action.LogSpecifier.Name, PathType.LogName);
        }
        else
        {
            reader = new EventLogReader(action.LogSpecifier.Name, PathType.FilePath);
        }

        // Do this on a background thread so we don't hang the UI
        await Task.Run(() =>
        {
            var sw = new Stopwatch();
            sw.Start();

            List<DisplayEventModel> events = new();
            HashSet<int> eventIdsAll = new();
            HashSet<string> eventProviderNamesAll = new();
            HashSet<string> eventTaskNamesAll = new();
            EventRecord lastEvent = null!;

            while (reader.ReadEvent() is { } e)
            {
                lastEvent = e;
                var resolved = _eventResolver.Resolve(e);
                eventIdsAll.Add(resolved.Id);
                eventProviderNamesAll.Add(resolved.Source);
                eventTaskNamesAll.Add(resolved.TaskCategory);

                events.Add(resolved);

                if (sw.ElapsedMilliseconds > 1000)
                {
                    sw.Restart();
                    dispatcher.Dispatch(new StatusBarAction.SetEventsLoaded(events.Count));
                }
            }

            dispatcher.Dispatch(new StatusBarAction.SetEventsLoaded(events.Count));

            events.Reverse();

            LiveLogWatcher watcher = null!;

            if (action.LogSpecifier.LogType == EventLogState.LogType.Live)
            {
                watcher = new LiveLogWatcher(
                    action.LogSpecifier.Name,
                    lastEvent?.Bookmark,
                    _debugLogger,
                    _eventResolver,
                    dispatcher);

                watcher.StartWatching();

                _debugLogger.Trace("LiveLogWatcher created and started.");
            }

            dispatcher.Dispatch(new EventLogAction.LoadEvents(events,
                watcher,
                eventIdsAll.ToImmutableList(),
                eventProviderNamesAll.ToImmutableList(),
                eventTaskNamesAll.ToImmutableList()));
        });
    }
}
