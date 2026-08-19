// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.Runtime.Stats;

internal sealed class StatsCommands(IDispatcher dispatcher) : IStatsCommands
{
    private readonly IDispatcher _dispatcher = dispatcher;

    public void SetVisible(bool visible) => _dispatcher.Dispatch(new SetStatsVisibleAction(visible));
}
