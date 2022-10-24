// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Store.FilterPane;

[FeatureState]
public class FilterPaneState
{
    public FilterPaneState(
        IEnumerable<string> recentFilters,
        IReadOnlyList<int> eventIdsAll,
        IReadOnlyList<string> eventProviderNamesAll,
        IReadOnlyList<string> taskNamesAll
    )
    {
        RecentFilters = recentFilters.ToImmutableList();
        EventIdsAll = eventIdsAll.ToImmutableList();
        EventProviderNamesAll = eventProviderNamesAll.ToImmutableList();
        TaskNamesAll = taskNamesAll.ToImmutableList();
    }

    public FilterPaneState() { }

    public ImmutableList<string> RecentFilters { get; } = ImmutableList<string>.Empty;

    public IReadOnlyList<int> EventIdsAll { get; } = ImmutableList<int>.Empty;

    public IReadOnlyList<string> EventProviderNamesAll { get; } = ImmutableList<string>.Empty;

    public IReadOnlyList<string> TaskNamesAll { get; } = ImmutableList<string>.Empty;
}
