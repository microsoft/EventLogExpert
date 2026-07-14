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

    public bool IsFilteringEnabled
    {
        get
        {
            if (FilteredDateRange?.IsEnabled is true) { return true; }

            // foreach over the typed ImmutableList uses its struct enumerator; Filters.Any(predicate) would box the
            // enumerator via IEnumerable, allocating on this frequently-evaluated render / state-selection path.
            foreach (var filter in Filters)
            {
                if (IsEnabled ? filter.IsEnabled : filter.IsExcluded) { return true; }
            }

            return false;
        }
    }
}
