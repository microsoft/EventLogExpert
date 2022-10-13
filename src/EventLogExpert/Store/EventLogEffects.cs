using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EventLogExpert.EventUtils;
using Fluxor;
using static EventLogExpert.Store.EventLogState;

namespace EventLogExpert.Store
{
    public class EventLogEffects
    {
        [EffectMethod]
        public static async Task HandleOpenLogAction(EventLogAction.OpenLog action, Fluxor.IDispatcher dispatcher)
        {
            dispatcher.Dispatch(new EventLogAction.ClearEvents());

            Func<Task<List<EventRecord>>> readEvents;
            if (action.logSpecifier.LogType == LogType.Live)
            {
                readEvents = EventReader.GetActiveEventLogReader("Application");
            }
            else
            {
                readEvents = EventReader.GetEventLogFileReader(action.logSpecifier.Name);
            }

            // Do this on a background thread so we don't hang the UI
            await Task.Run(async () =>
            {
                List<DisplayEvent> events = new List<DisplayEvent>();
                List<EventRecord> batch;
                while (null != (batch = await readEvents()))
                {
                    foreach (var e in batch)
                    {
                        events.Add(new DisplayEvent(
                        e.RecordId,
                        e.TimeCreated,
                        e.Id,
                        e.MachineName,
                        LevelNames[e.Level ?? 0],
                        e.ProviderName,
                        e.Task == 0 || e.Task == null ? "None" : TryGetValue(() => e.TaskDisplayName),
                        e.FormatDescription()));
                    }

                    dispatcher.Dispatch(new StatusBarAction.SetEventsLoaded(events.Count));
                }

                events.Reverse();
                dispatcher.Dispatch(new EventLogAction.LoadEvents(events));
            });
        }

        private static readonly Dictionary<byte, string> LevelNames = new Dictionary<byte, string>()
        {
            { 0, "Information" },
            { 2, "Error" },
            { 3, "Warning" },
            { 4, "Information" }
        };

        private static T TryGetValue<T>(Func<T> func)
        {
            try
            {
                var result = func();
                return result;
            }
            catch
            {
                return default(T);
            }
        }
    }
}
