// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.Histogram;

namespace EventLogExpert.Runtime.Tests.Histogram;

public sealed class HistogramSummaryTests
{
    [Fact]
    public void RegionLabel_CitesRawTotalAndErrorAndWarningCounts()
    {
        int slotCount = LevelSeverity.SlotCount;
        var slots = new int[3 * slotCount];
        slots[(0 * slotCount) + (int)SeverityLevel.Error] = 2;
        slots[(1 * slotCount) + (int)SeverityLevel.Warning] = 9;
        slots[(2 * slotCount) + (int)SeverityLevel.Information] = 4;

        var data = Severity(
            slots,
            3,
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 1, 3, 0, 0, DateTimeKind.Utc),
            15,
            TimeSpan.FromHours(1).Ticks);

        string label = HistogramSummary.RegionLabel(data, TimeZoneInfo.Utc);

        Assert.Contains("15 events", label);
        Assert.Contains("2 Errors", label);
        Assert.Contains("9 Warnings", label);
    }

    [Fact]
    public void RegionLabel_CountsCriticalWithinTheErrorGroup()
    {
        int slotCount = LevelSeverity.SlotCount;
        var slots = new int[slotCount];
        slots[(int)SeverityLevel.Critical] = 3;
        slots[(int)SeverityLevel.Error] = 1;

        var data = Severity(
            slots,
            1,
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 1, 1, 0, 0, DateTimeKind.Utc),
            4,
            TimeSpan.FromHours(1).Ticks);

        string label = HistogramSummary.RegionLabel(data, TimeZoneInfo.Utc);

        Assert.Contains("4 Errors", label);
    }

    [Fact]
    public void RegionLabel_NamesTopCategoriesForAGroupByDimension()
    {
        // Two categories over a single bin: 5 in category "chrome", 2 in "edge", plus 1 in Other (slot 2).
        var groups = HistogramGroups.ForCategories(["chrome", "edge"]);
        var slots = new[] { 5, 2, 1 };

        var data = new HistogramData(
            slots,
            3,
            1,
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 1, 1, 0, 0, DateTimeKind.Utc),
            8,
            TimeSpan.FromHours(1).Ticks,
            groups);

        string label = HistogramSummary.RegionLabel(data, TimeZoneInfo.Utc);

        Assert.Contains("8 events", label);
        Assert.Contains("5 chrome", label);
        Assert.Contains("2 edge", label);
        Assert.Contains("1 Other", label);
    }

    [Fact]
    public void WindowAnnouncement_ReportsTheVisibleRangeAndCounts()
    {
        var render = new HistogramRender(
            [new HistogramRenderBin(0, 10, 15, [4, 9, 2])],
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks,
            new DateTime(2024, 1, 1, 3, 0, 0, DateTimeKind.Utc).Ticks,
            15,
            15,
            [4, 9, 2]);

        string announcement = HistogramSummary.WindowAnnouncement(render, HistogramGroups.Severity, TimeZoneInfo.Utc);

        Assert.Contains("Showing", announcement);
        Assert.Contains("15 events", announcement);
        Assert.Contains("2 Errors", announcement);
        Assert.Contains("9 Warnings", announcement);
    }

    private static HistogramData Severity(int[] slots, int binCount, DateTime minUtc, DateTime maxUtc, int total, long bucketSpanTicks) =>
        new(slots, LevelSeverity.SlotCount, binCount, minUtc, maxUtc, total, bucketSpanTicks, HistogramGroups.Severity);
}
