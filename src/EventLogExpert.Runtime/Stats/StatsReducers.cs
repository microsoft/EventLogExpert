// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.Runtime.Stats;

internal sealed class StatsReducers
{
    [ReducerMethod]
    public static StatsState ReduceSetStatsVisible(StatsState state, SetStatsVisibleAction action) =>
        state.IsVisible == action.IsVisible ? state : new StatsState
        {
            IsVisible = action.IsVisible
        };
}
