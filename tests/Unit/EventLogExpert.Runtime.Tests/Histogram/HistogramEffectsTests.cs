// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.Histogram;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.Histogram;

public sealed class HistogramEffectsTests
{
    [Fact]
    public async Task HandleCloseAllLogs_WhenNoLogsAndRequestExists_ClearsRequest()
    {
        var (effects, dispatcher) = CreateEffects(
            new EventLogState(),
            new HistogramState { DimensionRequest = new HistogramDimensionRequest(HistogramDimension.EventId, 1) });

        await effects.HandleCloseAllLogs(new CloseAllLogsAction(), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Any<ClearHistogramDimensionRequestAction>());
    }

    [Fact]
    public async Task HandleCloseAllLogs_WhenNoRequest_DoesNotClearRequest()
    {
        var (effects, dispatcher) = CreateEffects(new EventLogState(), new HistogramState());

        await effects.HandleCloseAllLogs(new CloseAllLogsAction(), dispatcher);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<ClearHistogramDimensionRequestAction>());
    }

    [Fact]
    public async Task HandleCloseLog_WhenLogsRemain_DoesNotClearRequest()
    {
        EventLogId remainingLogId = EventLogId.Create();
        EventLogState eventLogState = new()
        {
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty.Add(
                "Application",
                new OpenLogInfo(remainingLogId, LogPathType.Channel))
        };
        var (effects, dispatcher) = CreateEffects(
            eventLogState,
            new HistogramState { DimensionRequest = new HistogramDimensionRequest(HistogramDimension.EventId, 1) });

        await effects.HandleCloseLog(new CloseLogAction(EventLogId.Create(), "System"), dispatcher);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<ClearHistogramDimensionRequestAction>());
    }

    [Fact]
    public async Task HandleCloseLog_WhenNoLogsAndRequestExists_ClearsRequest()
    {
        var (effects, dispatcher) = CreateEffects(
            new EventLogState(),
            new HistogramState { DimensionRequest = new HistogramDimensionRequest(HistogramDimension.EventId, 1) });

        await effects.HandleCloseLog(new CloseLogAction(EventLogId.Create(), "System"), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Any<ClearHistogramDimensionRequestAction>());
    }

    private static (Effects Effects, IDispatcher Dispatcher) CreateEffects(
        EventLogState eventLogStateValue,
        HistogramState histogramStateValue)
    {
        var eventLogState = Substitute.For<IState<EventLogState>>();
        eventLogState.Value.Returns(eventLogStateValue);

        var histogramState = Substitute.For<IState<HistogramState>>();
        histogramState.Value.Returns(histogramStateValue);

        var dispatcher = Substitute.For<IDispatcher>();
        Effects effects = new(eventLogState, histogramState);

        return (effects, dispatcher);
    }
}
