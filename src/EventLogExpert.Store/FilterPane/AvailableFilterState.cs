// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Store.FilterPane;

[FeatureState]
public class AvailableFilterState
{
    public AvailableFilterState(IEnumerable<int> eventIdsAll,
        IEnumerable<string> eventProviderNamesAll,
        IEnumerable<string> taskNamesAll)
    {
        EventIdsAll = eventIdsAll;
        EventProviderNamesAll = eventProviderNamesAll;
        TaskNamesAll = taskNamesAll;
    }

    public AvailableFilterState() { }

    // Temporarily disabling this until filtering is back in place
    //public ImmutableList<string> RecentFilters { get; } = ImmutableList<string>.Empty;

    public IEnumerable<int> EventIdsAll { get; } = ImmutableList<int>.Empty;

    public IEnumerable<string> EventProviderNamesAll { get; } = ImmutableList<string>.Empty;

    public IEnumerable<string> TaskNamesAll { get; } = ImmutableList<string>.Empty;
}
