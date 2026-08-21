// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.Runtime.Memory;

internal sealed class MemoryIndicatorReducer
{
    [ReducerMethod]
    public static MemoryIndicatorState ReduceRecomputed(
        MemoryIndicatorState state,
        MemoryIndicatorRecomputedAction action) =>
        state with
        {
            UsedMebibytes = action.UsedMebibytes,
            Level = action.Level,
            WorkingSetBytes = action.WorkingSetBytes
        };
}
