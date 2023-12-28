// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.FilterPane;

[FeatureState]
public record FilterPaneState
{
    public IImmutableList<FilterModel> CurrentFilters { get; init; } = ImmutableList<FilterModel>.Empty;

    public IImmutableList<AdvancedFilterModel> CachedFilters { get; init; } = ImmutableList<AdvancedFilterModel>.Empty;

    public FilterDateModel? FilteredDateRange { get; init; } = null;

    public AdvancedFilterModel? AdvancedFilter { get; init; } = null;

    public bool IsLoading { get; init; }
}
