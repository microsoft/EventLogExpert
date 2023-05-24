// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using Fluxor;

namespace EventLogExpert.Store.FilterPane;

[FeatureState]
public record FilterPaneState
{
    public IEnumerable<FilterModel> CurrentFilters { get; init; } = Enumerable.Empty<FilterModel>();

    public IEnumerable<FilterModel> AppliedFilters { get; init; } = Enumerable.Empty<FilterModel>();

    public FilterDateModel? FilteredDateRange { get; init; } = null;

    public string AdvancedFilter { get; init; } = string.Empty;
}
