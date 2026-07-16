// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.Histogram;

namespace EventLogExpert.Runtime.Tests.Histogram;

public sealed class HistogramAggregatorTests
{
    private const int Errors = 2;
    // Severity group indices in HistogramGroups.Severity (bottom to top): Other, Warnings, Errors.
    private const int Other = 0;
    private const int Warnings = 1;

    [Fact]
    public void Aggregate_ClampsAStaleWindowIntoTheDomain()
    {
        var data = ErrorPerBin(binCount: 4, bucketSpanTicks: 10);

        HistogramRender render = HistogramAggregator.Aggregate(data, -100, 1_000_000, targetBins: 10);

        Assert.Equal(data.MinUtc.Ticks, render.WindowStartTicks);
        Assert.Equal(data.MaxUtc.Ticks, render.WindowEndTicks);
        Assert.Equal(4, render.WindowTotal);
    }

    [Fact]
    public void Aggregate_DownSamplesBaseBinsToTargetBins()
    {
        var data = ErrorPerBin(binCount: 4, bucketSpanTicks: 10);

        HistogramRender render = HistogramAggregator.Aggregate(data, 0, 39, targetBins: 2);

        Assert.Equal(2, render.Bins.Count);
        Assert.Equal(2, render.Bins[0].GroupCounts[Errors]);
        Assert.Equal(2, render.Bins[1].GroupCounts[Errors]);
        Assert.Equal(4, render.WindowGroupTotals[Errors]);
    }

    [Fact]
    public void Aggregate_FewerThanTheMinimumSample_FlagNoAnomaly()
    {
        // Seven bins is below the 8-bin minimum, so even an obvious spike is not flagged (too small a sample to trust).
        var data = BinsWithTotals(10, 1, 1, 1, 1, 1, 100, 1);

        HistogramRender render = HistogramAggregator.Aggregate(data, data.MinUtc.Ticks, data.MaxUtc.Ticks, targetBins: 1000);

        Assert.DoesNotContain(render.Bins, bin => bin.IsAnomaly);
    }

    [Fact]
    public void Aggregate_FlagsAStatisticalSpikeBinAsAnomaly()
    {
        // Nine quiet bins of 2 and one spike of 100: only the spike exceeds mean + 2 stddev of the visible bins.
        var data = BinsWithTotals(10, 2, 2, 2, 2, 100, 2, 2, 2, 2, 2);

        HistogramRender render = HistogramAggregator.Aggregate(data, data.MinUtc.Ticks, data.MaxUtc.Ticks, targetBins: 1000);

        Assert.Single(render.Bins, bin => bin.IsAnomaly);
        Assert.True(render.Bins[4].IsAnomaly);
    }

    [Fact]
    public void Aggregate_GroupByCategories_SumsEachCategorySlotAcrossBaseBins()
    {
        // Two categories (slots 0, 1) plus Other (slot 2), 2 base bins. ForCategories orders groups Other, cat0, cat1.
        var groups = HistogramGroups.ForCategories(["a", "b"]);
        var slots = new int[2 * 3];
        slots[(0 * 3) + 0] = 3; // bin0 category a
        slots[(0 * 3) + 1] = 1; // bin0 category b
        slots[(1 * 3) + 0] = 2; // bin1 category a
        slots[(1 * 3) + 2] = 4; // bin1 Other

        var data = new HistogramData(slots, 3, 2, Utc(0), Utc(19), 10, 10, groups);

        HistogramRender render = HistogramAggregator.Aggregate(data, 0, 19, targetBins: 1);

        var bin = Assert.Single(render.Bins);
        Assert.Equal(4, bin.GroupCounts[0]); // Other
        Assert.Equal(5, bin.GroupCounts[1]); // category a
        Assert.Equal(1, bin.GroupCounts[2]); // category b
        Assert.Equal(10, bin.Total);
        Assert.Equal([4, 5, 1], render.WindowGroupTotals);
    }

    [Fact]
    public void Aggregate_GroupsSlotsIntoErrorWarningAndNormalBands()
    {
        int slotCount = LevelSeverity.SlotCount;
        var slots = new int[slotCount];
        slots[(int)SeverityLevel.Critical] = 2;
        slots[(int)SeverityLevel.Error] = 3;
        slots[(int)SeverityLevel.Warning] = 4;
        slots[(int)SeverityLevel.Information] = 5;
        slots[(int)SeverityLevel.Verbose] = 6;
        slots[0] = 1;

        var data = Severity(slots, 1, Utc(0), Utc(9), 21, 10);

        HistogramRender render = HistogramAggregator.Aggregate(data, 0, 9, targetBins: 10);

        var bin = Assert.Single(render.Bins);
        Assert.Equal(5, bin.GroupCounts[Errors]);
        Assert.Equal(4, bin.GroupCounts[Warnings]);
        Assert.Equal(12, bin.GroupCounts[Other]);
        Assert.Equal(21, bin.Total);
        Assert.Equal(21, render.WindowTotal);
        Assert.Equal(5, render.WindowGroupTotals[Errors]);
        Assert.Equal(4, render.WindowGroupTotals[Warnings]);
        Assert.Equal(21, render.MaxBinTotal);
    }

