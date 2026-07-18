// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.Runtime.Histogram;

internal sealed class Reducers
{
    [ReducerMethod]
    public static HistogramState ReduceClearHistogramDimensionRequest(
        HistogramState state,
        ClearHistogramDimensionRequestAction action) =>
        state.DimensionRequest is null ? state : state with { DimensionRequest = null };

    [ReducerMethod]
    public static HistogramState ReduceRequestHistogramDimension(HistogramState state, RequestHistogramDimensionAction action)
    {
        var token = state.NextDimensionToken + 1;

        return state with
        {
            DimensionRequest = new HistogramDimensionRequest(action.Dimension, token),
            NextDimensionToken = token
        };
    }

    [ReducerMethod]
    public static HistogramState ReduceSetHistogramVisible(HistogramState state, SetHistogramVisibleAction action) =>
        state with { IsVisible = action.IsVisible, DimensionRequest = action.IsVisible ? state.DimensionRequest : null };
}
