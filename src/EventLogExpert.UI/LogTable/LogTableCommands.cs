// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.UI.LogTable;

internal sealed class LogTableCommands(IDispatcher dispatcher) : ILogTableCommands
{
    private readonly IDispatcher _dispatcher = dispatcher;

    public void LoadColumns() => _dispatcher.Dispatch(new LoadColumnsAction());

    public void ResetColumnDefaults() => _dispatcher.Dispatch(new ResetColumnDefaultsAction());

    public void ToggleSortDirection() => _dispatcher.Dispatch(new ToggleSortingAction());
}
