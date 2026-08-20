// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Persistence;
using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.Runtime.LogTable;

public interface IEventColumnView
{
    int Count { get; }

    void BucketTimeTicksByEventData(
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        string fieldName,
        long[] targetCodes,
        int[] slotCounts,
        CancellationToken cancellationToken);

    void BucketTimeTicksByEventDataHResult(
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        string fieldName,
        IReadOnlyCollection<string> eligibleProviders,
        IReadOnlyList<string> userDataErrorCodePaths,
        long[] targetCodes,
        int[] slotCounts,
        CancellationToken cancellationToken);

    void BucketTimeTicksByEventDataHResultWithTie(
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
        CancellationToken cancellationToken);

    void BucketTimeTicksByEventDataString(
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        string[] candidateFields,
        IReadOnlyDictionary<string, int> rawValueToSlot,
        int slotCount,
        int[] slotCounts,
        CancellationToken cancellationToken);

    void BucketTimeTicksByEventDataStringWithTie(
        byte[] highlightWinners,
        uint[] slotColorMask,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        string[] candidateFields,
        IReadOnlyDictionary<string, int> rawValueToSlot,
        int slotCount,
        int[] slotCounts,
        CancellationToken cancellationToken);

    void BucketTimeTicksByEventDataWithTie(
        byte[] highlightWinners,
        uint[] slotColorMask,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        string fieldName,
        long[] targetCodes,
        int[] slotCounts,
        CancellationToken cancellationToken);

    void BucketTimeTicksByEventId(
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        int[] targetIds,
        int[] slotCounts,
        CancellationToken cancellationToken);

    void BucketTimeTicksByEventIdWithTie(
        byte[] highlightWinners,
        uint[] slotColorMask,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        int[] targetIds,
        int[] slotCounts,
        CancellationToken cancellationToken);

    void BucketTimeTicksByField(
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        EventFieldId field,
        string[] targetValues,
        int[] slotCounts,
        CancellationToken cancellationToken);

    void BucketTimeTicksByFieldWithTie(
        byte[] highlightWinners,
        uint[] slotColorMask,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        EventFieldId field,
        string[] targetValues,
        int[] slotCounts,
        CancellationToken cancellationToken);

    void BucketTimeTicksBySeverity(
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        int[] slotCounts,
        CancellationToken cancellationToken);

    void BucketTimeTicksBySeverityWithTie(
        byte[] highlightWinners,
        uint[] slotColorMask,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        int[] slotCounts,
        CancellationToken cancellationToken);

    void CountEventDataHResults(
        string fieldName,
        IReadOnlyCollection<string> eligibleProviders,
        IReadOnlyList<string> userDataErrorCodePaths,
        IDictionary<long, int> counts,
        CancellationToken cancellationToken);

    void CountEventDataStringValues(string[] candidateFields, IDictionary<string, int> counts, CancellationToken cancellationToken);

    void CountEventDataValues(string fieldName, IDictionary<long, int> counts, CancellationToken cancellationToken);

    void CountEventIds(IDictionary<int, int> counts, CancellationToken cancellationToken);

    void CountFieldValues(EventFieldId field, IDictionary<string, int> counts, CancellationToken cancellationToken);

    void CountResolutionBySource(IDictionary<string, ProviderResolutionCounts> counts, CancellationToken cancellationToken);

    void CountResolutionDetailForSource(
        string source,
        IDictionary<int, ProviderResolutionCounts> byId,
        ProviderResolutionCounts[] byLevelSlot,
        CancellationToken cancellationToken);

    void CountSeverity(int[] slotCounts, CancellationToken cancellationToken);

    byte[] EnsureHighlightWinners(
        IReadOnlyList<SavedFilter> orderedColoredFilters,
        int planKey,
        CancellationToken cancellationToken);

    IEnumerable<ResolvedEvent> EnumerateDetail();

    IEnumerable<ResolvedEvent> EnumerateDetailLean() => EnumerateDetail();

    ResolvedEvent GetDetail(EventLocator locator);

    ResolvedEvent GetDetailLean(EventLocator locator);

    string GroupKeyAt(EventLocator locator, ColumnName column);

    EventLocator LocatorAt(int index);

    int Rank(EventLocator locator);

    EventLocator? ResolveByKey(ValueKey key);

    IReadOnlyList<DisplayRow> Slice(int start, int count);

    bool TryGetDetail(EventLocator locator, [NotNullWhen(true)] out ResolvedEvent? detail);

    bool TryGetTimeTicks(EventLocator locator, out long ticks);

    bool TryGetTimeTicksRange(out long minTicks, out long maxTicks, CancellationToken cancellationToken);
}
