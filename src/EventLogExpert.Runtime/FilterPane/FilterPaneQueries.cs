// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.Runtime.FilterPane;

internal sealed class FilterPaneQueries(IState<FilterPaneState> filterPaneState) : IFilterPaneQueries
{
    private readonly IState<FilterPaneState> _filterPaneState = filterPaneState;

    public bool IsEnabled() => _filterPaneState.Value.IsEnabled;
}
