// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Persistence;
using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.Runtime.LogTable;

internal sealed class EmptyColumnView : IEventColumnView
{
    private static readonly DisplayRow[] s_noRows = [];

    public int Count => 0;

    internal static EmptyColumnView Instance { get; } = new();

    public void BucketTimeTicksByEventData(
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        string fieldName,
        long[] targetCodes,
        int[] slotCounts,
        CancellationToken cancellationToken) { }

    public void BucketTimeTicksByEventDataHResult(
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        string fieldName,
        IReadOnlyCollection<string> eligibleProviders,
        IReadOnlyList<string> userDataErrorCodePaths,
        long[] targetCodes,
        int[] slotCounts,
        CancellationToken cancellationToken) { }

    public void BucketTimeTicksByEventDataHResultWithTie(
        byte[] highlightWinners,
        uint[] slotColorMask,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        string fieldName,
        IReadOnlyCollection<string> eligibleProviders,
        IReadOnlyList<string> userDataErrorCodePaths,
        long[] targetCodes,
        int[] slotCounts,
        CancellationToken cancellationToken) { }

    public void BucketTimeTicksByEventDataString(
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        string[] candidateFields,
        IReadOnlyDictionary<string, int> rawValueToSlot,
        int slotCount,
        int[] slotCounts,
        CancellationToken cancellationToken) { }

    public void BucketTimeTicksByEventDataStringWithTie(
        byte[] highlightWinners,
        uint[] slotColorMask,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        string[] candidateFields,
        IReadOnlyDictionary<string, int> rawValueToSlot,
        int slotCount,
        int[] slotCounts,
        CancellationToken cancellationToken) { }

    public void BucketTimeTicksByEventDataWithTie(
        byte[] highlightWinners,
        uint[] slotColorMask,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        string fieldName,
        long[] targetCodes,
        int[] slotCounts,
        CancellationToken cancellationToken) { }

    public void BucketTimeTicksByEventId(
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        int[] targetIds,
        int[] slotCounts,
        CancellationToken cancellationToken) { }

    public void BucketTimeTicksByEventIdWithTie(
        byte[] highlightWinners,
        uint[] slotColorMask,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        int[] targetIds,
        int[] slotCounts,
        CancellationToken cancellationToken) { }

    public void BucketTimeTicksByField(
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        EventFieldId field,
        string[] targetValues,
        int[] slotCounts,
        CancellationToken cancellationToken) { }

    public void BucketTimeTicksByFieldWithTie(
        byte[] highlightWinners,
        uint[] slotColorMask,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        EventFieldId field,
        string[] targetValues,
        int[] slotCounts,
        CancellationToken cancellationToken) { }

    public void BucketTimeTicksBySeverity(
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        int[] slotCounts,
        CancellationToken cancellationToken) { }

    public void BucketTimeTicksBySeverityWithTie(
        byte[] highlightWinners,
        uint[] slotColorMask,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        int[] slotCounts,
        CancellationToken cancellationToken) { }

    public void CountEventDataHResults(
        string fieldName,
        IReadOnlyCollection<string> eligibleProviders,
        IReadOnlyList<string> userDataErrorCodePaths,
        IDictionary<long, int> counts,
        CancellationToken cancellationToken) { }

    public void CountEventDataStringValues(
        string[] candidateFields,
        IDictionary<string, int> counts,
        CancellationToken cancellationToken) { }

    public void CountEventDataValues(
        string fieldName,
        IDictionary<long, int> counts,
        CancellationToken cancellationToken) { }

    public void CountEventIds(IDictionary<int, int> counts, CancellationToken cancellationToken) { }

    public void CountFieldValues(EventFieldId field, IDictionary<string, int> counts, CancellationToken cancellationToken) { }

    public byte[] EnsureHighlightWinners(
        IReadOnlyList<SavedFilter> orderedColoredFilters,
        int planKey,
        CancellationToken cancellationToken) => new byte[1];

    public IEnumerable<ResolvedEvent> EnumerateDetail() => [];

    public IEnumerable<ResolvedEvent> EnumerateDetailLean() => [];

    public ResolvedEvent GetDetail(EventLocator locator) => throw NotAMember(locator);

    public ResolvedEvent GetDetailLean(EventLocator locator) => throw NotAMember(locator);

    public string GroupKeyAt(EventLocator locator, ColumnName column) => throw NotAMember(locator);

    public EventLocator LocatorAt(int index) => throw new ArgumentOutOfRangeException(nameof(index));

    public int Rank(EventLocator locator) => -1;

    public EventLocator? ResolveByKey(ValueKey key) => null;

    public IReadOnlyList<DisplayRow> Slice(int start, int count)
    {
        if (start < 0) { throw new ArgumentOutOfRangeException(nameof(start)); }

        if (count < 0) { throw new ArgumentOutOfRangeException(nameof(count)); }

        return s_noRows;
    }

    public bool TryGetDetail(EventLocator locator, [NotNullWhen(true)] out ResolvedEvent? detail)
    {
        detail = null;

        return false;
    }

    public bool TryGetTimeTicks(EventLocator locator, out long ticks)
    {
        ticks = 0;

        return false;
    }

    public bool TryGetTimeTicksRange(out long minTicks, out long maxTicks, CancellationToken cancellationToken)
    {
        minTicks = 0;
        maxTicks = 0;

        return false;
    }

    private static KeyNotFoundException NotAMember(EventLocator locator) =>
        new($"Locator log id '{locator.LogId}' is not a member of this combined view.");
}
