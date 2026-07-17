// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.Runtime.LogTable;

/// <summary>
///     The live display facade: a filter-surviving, sorted view over one or more column-backed logs. The viewport
///     reads rows by display position through <see cref="Slice" />; selection, highlight, and scroll resolve by
///     <see cref="EventLocator" /> through <see cref="Rank" /> and <see cref="ResolveByKey" />.
/// </summary>
public interface IEventColumnView
{
    int Count { get; }

    /// <summary>
    ///     Group-by variant keyed on a named EventData field's whole-number code; (targetCodes length + 1) slots per bin,
    ///     with the trailing "other" slot also absorbing rows that lack the field.
    /// </summary>
    void BucketTimeTicksByEventData(
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        string fieldName,
        long[] targetCodes,
        int[] slotCounts,
        CancellationToken cancellationToken);

    /// <summary>
    ///     HRESULT-code variant of <see cref="BucketTimeTicksByEventData" /> for the ErrorCode dimension: only survivors
    ///     from a provider in <paramref name="eligibleProviders" /> whose <paramref name="fieldName" /> field holds a nonzero
    ///     32-bit HRESULT contribute (their target slot, else the trailing "other" slot); every other row is omitted.
    /// </summary>
    void BucketTimeTicksByEventDataHResult(
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        string fieldName,
        IReadOnlyCollection<string> eligibleProviders,
        long[] targetCodes,
        int[] slotCounts,
        CancellationToken cancellationToken);

    /// <summary>Group-by variant keyed on the numeric event id; (targetIds length + 1) slots per bin.</summary>
    void BucketTimeTicksByEventId(
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        int[] targetIds,
        int[] slotCounts,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Group-by variant of <see cref="BucketTimeTicksBySeverity" /> for a pooled string field; (targetValues length +
    ///     1) slots per bin.
    /// </summary>
    void BucketTimeTicksByField(
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        EventFieldId field,
        string[] targetValues,
        int[] slotCounts,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Additively buckets this view's rows by UTC tick and severity slot; bucket-major
    ///     slotCounts[i*LevelSeverity.SlotCount + slot], out-of-domain ticks clamp to the end buckets.
    /// </summary>
    void BucketTimeTicksBySeverity(
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        int[] slotCounts,
        CancellationToken cancellationToken);

    /// <summary>
    ///     HRESULT-code variant of <see cref="CountEventDataValues" /> for the ErrorCode dimension: tallies this view's
    ///     survivors from a provider in <paramref name="eligibleProviders" /> by the nonzero 32-bit HRESULT in
    ///     <paramref name="fieldName" /> (accumulating across a combined view); resolves the top-N failure codes.
    /// </summary>
    void CountEventDataHResults(string fieldName, IReadOnlyCollection<string> eligibleProviders, IDictionary<long, int> counts, CancellationToken cancellationToken);

    /// <summary>
    ///     Tallies this view's rows by the whole-number code of a named EventData field (accumulating across a combined
    ///     view, since a numeric code is store-independent); resolves the top-N group-by categories for the histogram.
    /// </summary>
    void CountEventDataValues(string fieldName, IDictionary<long, int> counts, CancellationToken cancellationToken);

    /// <summary>
    ///     Tallies this view's rows by numeric event id into <paramref name="counts" /> (accumulating across a combined
    ///     view).
    /// </summary>
    void CountEventIds(IDictionary<int, int> counts, CancellationToken cancellationToken);

    /// <summary>
    ///     Tallies this view's rows by non-empty pooled string value of field (accumulating, so a combined view sums by
    ///     logical value across logs); resolves the top-N group-by categories.
    /// </summary>
    void CountFieldValues(EventFieldId field, IDictionary<string, int> counts, CancellationToken cancellationToken);

    /// <summary>Full-detail rehydrate of every display row, in display order, for export and clipboard.</summary>
    IEnumerable<ResolvedEvent> EnumerateDetail();

    ResolvedEvent GetDetail(EventLocator locator);

    /// <summary>
    ///     Lean single-row rehydrate (grid scalars plus Description) for the row addressed by <paramref name="locator" />
    ///     .
    /// </summary>
    ResolvedEvent GetDetailLean(EventLocator locator);

    string GroupKeyAt(EventLocator locator, ColumnName column);

    EventLocator LocatorAt(int index);

    /// <summary>
    ///     The display position of <paramref name="locator" /> in this view, or <c>-1</c> when the locator is not in the
    ///     view (filtered out) or does not address this view's log generation.
    /// </summary>
    int Rank(EventLocator locator);

    /// <summary>
    ///     Re-resolves a stable <see cref="ValueKey" /> to the locator that currently carries it, or <c>null</c> when no
    ///     surviving row matches (a null-RecordId event never produces a key; a filtered-out row is absent). Drives selection
    ///     restore across a reload.
    /// </summary>
    EventLocator? ResolveByKey(ValueKey key);

    IReadOnlyList<DisplayRow> Slice(int start, int count);

    /// <summary>
    ///     Exception-free resolve of <paramref name="locator" /> to its full <see cref="ResolvedEvent" />: <c>false</c>
    ///     when the locator no longer addresses a live physical row (the log closed, the store rebuilt to a newer generation,
    ///     or the index is out of range). A valid but filtered-out row still resolves, so a focused selection stays
    ///     inspectable after a filter hides it.
    /// </summary>
    bool TryGetDetail(EventLocator locator, [NotNullWhen(true)] out ResolvedEvent? detail);

    /// <summary>
    ///     Exception-free read of locator's UTC-tick timestamp from the tick column (no rehydrate); false when it no
    ///     longer addresses a live row. Like TryGetDetail, a filtered-out row still resolves, so in-view callers must also
    ///     check Rank.
    /// </summary>
    bool TryGetTimeTicks(EventLocator locator, out long ticks);

    /// <summary>UTC-tick span across this view's rows: true with [minTicks, maxTicks], false when the view is empty.</summary>
    bool TryGetTimeTicksRange(out long minTicks, out long maxTicks, CancellationToken cancellationToken);
}
