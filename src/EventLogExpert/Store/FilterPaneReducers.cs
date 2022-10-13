using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Fluxor;

namespace EventLogExpert.Store
{
    public class FilterPaneReducers
    {
        [ReducerMethod]
        public static FilterPaneState ReduceAddRecentFilter(FilterPaneState state, FilterPaneAction.AddRecentFilter action) =>
            new FilterPaneState(state.RecentFilters.Prepend(action.filterText).Take(10).ToImmutableList());
    }
}
