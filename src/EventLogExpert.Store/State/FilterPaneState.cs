using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Store.State;

[FeatureState]
public class FilterPaneState
{
    public FilterPaneState(
        IEnumerable<string> recentFilters,
        IReadOnlyList<int> eventIdsAll,
        IReadOnlyList<int> eventIdsSelected,
        IReadOnlyList<string> eventProviderNamesAll,
        IReadOnlyList<string> eventProviderNamesSelected,
        IReadOnlyList<string> taskNamesAll,
        IReadOnlyList<string> taskNamesSelected)
    {
        RecentFilters = recentFilters.ToImmutableList();
        EventIdsAll = eventIdsAll.ToImmutableList();
        EventIdsSelected = eventIdsSelected.ToImmutableList();
        EventProviderNamesAll = eventProviderNamesAll.ToImmutableList();
        EventProviderNamesSelected = eventProviderNamesSelected.ToImmutableList();
        TaskNamesAll = taskNamesAll.ToImmutableList();
        TaskNamesSelected = taskNamesSelected.ToImmutableList();
    }

    public FilterPaneState() { }

    public ImmutableList<string> RecentFilters { get; } = ImmutableList<string>.Empty;

    public IReadOnlyList<int> EventIdsAll { get; } = ImmutableList<int>.Empty;

    public IReadOnlyList<int> EventIdsSelected { get; } = ImmutableList<int>.Empty;

    public IReadOnlyList<string> EventProviderNamesAll { get; } = ImmutableList<string>.Empty;

    public IReadOnlyList<string> EventProviderNamesSelected { get; } = ImmutableList<string>.Empty;

    public IReadOnlyList<string> TaskNamesAll { get; } = ImmutableList<string>.Empty;

    public IReadOnlyList<string> TaskNamesSelected { get; } = ImmutableList<string>.Empty;
}
