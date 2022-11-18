// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Store.FilterPane;

[FeatureState]
public record AvailableFilterState
{
    // Temporarily disabling this until filtering is back in place
    //public ImmutableList<string> RecentFilters { get; } = ImmutableList<string>.Empty;

    public IEnumerable<int> EventIdsAll { get; init; } = ImmutableList<int>.Empty;

    public IEnumerable<string> EventProviderNamesAll { get; init; } = ImmutableList<string>.Empty;

    public IEnumerable<string> TaskNamesAll { get; init; } = ImmutableList<string>.Empty;
}
