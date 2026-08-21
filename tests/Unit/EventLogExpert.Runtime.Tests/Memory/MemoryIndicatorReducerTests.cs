// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Memory;

namespace EventLogExpert.Runtime.Tests.Memory;

public sealed class MemoryIndicatorReducerTests
{
    [Fact]
    public void ReduceRecomputed_ReplacesTheProjectedValues()
    {
        var initial = new MemoryIndicatorState { UsedMebibytes = 1, WorkingSetBytes = 2, Level = MemoryUsageLevel.Normal };

        var next = MemoryIndicatorReducer.ReduceRecomputed(
            initial,
            new MemoryIndicatorRecomputedAction(512, MemoryUsageLevel.High, 900_000_000));

        Assert.Equal(512, next.UsedMebibytes);
        Assert.Equal(MemoryUsageLevel.High, next.Level);
        Assert.Equal(900_000_000, next.WorkingSetBytes);
    }
}
