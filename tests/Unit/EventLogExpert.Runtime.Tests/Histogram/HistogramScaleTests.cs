// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Histogram;

namespace EventLogExpert.Runtime.Tests.Histogram;

public sealed class HistogramScaleTests
{
    [Fact]
    public void BarHeight_EmptyBucket_IsZero()
    {
        Assert.Equal(0, HistogramScale.BarHeight(0, 100, 50));
    }

    [Fact]
    public void BarHeight_NonEmptyBucketBelowThePixelFloor_IsRaisedToOnePixel()
    {
        // A single event against a huge peak sqrt-scales below 1px; the floor keeps the bucket visible.
        double height = HistogramScale.BarHeight(1, 1_000_000, 50);

        Assert.True(height >= 1);
    }

    [Fact]
    public void BarHeight_PeakBucket_FillsThePlot()
    {
        Assert.Equal(50, HistogramScale.BarHeight(100, 100, 50));
    }

    [Fact]
    public void BarHeight_UsesSquareRootScaling()
    {
        // A quarter-count bucket reads at half height under sqrt (linear would read a quarter).
        Assert.Equal(50, HistogramScale.BarHeight(25, 100, 100), precision: 6);
    }

    [Fact]
    public void BarHeight_ZeroMaxCountOrHeight_IsZero()
    {
        Assert.Equal(0, HistogramScale.BarHeight(5, 0, 50));
        Assert.Equal(0, HistogramScale.BarHeight(5, 100, 0));
    }

    [Fact]
    public void StackedGroupHeights_DistributesLeftoverPixelsByLargestRemainder()
    {
        // Three equal counts over a 4px bar: the 1px floor gives each group 1px (3px), and the single leftover pixel is handed
        // out by largest remainder, so the segments still sum to the bar height with every present group visible.
        int[] heights = HistogramScale.StackedGroupHeights([1, 1, 1], 3, 4);

        Assert.Equal(4, heights[0] + heights[1] + heights[2]);
        Assert.True(heights[0] >= 1);
        Assert.True(heights[1] >= 1);
        Assert.True(heights[2] >= 1);
    }

    [Fact]
    public void StackedGroupHeights_EmptyBin_IsZero()
    {
        Assert.Equal([0, 0, 0], HistogramScale.StackedGroupHeights([0, 0, 0], 100, 50));
    }

    [Fact]
    public void StackedGroupHeights_FloorRepairPreservesTheSum()
    {
        // A rare group against a huge baseline: the proportional split rounds it to 0, the floor repair lifts it to 1 by
        // stealing from the tallest group, and the total is unchanged.
        int[] heights = HistogramScale.StackedGroupHeights([1000, 0, 1], 1001, 100);
        int barTotal = (int)Math.Round(HistogramScale.BarHeight(1001, 1001, 100));

        Assert.Equal(barTotal, heights[0] + heights[1] + heights[2]);
        Assert.Equal(1, heights[2]);
        Assert.Equal(0, heights[1]);
        Assert.Equal(barTotal - 1, heights[0]);
    }

    [Fact]
    public void StackedGroupHeights_LeftoverPixelGoesToTheHighestRemainder()
    {
        // Counts (5,3,2) over a 7px bar: proportional shares are 3.5/2.1/1.4 -> floors 3/2/1 (6px), and the single leftover
        // pixel goes to group 0 (the largest fractional remainder 0.5), yielding (4,2,1) - a non-tie case that pins selection.
        int[] heights = HistogramScale.StackedGroupHeights([5, 3, 2], 10, 7);

        Assert.Equal(4, heights[0]);
        Assert.Equal(2, heights[1]);
        Assert.Equal(1, heights[2]);
    }

    [Fact]
    public void StackedGroupHeights_RareGroupAmongManyInAnother_StaysAtLeastOnePixel()
    {
        int[] heights = HistogramScale.StackedGroupHeights([1000, 0, 1], maxBinTotal: 1001, plotHeightPx: 100);

        Assert.True(heights[2] >= 1);
        Assert.Equal(0, heights[1]);
        Assert.True(heights[0] > heights[2]);
    }

    [Fact]
    public void StackedGroupHeights_SegmentsSumToTheScaledBarHeight()
    {
        int[] heights = HistogramScale.StackedGroupHeights([30, 15, 5], 50, 80);

        int expected = (int)Math.Round(HistogramScale.BarHeight(50, 50, 80));

        Assert.Equal(expected, heights[0] + heights[1] + heights[2]);
        Assert.True(heights[0] > 0);
        Assert.True(heights[1] > 0);
        Assert.True(heights[2] > 0);
    }

    [Fact]
    public void StackedGroupHeights_SingleGroup_TakesTheWholeBar()
    {
        int[] heights = HistogramScale.StackedGroupHeights([10, 0, 0], 10, 50);

        Assert.Equal(50, heights[0]);
        Assert.Equal(0, heights[1]);
        Assert.Equal(0, heights[2]);
    }
}
