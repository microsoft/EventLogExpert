// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Filtering.TestUtils.Constants;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Tests.TestUtils;

namespace EventLogExpert.Runtime.Tests.Histogram;

public sealed class HistogramHighlightTieTests
{
    [Fact]
    public void Build_WhenHighlightTieIsNotRequested_LeavesGroupMasksNull()
    {
        EventColumnView view = DisplayViewTestFactory.Build(
            EventLogId.Create(),
            [FilterEventBuilder.CreateTestEvent(20)]);

        HistogramData data = HistogramBuilder.Build(
            view,
            HistogramDimension.EventId,
            HistogramConstants.MaxBuckets,
            TestContext.Current.CancellationToken)!;

        Assert.Null(data.GroupHighlightMasks);
    }

    [Fact]
    public void BuildWithHighlightTie_FieldDimension_SetsMasksForCountedRows()
    {
        EventColumnView view = DisplayViewTestFactory.Build(
            EventLogId.Create(),
            [FilterEventBuilder.CreateTestEvent(20, source: "Alpha")]);
        SavedFilter[] filters = [CreateFilter("Id == 20", HighlightColor.LightRed)];
        byte[] highlightWinners = view.EnsureHighlightWinners(filters, planKey: 1, TestContext.Current.CancellationToken);

        HistogramData data = BuildTie(view, HistogramDimension.Source, highlightWinners);

        int group = GroupIndex(data, "Alpha");
        Assert.Equal(1u << 1, data.GroupHighlightMasks![group]);
    }

    [Theory]
    [InlineData(HistogramDimension.Severity)]
    [InlineData(HistogramDimension.Source)]
    [InlineData(HistogramDimension.EventId)]
    [InlineData(HistogramDimension.Log)]
    [InlineData(HistogramDimension.LogonType)]
    [InlineData(HistogramDimension.TicketEncryptionType)]
    [InlineData(HistogramDimension.ErrorCode)]
    [InlineData(HistogramDimension.ProcessImage)]
    public void BuildWithHighlightTie_PreservesSlotCountsAcrossDimensions(HistogramDimension dimension)
    {
        EventColumnView view = DisplayViewTestFactory.Build(
            EventLogId.Create(),
            [
                FilterEventBuilder.CreateTestEvent(20, source: "Alpha", level: FilterTestConstants.EventLevelError),
                FilterEventBuilder.CreateTestEvent(21, source: "Beta", level: "Information")
            ]);
        SavedFilter[] filters = [CreateFilter("Id == 20 || Id == 21", HighlightColor.LightRed)];
        byte[] highlightWinners = view.EnsureHighlightWinners(filters, planKey: 1, TestContext.Current.CancellationToken);

        HistogramData plain = HistogramBuilder.Build(
            view,
            dimension,
            HistogramConstants.MaxBuckets,
            TestContext.Current.CancellationToken)!;
        HistogramData tied = BuildTie(view, dimension, highlightWinners);

        Assert.Equal(plain.SlotCounts, tied.SlotCounts);
    }

    [Fact]
    public void BuildWithHighlightTie_UsesCapturedWinnerArrayWhenViewCacheChanges()
    {
        EventColumnView view = DisplayViewTestFactory.Build(
            EventLogId.Create(),
            [FilterEventBuilder.CreateTestEvent(20)]);
        SavedFilter[] firstFilters = [CreateFilter("Id == 20", HighlightColor.LightRed)];
        SavedFilter[] secondFilters = [CreateFilter("Id == 21", HighlightColor.LightBlue)];
        byte[] capturedWinners = view.EnsureHighlightWinners(firstFilters, planKey: 1, TestContext.Current.CancellationToken);
        _ = view.EnsureHighlightWinners(secondFilters, planKey: 2, TestContext.Current.CancellationToken);

        HistogramData data = BuildTie(view, HistogramDimension.EventId, capturedWinners);

        int group = GroupIndex(data, "20");
        Assert.Equal(1u << 1, data.GroupHighlightMasks![group]);
    }

