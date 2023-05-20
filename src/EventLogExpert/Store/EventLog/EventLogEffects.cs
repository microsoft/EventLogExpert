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
    private readonly IEventResolver _eventResolver;
    private readonly ITraceLogger _debugLogger;

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
            EventBookmark lastEventBookmark = null!;

            while (reader.ReadEvent() is { } e)
            {
                lastEventBookmark = e.Bookmark;
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

            EventLogWatcher watcher = null!;

            if (action.LogSpecifier.LogType == EventLogState.LogType.Live)
            {
                var query = new EventLogQuery(action.LogSpecifier.Name, PathType.LogName);

                if (lastEventBookmark != null)
                {
                    watcher = new EventLogWatcher(query, lastEventBookmark);
                }
                else
                {
                    watcher = new EventLogWatcher(query);
                }

                watcher.EventRecordWritten += (watcher, eventArgs) =>
                {
                    _debugLogger.Trace("EventRecordWritten was called.");
                    var resolved = _eventResolver.Resolve(eventArgs.EventRecord);
                    dispatcher.Dispatch(new EventLogAction.AddEvent(resolved));
                };

                watcher.Enabled = true;

                _debugLogger.Trace("EventLogWatcher enabled.");
            }

            dispatcher.Dispatch(new EventLogAction.LoadEvents(events,
                watcher,
                eventIdsAll.ToImmutableList(),
                eventProviderNamesAll.ToImmutableList(),
                eventTaskNamesAll.ToImmutableList()));
        });
    }
}
