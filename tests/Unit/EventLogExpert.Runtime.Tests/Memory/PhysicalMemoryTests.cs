// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Memory;

namespace EventLogExpert.Runtime.Tests.Memory;

public sealed class PhysicalMemoryTests
{
    [Fact]
    public void AvailableBytesFrom_LoadExceedsTotal_ReturnsZero() =>
        Assert.Equal(0L, PhysicalMemory.AvailableBytesFrom(46_000_000_000, 50_000_000_000));

    [Fact]
    public void AvailableBytesFrom_SaturatedMachine_ReturnsZeroNotTotal() =>
        Assert.Equal(0L, PhysicalMemory.AvailableBytesFrom(46_000_000_000, 46_000_000_000));

    [Fact]
    public void AvailableBytesFrom_UnpopulatedLoadSample_ReturnsZeroNotTotal() =>
        Assert.Equal(0L, PhysicalMemory.AvailableBytesFrom(46_000_000_000, 0));

    [Fact]
    public void AvailableBytesFrom_ValidLoad_ReturnsFreePhysical() =>
        Assert.Equal(17_000_000_000, PhysicalMemory.AvailableBytesFrom(46_000_000_000, 29_000_000_000));
}
