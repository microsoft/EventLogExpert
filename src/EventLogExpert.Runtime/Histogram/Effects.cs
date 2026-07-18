// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.EventLog;
using Fluxor;

namespace EventLogExpert.Runtime.Histogram;

internal sealed class Effects(IState<EventLogState> eventLogState, IState<HistogramState> histogramState)
{
    private readonly IState<EventLogState> _eventLogState = eventLogState;
    private readonly IState<HistogramState> _histogramState = histogramState;

    [EffectMethod]
    public Task HandleCloseAllLogs(CloseAllLogsAction action, IDispatcher dispatcher) => ClearIfNoLogs(dispatcher);

    [EffectMethod]
    public Task HandleCloseLog(CloseLogAction action, IDispatcher dispatcher) => ClearIfNoLogs(dispatcher);

    private Task ClearIfNoLogs(IDispatcher dispatcher)
    {
        if (_eventLogState.Value.OpenLogs.IsEmpty && _histogramState.Value.DimensionRequest is not null)
        {
            dispatcher.Dispatch(new ClearHistogramDimensionRequestAction());
        }

        return Task.CompletedTask;
    }
}
