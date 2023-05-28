// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using Fluxor;
using System.Collections.ObjectModel;

namespace EventLogExpert.Store.FilterPane;

[FeatureState]
public record FilterPaneState
{
    public ReadOnlyCollection<FilterModel> CurrentFilters { get; init; } = new List<FilterModel>().AsReadOnly();

    public FilterDateModel? FilteredDateRange { get; init; } = null;

    public string AdvancedFilter { get; init; } = string.Empty;

    public bool IsAdvancedFilterEnabled { get; set; } = true;
}
