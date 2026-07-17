// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.LogTable.Histogram;

namespace EventLogExpert.UI.Tests.LogTable.Histogram;

public sealed class HistogramTrackCapTests
{
    [Fact]
    public void MinBinsForWidth_HugeWidth_DoesNotOverflowAndClampsToTotalBins()
    {
        int result = HistogramTrackCap.MinBinsForWidth(int.MaxValue, 20000);

        Assert.Equal(20000, result);
    }

    [Theory]
    [InlineData(6000, 20000, 4)]  // exactly at the 30M cap: 4 bins => track == 30M, so no floor beyond the raw minimum
    [InlineData(6001, 20000, 5)]  // one px over: ceil raises the floor to 5 so the track stays under the cap
    [InlineData(1920, 20000, 2)]  // ceil(1.28)
    [InlineData(3840, 20000, 3)]  // ceil(2.56)
    [InlineData(0, 20000, 1)]     // no measured viewport yet
    [InlineData(-10, 20000, 1)]   // bogus width guarded
    [InlineData(1920, 0, 1)]      // no bins
    [InlineData(1920, 3, 1)]      // tiny domain: raw floor below 1 clamps up to 1
    public void MinBinsForWidth_ReturnsCapFloor(int viewportWidthPx, int totalBins, int expected) =>
        Assert.Equal(expected, HistogramTrackCap.MinBinsForWidth(viewportWidthPx, totalBins));
}
