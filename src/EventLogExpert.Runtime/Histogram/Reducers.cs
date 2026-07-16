// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.Runtime.Histogram;

internal sealed class Reducers
{
    [ReducerMethod]
    public static HistogramState ReduceSetHistogramVisible(HistogramState state, SetHistogramVisibleAction action) =>
        new() { IsVisible = action.IsVisible };
}
