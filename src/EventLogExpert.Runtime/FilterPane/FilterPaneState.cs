// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterPane;

[FeatureState]
public sealed record FilterPaneState
{
    public ImmutableList<SavedFilter> Filters { get; init; } = [];

    public DateFilter? FilteredDateRange { get; init; }

    public bool IsEnabled { get; init; } = true;

    public bool IsFilteringEnabled =>
        FilteredDateRange?.IsEnabled is true ||
        (IsEnabled ? Filters.Any(filter => filter.IsEnabled) : Filters.Any(filter => filter.IsExcluded));
}
