using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventLogExpert.Store
{
    public record StatusBarAction
    {
        public record SetEventsLoaded(int eventCount);
    }
}
