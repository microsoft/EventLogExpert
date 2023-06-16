// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.UI.Store.FilterPane;

public class FilterPaneEffects
{
    private readonly IState<FilterPaneState> _state;

    public FilterPaneEffects(IState<FilterPaneState> state) => _state = state;
}
