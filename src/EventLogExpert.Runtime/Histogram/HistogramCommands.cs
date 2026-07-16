// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.Runtime.Histogram;

internal sealed class HistogramCommands(IDispatcher dispatcher) : IHistogramCommands
{
    private readonly IDispatcher _dispatcher = dispatcher;

    public void SetVisible(bool visible) => _dispatcher.Dispatch(new SetHistogramVisibleAction(visible));
}
