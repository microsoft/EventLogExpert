// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.FilterPane;

[FeatureState]
public record FilterPaneState
{
    public IImmutableList<FilterModel> CurrentFilters { get; init; } = ImmutableList.Create<FilterModel>();

    public FilterDateModel? FilteredDateRange { get; init; } = null;

    public string AdvancedFilter { get; init; } = string.Empty;

    public bool IsAdvancedFilterEnabled { get; set; } = true;
}
