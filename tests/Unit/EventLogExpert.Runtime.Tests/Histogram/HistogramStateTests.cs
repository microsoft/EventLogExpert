// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Histogram;

namespace EventLogExpert.Runtime.Tests.Histogram;

public sealed class HistogramStateTests
{
    [Fact]
    public void IsVisible_DefaultsToHidden()
    {
        // The timeline is off on every launch - there is no persisted preference; a scenario or the View menu reveals it.
        Assert.False(new HistogramState().IsVisible);
    }

    [Fact]
    public void ReduceClearHistogramDimensionRequest_WhenRequestExists_ClearsRequest()
    {
        HistogramState state = new()
        {
            DimensionRequest = new HistogramDimensionRequest(HistogramDimension.EventId, 1),
            NextDimensionToken = 1
        };

        var reduced = Reducers.ReduceClearHistogramDimensionRequest(state, new ClearHistogramDimensionRequestAction());

        Assert.Null(reduced.DimensionRequest);
        Assert.Equal(1, reduced.NextDimensionToken);
    }

    [Fact]
    public void ReduceRequestHistogramDimension_AfterClear_UsesNewToken()
    {
        var first = Reducers.ReduceRequestHistogramDimension(
            new HistogramState(),
            new RequestHistogramDimensionAction(HistogramDimension.EventId));
        var cleared = Reducers.ReduceClearHistogramDimensionRequest(first, new ClearHistogramDimensionRequestAction());

        var second = Reducers.ReduceRequestHistogramDimension(
            cleared,
            new RequestHistogramDimensionAction(HistogramDimension.Source));

        Assert.Equal(2, second.NextDimensionToken);
        Assert.Equal(new HistogramDimensionRequest(HistogramDimension.Source, 2), second.DimensionRequest);
    }

    [Fact]
    public void ReduceRequestHistogramDimension_IncrementsNextDimensionToken()
    {
        HistogramState state = new() { NextDimensionToken = 7 };

        var reduced = Reducers.ReduceRequestHistogramDimension(
            state,
            new RequestHistogramDimensionAction(HistogramDimension.EventId));

        Assert.Equal(8, reduced.NextDimensionToken);
        Assert.Equal(new HistogramDimensionRequest(HistogramDimension.EventId, 8), reduced.DimensionRequest);
    }

    [Fact]
    public void ReduceSetHistogramVisible_WhenHidden_ClearsRequest()
    {
        HistogramState state = new()
        {
            DimensionRequest = new HistogramDimensionRequest(HistogramDimension.EventId, 1),
            IsVisible = true,
            NextDimensionToken = 1
        };

        var reduced = Reducers.ReduceSetHistogramVisible(state, new SetHistogramVisibleAction(false));

        Assert.False(reduced.IsVisible);
        Assert.Null(reduced.DimensionRequest);
    }

    [Fact]
    public void ReduceSetHistogramVisible_WhenShown_PreservesRequest()
    {
        HistogramState state = new()
        {
            DimensionRequest = new HistogramDimensionRequest(HistogramDimension.EventId, 1),
            NextDimensionToken = 1
        };

        var reduced = Reducers.ReduceSetHistogramVisible(state, new SetHistogramVisibleAction(true));

        Assert.True(reduced.IsVisible);
        Assert.Equal(state.DimensionRequest, reduced.DimensionRequest);
    }
}
