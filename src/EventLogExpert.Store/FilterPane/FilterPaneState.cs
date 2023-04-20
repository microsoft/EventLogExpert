// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using Fluxor;

namespace EventLogExpert.Store.FilterPane;

[FeatureState]
public record FilterPaneState
{
    public IEnumerable<FilterModel> CurrentFilters { get; init; } = Enumerable.Empty<FilterModel>();
}