    [Fact]
    public void Aggregate_PartitionsNonDivisibleBinCountsWithoutGapOrDoubleCount()
    {
        var data = ErrorPerBin(binCount: 5, bucketSpanTicks: 10);

        HistogramRender render = HistogramAggregator.Aggregate(data, 0, 49, targetBins: 3);

        // 5 base bins over 3 render bins partition as [0], [1,2], [3,4]; every base bin is counted exactly once.
        Assert.Equal(3, render.Bins.Count);
        Assert.Equal(1, render.Bins[0].GroupCounts[Errors]);
        Assert.Equal(2, render.Bins[1].GroupCounts[Errors]);
        Assert.Equal(2, render.Bins[2].GroupCounts[Errors]);
        Assert.Equal(5, render.WindowGroupTotals[Errors]);
    }

    [Fact]
    public void Aggregate_RenderBinEndTicksAreInclusiveAndNonOverlapping()
    {
        // 4 base bins of 10 ticks over [0,39]; at high target-bins each render bar is one base bin. The inclusive end must be
        // one tick before the next bar starts, so scope/click ranges and find-hit binning never leak the boundary event.
        var data = ErrorPerBin(binCount: 4, bucketSpanTicks: 10);

        HistogramRender render = HistogramAggregator.Aggregate(data, 0, 39, targetBins: 1000);

        Assert.Equal(4, render.Bins.Count);
        Assert.Equal(0, render.Bins[0].StartTicks);
        Assert.Equal(9, render.Bins[0].EndTicks);
        Assert.Equal(39, render.Bins[3].EndTicks);

        for (int index = 0; index + 1 < render.Bins.Count; index++)
        {
            Assert.True(render.Bins[index].EndTicks >= render.Bins[index].StartTicks);
            Assert.Equal(render.Bins[index + 1].StartTicks - 1, render.Bins[index].EndTicks);
        }
    }

    [Fact]
    public void Aggregate_ReportsBinSnappedWindowBounds()
    {
        // 20 base bins of 10 ticks each: an arbitrary window [111,188] snaps out to the containing bins [110,120)..[180,190),
        // so the reported bounds are [110,189] - the same span the bars count, the axis labels, and Scope-to-range use.
        var data = ErrorPerBin(binCount: 20, bucketSpanTicks: 10);

        HistogramRender render = HistogramAggregator.Aggregate(data, 111, 188, targetBins: 1000);

        Assert.Equal(110, render.WindowStartTicks);
        Assert.Equal(189, render.WindowEndTicks);
    }

    [Fact]
    public void Aggregate_RestrictsToTheVisibleWindow()
    {
        var data = ErrorPerBin(binCount: 4, bucketSpanTicks: 10);

        HistogramRender render = HistogramAggregator.Aggregate(data, 10, 29, targetBins: 10);

        Assert.Equal(2, render.WindowGroupTotals[Errors]);
        Assert.Equal(10, render.WindowStartTicks);
        Assert.Equal(29, render.WindowEndTicks);
    }

    [Fact]
    public void Aggregate_ThrowsWhenTargetBinsIsNotPositive()
    {
        var data = ErrorPerBin(binCount: 2, bucketSpanTicks: 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => HistogramAggregator.Aggregate(data, 0, 19, targetBins: 0));
    }

    [Fact]
    public void Aggregate_TracksTheTallestRenderedBin()
    {
        int slotCount = LevelSeverity.SlotCount;
        var slots = new int[2 * slotCount];
        slots[(0 * slotCount) + (int)SeverityLevel.Error] = 1;
        slots[(1 * slotCount) + (int)SeverityLevel.Error] = 7;

        var data = Severity(slots, 2, Utc(0), Utc(19), 8, 10);

        HistogramRender render = HistogramAggregator.Aggregate(data, 0, 19, targetBins: 10);

        Assert.Equal(7, render.MaxBinTotal);
    }

    [Fact]
    public void Aggregate_UniformBins_FlagNoAnomaly()
    {
        var data = BinsWithTotals(10, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5);

        HistogramRender render = HistogramAggregator.Aggregate(data, data.MinUtc.Ticks, data.MaxUtc.Ticks, targetBins: 1000);

        Assert.DoesNotContain(render.Bins, bin => bin.IsAnomaly);
    }

    private static HistogramData BinsWithTotals(long bucketSpanTicks, params int[] totals)
    {
        int slotCount = LevelSeverity.SlotCount;
        var slots = new int[totals.Length * slotCount];
        int total = 0;

        for (int bin = 0; bin < totals.Length; bin++)
        {
            slots[(bin * slotCount) + (int)SeverityLevel.Information] = totals[bin];
            total += totals[bin];
        }

        return Severity(slots, totals.Length, Utc(0), Utc((totals.Length * bucketSpanTicks) - 1), total, bucketSpanTicks);
    }

    private static HistogramData ErrorPerBin(int binCount, long bucketSpanTicks)
    {
        int slotCount = LevelSeverity.SlotCount;
        var slots = new int[binCount * slotCount];

        for (int bin = 0; bin < binCount; bin++) { slots[(bin * slotCount) + (int)SeverityLevel.Error] = 1; }

        return Severity(slots, binCount, Utc(0), Utc((binCount * bucketSpanTicks) - 1), binCount, bucketSpanTicks);
    }

    private static HistogramData Severity(int[] slots, int binCount, DateTime minUtc, DateTime maxUtc, int total, long bucketSpanTicks) =>
        new(slots, LevelSeverity.SlotCount, binCount, minUtc, maxUtc, total, bucketSpanTicks, HistogramGroups.Severity);

    private static DateTime Utc(long ticks) => new(ticks, DateTimeKind.Utc);
}