    [Fact]
    public void BuildWithHighlightTie_WhenGroupHasOneWinner_StoresThatWinnerMask()
    {
        EventColumnView view = DisplayViewTestFactory.Build(
            EventLogId.Create(),
            [FilterEventBuilder.CreateTestEvent(20), FilterEventBuilder.CreateTestEvent(20)]);
        SavedFilter[] filters = [CreateFilter("Id == 20", HighlightColor.LightRed)];
        byte[] highlightWinners = view.EnsureHighlightWinners(filters, planKey: 1, TestContext.Current.CancellationToken);

        HistogramData data = HistogramBuilder.BuildWithHighlightTie(
            view,
            HistogramDimension.EventId,
            HistogramConstants.MaxBuckets,
            highlightWinners,
            TestContext.Current.CancellationToken)!;

        int group = GroupIndex(data, "20");
        Assert.Equal(1u << 1, data.GroupHighlightMasks![group]);
    }

    [Fact]
    public void BuildWithHighlightTie_WhenGroupHasUncoloredRow_IncludesBitZero()
    {
        EventColumnView view = DisplayViewTestFactory.Build(
            EventLogId.Create(),
            [
                FilterEventBuilder.CreateTestEvent(20, source: FilterTestConstants.EventSourceTestSource),
                FilterEventBuilder.CreateTestEvent(20, source: FilterTestConstants.EventSourceOtherSource)
            ]);
        SavedFilter[] filters = [CreateFilter(FilterTestConstants.FilterSourceEqualsTestSource, HighlightColor.LightRed)];
        byte[] highlightWinners = view.EnsureHighlightWinners(filters, planKey: 1, TestContext.Current.CancellationToken);

        HistogramData data = HistogramBuilder.BuildWithHighlightTie(
            view,
            HistogramDimension.EventId,
            HistogramConstants.MaxBuckets,
            highlightWinners,
            TestContext.Current.CancellationToken)!;

        int group = GroupIndex(data, "20");
        Assert.Equal((1u << 0) | (1u << 1), data.GroupHighlightMasks![group]);
    }

    [Fact]
    public void BuildWithHighlightTie_WhenSeverityGroupFoldsSlots_OrsSlotMasks()
    {
        EventColumnView view = DisplayViewTestFactory.Build(
            EventLogId.Create(),
            [
                FilterEventBuilder.CreateTestEvent(20, level: FilterTestConstants.EventLevelError),
                FilterEventBuilder.CreateTestEvent(21, level: "Critical")
            ]);
        SavedFilter[] filters =
        [
            CreateFilter("Id == 20", HighlightColor.LightRed),
            CreateFilter("Id == 21", HighlightColor.LightRed)
        ];
        byte[] highlightWinners = view.EnsureHighlightWinners(filters, planKey: 1, TestContext.Current.CancellationToken);

        HistogramData data = HistogramBuilder.BuildWithHighlightTie(
            view,
            HistogramDimension.Severity,
            HistogramConstants.MaxBuckets,
            highlightWinners,
            TestContext.Current.CancellationToken)!;

        int group = GroupIndex(data, "Errors");
        Assert.Equal((1u << 1) | (1u << 2), data.GroupHighlightMasks![group]);
    }

    [Fact]
    public void CombinedColumnView_BuildWithHighlightTie_OrsPerChildMasks()
    {
        EventColumnView first = DisplayViewTestFactory.Build(
            EventLogId.Create(),
            [FilterEventBuilder.CreateTestEvent(20, source: "Alpha")]);
        EventColumnView second = DisplayViewTestFactory.Build(
            EventLogId.Create(),
            [FilterEventBuilder.CreateTestEvent(20, source: "Beta")]);
        var combined = new CombinedColumnView(
            [first, second],
            new SortContext(orderBy: null, isDescending: false, groupBy: null, isGroupDescending: false));
        SavedFilter[] filters =
        [
            CreateFilter("Source == \"Alpha\"", HighlightColor.LightRed),
            CreateFilter("Source == \"Beta\"", HighlightColor.LightBlue)
        ];
        byte[] highlightWinners = combined.EnsureHighlightWinners(filters, planKey: 1, TestContext.Current.CancellationToken);

        HistogramData data = BuildTie(combined, HistogramDimension.EventId, highlightWinners);

        int group = GroupIndex(data, "20");
        Assert.Equal((1u << 1) | (1u << 2), data.GroupHighlightMasks![group]);
    }

