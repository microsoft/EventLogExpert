using System.Collections.Immutable;
using Fluxor;

namespace EventLogExpert.Store.State;

[FeatureState]
public class FilterPaneState
{
    public FilterPaneState(IEnumerable<string> recentFilters) => RecentFilters = recentFilters.ToImmutableList();

    public FilterPaneState() => RecentFilters = ImmutableList.Create<string>();

    public ImmutableList<string> RecentFilters { get; }
}