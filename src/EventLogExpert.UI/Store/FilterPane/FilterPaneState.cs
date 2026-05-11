// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Filter;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.FilterPane;

[FeatureState]
public sealed record FilterPaneState
{
    public ImmutableList<SavedFilter> Filters { get; init; } = [];

    public DateFilter? FilteredDateRange { get; init; }

    public bool IsEnabled { get; init; } = true;

    public bool IsLoading { get; init; }
}