    [Fact]
    public void CombinedColumnView_BuildWithHighlightTie_RejectsForeignWinnerHandle()
    {
        EventColumnView first = DisplayViewTestFactory.Build(
            EventLogId.Create(),
            [FilterEventBuilder.CreateTestEvent(20)]);
        var combined = new CombinedColumnView(
            [first],
            new SortContext(orderBy: null, isDescending: false, groupBy: null, isGroupDescending: false));

        Assert.Throws<InvalidOperationException>(() =>
            BuildTie(combined, HistogramDimension.EventId, [1]));
    }

    [Fact]
    public void EnsureHighlightWinners_AfterPlanChange_ReturnsPlanConsistentSnapshot()
    {
        EventColumnView view = DisplayViewTestFactory.Build(
            EventLogId.Create(),
            [FilterEventBuilder.CreateTestEvent(20), FilterEventBuilder.CreateTestEvent(21)]);
        SavedFilter[] firstFilters = [CreateFilter("Id == 20", HighlightColor.LightRed)];
        SavedFilter[] secondFilters = [CreateFilter("Id == 21", HighlightColor.LightBlue)];
        byte[] firstWinners = view.EnsureHighlightWinners(firstFilters, planKey: 1, TestContext.Current.CancellationToken);
        byte[] secondWinners = view.EnsureHighlightWinners(secondFilters, planKey: 2, TestContext.Current.CancellationToken);

        byte[] firstAgainWinners = view.EnsureHighlightWinners(firstFilters, planKey: 1, TestContext.Current.CancellationToken);

        Assert.Equal(new byte[] { 1, 0 }, firstWinners);
        Assert.Equal(new byte[] { 0, 1 }, secondWinners);
        Assert.Equal(new byte[] { 1, 0 }, firstAgainWinners);
        Assert.NotSame(secondWinners, firstAgainWinners);
    }

    [Fact]
    public void EventColumnView_WithContext_PreservesCapturedHighlightWinners()
    {
        EventColumnView view = DisplayViewTestFactory.Build(
            EventLogId.Create(),
            [
                FilterEventBuilder.CreateTestEvent(20, timeCreated: DateTime.UtcNow.AddMinutes(1)),
                FilterEventBuilder.CreateTestEvent(21, timeCreated: DateTime.UtcNow)
            ]);
        SavedFilter[] filters = [CreateFilter("Id == 20", HighlightColor.LightRed)];
        byte[] winners = view.EnsureHighlightWinners(filters, planKey: 1, TestContext.Current.CancellationToken);
        IEventColumnView sorted = view.WithContext(new SortContext(ColumnName.DateAndTime, true, null, false));
        byte[] sortedWinners = sorted.EnsureHighlightWinners(filters, planKey: 1, TestContext.Current.CancellationToken);

        Assert.Same(winners, sortedWinners);
    }

    private static HistogramData BuildTie(
        IEventColumnView view,
        HistogramDimension dimension,
        byte[] highlightWinners) =>
        HistogramBuilder.BuildWithHighlightTie(
            view,
            dimension,
            HistogramConstants.MaxBuckets,
            highlightWinners,
            TestContext.Current.CancellationToken)!;

    private static SavedFilter CreateFilter(string text, HighlightColor color) =>
        SavedFilter.TryCreate(text, color: color, isEnabled: true)
        ?? throw new InvalidOperationException($"Failed to compile test filter '{text}'.");

    private static int GroupIndex(HistogramData data, string label)
    {
        for (int index = 0; index < data.Groups.Count; index++)
        {
            if (data.Groups[index].Label == label) { return index; }
        }

        throw new InvalidOperationException($"Group '{label}' was not created.");
    }
}
