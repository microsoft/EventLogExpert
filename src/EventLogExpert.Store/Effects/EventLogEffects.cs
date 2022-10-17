using System.Diagnostics.Eventing.Reader;
using EventLogExpert.Library.Models;
using EventLogExpert.Library.Readers;
using EventLogExpert.Store.Actions;
using Fluxor;
using static EventLogExpert.Store.State.EventLogState;

namespace EventLogExpert.Store.Effects;

public class EventLogEffects
{
    private static readonly Dictionary<byte, string> LevelNames = new()
    {
        { 0, "Information" },
        { 2, "Error" },
        { 3, "Warning" },
        { 4, "Information" }
    };

    [EffectMethod]
    public static async Task HandleOpenLogAction(EventLogAction.OpenLog action, IDispatcher dispatcher)
    {
        dispatcher.Dispatch(new EventLogAction.ClearEvents());

        Func<Task<List<EventRecord>>> readEvents = action.LogSpecifier.LogType == LogType.Live
            ? EventReader.GetActiveEventLogReader("Application")
            : EventReader.GetEventLogFileReader(action.LogSpecifier.Name);

        // Do this on a background thread so we don't hang the UI
        await Task.Run(async () =>
        {
            List<DisplayEventModel> events = new();

            while (await readEvents() is { } batch)
            {
                foreach (var e in batch)
                {
                    events.Add(new DisplayEventModel(
                        e.RecordId,
                        e.TimeCreated,
                        e.Id,
                        e.MachineName,
                        LevelNames[e.Level ?? 0],
                        e.ProviderName,
                        e.Task is 0 or null ? "None" : TryGetValue(() => e.TaskDisplayName),
                        e.FormatDescription()));
                }

                dispatcher.Dispatch(new StatusBarAction.SetEventsLoaded(events.Count));
            }

            events.Reverse();
            dispatcher.Dispatch(new EventLogAction.LoadEvents(events));
        });
    }

    private static T TryGetValue<T>(Func<T> func)
    {
        try
        {
            var result = func();
            return result;
        }
        catch
        {
            return default;
        }
    }
}