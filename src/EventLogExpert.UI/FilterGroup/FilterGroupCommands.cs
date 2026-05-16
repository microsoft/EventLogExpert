// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.UI.FilterGroup;

internal sealed class FilterGroupCommands(IDispatcher dispatcher) : IFilterGroupCommands
{
    private readonly IDispatcher _dispatcher = dispatcher;

    public void LoadGroups() => _dispatcher.Dispatch(new LoadGroupsAction());
}
