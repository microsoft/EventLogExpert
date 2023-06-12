// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.Store.EventLog;
using Fluxor;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Store.FilterPane;

public class FilterPaneEffects
{
    private readonly IState<FilterPaneState> _state;

    public FilterPaneEffects(IState<FilterPaneState> state) => _state = state;
}
