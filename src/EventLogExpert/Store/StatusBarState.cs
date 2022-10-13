using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Fluxor;

namespace EventLogExpert.Store
{
    [FeatureState(MaximumStateChangedNotificationsPerSecond = 1)]
    public class StatusBarState
    {
        public int EventsLoaded { get; }

        public StatusBarState(int eventsLoaded)
        {
            EventsLoaded = eventsLoaded;
        }

        public StatusBarState()
        {
            EventsLoaded = 0;
        }
    }
}
