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
    [FeatureState]
    public class FilterPaneState
    {
        public ImmutableList<string> RecentFilters { get; }

        public FilterPaneState(IList<string> recentFilters)
        {
            RecentFilters = recentFilters.ToImmutableList();
        }

        public FilterPaneState()
        {
            RecentFilters = ImmutableList.Create<string>();
        }
    }
}
