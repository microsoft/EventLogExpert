using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EventLogExpert.EventUtils;
using Fluxor;

namespace EventLogExpert.Store
{
    /// <summary>
    /// NOTE: Because Virtualize requires an ICollection<T>, we have to use
    /// some sort of mutable collection for EventsToDisplay, unfortunately.
    /// If that ever changes we should consider making these immutable.
    /// </summary>
    [FeatureState]
    public class EventLogState
    {
        public enum LogType
        {
            Live,
            File
        }

        public record LogSpecifier(string Name, LogType? LogType);

        public LogSpecifier ActiveLog { get; }

        public ICollection<DisplayEvent> Events { get; }

        public ImmutableList<Func<DisplayEvent, bool>> Filter { get; }

        public ICollection<DisplayEvent> EventsToDisplay { get; }

        public EventLogState(
            LogSpecifier activeLog,
            ICollection<DisplayEvent> events,
            IList<Func<DisplayEvent, bool>> filter,
            ICollection<DisplayEvent> eventsToDisplay)
        {
            ActiveLog = activeLog;
            Events = events;
            Filter = filter.ToImmutableList();
            EventsToDisplay = eventsToDisplay;
        }

        private EventLogState()
        {
            ActiveLog = null;
            Events = new List<DisplayEvent>();
            Filter = ImmutableList.Create<Func<DisplayEvent, bool>>();
            EventsToDisplay = new List<DisplayEvent>();
        }
    }
}
