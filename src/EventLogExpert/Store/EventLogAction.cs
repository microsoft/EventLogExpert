using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EventLogExpert.EventUtils;

namespace EventLogExpert.Store
{
    public record EventLogAction
    {
        public record OpenLog(EventLogState.LogSpecifier logSpecifier) : EventLogAction;

        public record ClearEvents() : EventLogAction;

        public record LoadEvents(ICollection<DisplayEvent> events) : EventLogAction;

        public record FilterEvents(IList<Func<DisplayEvent, bool>> filter) : EventLogAction;
    }
}
