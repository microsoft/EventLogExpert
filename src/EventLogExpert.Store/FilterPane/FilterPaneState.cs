// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using Fluxor;

namespace EventLogExpert.Store.FilterPane;

[FeatureState]
public class FilterPaneState
{
    public FilterPaneState(IEnumerable<FilterModel> currentFilters)
    {
        CurrentFilters = currentFilters;
    }

    public FilterPaneState() { }

    public IEnumerable<FilterModel> CurrentFilters { get; } = Enumerable.Empty<FilterModel>();
}
