using EventLogExpert.Library.EventResolvers;
using EventLogExpert.Library.Models;
using EventLogExpert.Store.Actions;
using Fluxor;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using static EventLogExpert.Store.State.EventLogState;

namespace EventLogExpert.Store.Effects;

public class EventLogEffects
{
    private readonly IEventResolver _eventResolver;

    public EventLogEffects(IEventResolver eventResolver)
    {
        _eventResolver = eventResolver;
    }

    private static readonly Dictionary<byte, string> LevelNames = new()
    {
        { 0, "Information" },
        { 2, "Error" },
        { 3, "Warning" },
        { 4, "Information" }
    };

    [EffectMethod]
    public async Task HandleOpenLogAction(EventLogAction.OpenLog action, IDispatcher dispatcher)
    {
        dispatcher.Dispatch(new EventLogAction.ClearEvents());

        EventLogReader reader;
        if (action.LogSpecifier.LogType == LogType.Live)
        {
            reader = new EventLogReader("Application", PathType.LogName);
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

            while (reader.ReadEvent() is { } e)
            {
                var resolved = _eventResolver.Resolve(e);
                eventIdsAll.Add(resolved.Id);
                eventProviderNamesAll.Add(resolved.ProviderName);
                eventTaskNamesAll.Add(resolved.TaskDisplayName);

                events.Add(resolved);

                if (sw.ElapsedMilliseconds > 1000)
                {
                    sw.Restart();
                    dispatcher.Dispatch(new StatusBarAction.SetEventsLoaded(events.Count));
                }
            }

            events.Reverse();
            dispatcher.Dispatch(new EventLogAction.LoadEvents(events, eventIdsAll.ToImmutableList(), eventProviderNamesAll.ToImmutableList(), eventTaskNamesAll.ToImmutableList()));
        });
    }
}
