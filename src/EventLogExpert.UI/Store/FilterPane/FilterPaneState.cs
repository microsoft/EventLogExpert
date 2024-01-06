// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.FilterPane;

[FeatureState]
public sealed record FilterPaneState
{
    public ImmutableList<FilterModel> AdvancedFilters { get; init; } = [];

    public ImmutableList<FilterModel> BasicFilters { get; init; } = [];

    public ImmutableList<FilterModel> CachedFilters { get; init; } = [];

    public FilterDateModel? FilteredDateRange { get; init; }

    public bool IsEnabled { get; init; } = true;

    public bool IsLoading { get; init; }
}
