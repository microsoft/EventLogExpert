// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using Fluxor;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace EventLogExpert.Store.FilterPane;

[FeatureState]
public record FilterPaneState
{
    public IImmutableList<FilterModel> CurrentFilters { get; init; } = ImmutableList.Create<FilterModel>();

    public FilterDateModel? FilteredDateRange { get; init; } = null;

    public string AdvancedFilter { get; init; } = string.Empty;

    public bool IsAdvancedFilterEnabled { get; set; } = true;
}
