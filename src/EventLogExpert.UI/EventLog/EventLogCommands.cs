// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.UI.EventLog;

internal sealed class EventLogCommands(IDispatcher dispatcher) : IEventLogCommands
{
    private readonly IDispatcher _dispatcher = dispatcher;

    public void LoadNewEvents() => _dispatcher.Dispatch(new LoadNewEventsAction());
}
