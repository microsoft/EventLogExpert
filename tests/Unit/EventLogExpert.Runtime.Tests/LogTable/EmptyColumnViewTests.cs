// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class EmptyColumnViewTests
{
    private const int BucketCount = 4;
    private const long BucketSpanTicks = 1;
    private const long MinTicks = 0;

    private static readonly EventLocator s_absent = new(EventLogId.Create(), 0, 0);
    private static readonly IEventColumnView s_empty = EmptyColumnView.Instance;

    [Fact]
    public void Count_IsZero() => Assert.Equal(0, s_empty.Count);

    [Fact]
    public void Detail_ThrowsForALocatorItDoesNotOwn()
    {
        Assert.Throws<KeyNotFoundException>(() => s_empty.GetDetail(s_absent));
        Assert.Throws<KeyNotFoundException>(() => s_empty.GetDetailLean(s_absent));
        Assert.Throws<KeyNotFoundException>(() => s_empty.GroupKeyAt(s_absent, ColumnName.Source));
    }

    [Fact]
    public void EnumerateDetail_YieldsNothing() => Assert.Empty(s_empty.EnumerateDetail());

    [Fact]
    public void HighlightTieKernels_AcceptAnyHandleAndMutateNothing()
    {
        byte[] foreignHandle = [9, 9, 9];
        uint[] slotColorMask = [0xAAAAAAAAu, 0xBBBBBBBBu];
        int[] slotCounts = [5, 5, 5, 5];
        byte[] handleBeforeCalls = [.. foreignHandle];
        uint[] maskBeforeCalls = [.. slotColorMask];
        int[] countsBeforeCalls = [.. slotCounts];
        IReadOnlyCollection<string> eligibleProviders = ["Provider"];
        IReadOnlyList<string> userDataErrorCodePaths = ["System/Data"];
        IReadOnlyDictionary<string, int> rawValueToSlot = new Dictionary<string, int> { ["value"] = 0 };
        var cancellationToken = TestContext.Current.CancellationToken;

        s_empty.BucketTimeTicksByEventIdWithTie(
            foreignHandle, slotColorMask, MinTicks, BucketSpanTicks, BucketCount, [20], slotCounts, cancellationToken);
        s_empty.BucketTimeTicksBySeverityWithTie(
            foreignHandle, slotColorMask, MinTicks, BucketSpanTicks, BucketCount, slotCounts, cancellationToken);
        s_empty.BucketTimeTicksByFieldWithTie(
            foreignHandle, slotColorMask, MinTicks, BucketSpanTicks, BucketCount, EventFieldId.Source, ["value"], slotCounts, cancellationToken);
        s_empty.BucketTimeTicksByEventDataWithTie(
            foreignHandle, slotColorMask, MinTicks, BucketSpanTicks, BucketCount, "Field", [1], slotCounts, cancellationToken);
        s_empty.BucketTimeTicksByEventDataHResultWithTie(
            foreignHandle, slotColorMask, MinTicks, BucketSpanTicks, BucketCount, "Field", eligibleProviders, userDataErrorCodePaths, [1], slotCounts,
            cancellationToken);
        s_empty.BucketTimeTicksByEventDataStringWithTie(
            foreignHandle, slotColorMask, MinTicks, BucketSpanTicks, BucketCount, ["Field"], rawValueToSlot, BucketCount, slotCounts, cancellationToken);

        Assert.Equal(handleBeforeCalls, foreignHandle);
        Assert.Equal(maskBeforeCalls, slotColorMask);
        Assert.Equal(countsBeforeCalls, slotCounts);
    }

    [Fact]
    public void HighlightWinners_ReturnsADistinctHandlePerCall()
    {
        byte[] emptyFirst = s_empty.EnsureHighlightWinners([], 0, TestContext.Current.CancellationToken);
        byte[] emptySecond = s_empty.EnsureHighlightWinners([], 0, TestContext.Current.CancellationToken);

        Assert.Single(emptyFirst);
        Assert.NotSame(emptyFirst, emptySecond);
    }

    [Fact]
    public void Histogram_KernelsLeaveTheirAccumulatorsUntouched()
    {
        int[] emptySlots = new int[BucketCount];
        Dictionary<int, int> emptyCounts = [];

        s_empty.BucketTimeTicksBySeverity(MinTicks, BucketSpanTicks, BucketCount, emptySlots, TestContext.Current.CancellationToken);
        s_empty.CountEventIds(emptyCounts, TestContext.Current.CancellationToken);

        Assert.All(emptySlots, slot => Assert.Equal(0, slot));
        Assert.Empty(emptyCounts);
    }

    [Fact]
    public void LocatorAt_RejectsEveryIndex()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => s_empty.LocatorAt(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => s_empty.LocatorAt(-1));
    }

    [Fact]
    public void RankAndResolve_ReportAbsenceRatherThanThrowing()
    {
        Assert.Equal(-1, s_empty.Rank(s_absent));

        var key = new ValueKey(1, DateTime.UnixEpoch, "Source", "Log");

        Assert.Null(s_empty.ResolveByKey(key));
    }

    [Fact]
    public void Slice_ReturnsNothingAndRejectsNegativeArguments()
    {
        Assert.Empty(s_empty.Slice(0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => s_empty.Slice(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => s_empty.Slice(0, -1));
    }

    [Fact]
    public void TryGetters_ReportFailureWithAbsentOutputs()
    {
        Assert.False(s_empty.TryGetDetail(s_absent, out ResolvedEvent? emptyDetail));
        Assert.Null(emptyDetail);

        Assert.False(s_empty.TryGetTimeTicks(s_absent, out long emptyTicks));
        Assert.Equal(0, emptyTicks);

        Assert.False(s_empty.TryGetTimeTicksRange(out long emptyMin, out long emptyMax, TestContext.Current.CancellationToken));
        Assert.Equal(0, emptyMin);
        Assert.Equal(0, emptyMax);
    }
}
