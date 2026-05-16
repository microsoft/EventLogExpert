// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.UI.FilterPane;

internal sealed class FilterPaneCommands(IDispatcher dispatcher) : IFilterPaneCommands
{
    private readonly IDispatcher _dispatcher = dispatcher;

    public void ToggleFilterDate() => _dispatcher.Dispatch(new ToggleFilterDateAction());

    public void ToggleFilteringEnabled() => _dispatcher.Dispatch(new ToggleIsEnabledAction());
}
