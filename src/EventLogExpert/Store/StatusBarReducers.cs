using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Fluxor;

namespace EventLogExpert.Store
{
    public class StatusBarReducers
    {
        [ReducerMethod]
        public static StatusBarState ReduceSetEventsLoaded(StatusBarState state, StatusBarAction.SetEventsLoaded action) =>
            new(action.eventCount);
    }
}
