// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Structured;

namespace EventLogExpert.Eventing.Common.Events;

public interface IEventColumnReader
{
    long ContentVersion { get; }

    int Count { get; }

    int Generation { get; }

    EventLogId LogId { get; }

    /// <summary>
    ///     The interned string pool backing the pooled columns, for ranking distinct <see cref="CopyPoolIndexColumn" />
    ///     values (index i is the string at pool index i; may contain duplicate values across appended segments).
    /// </summary>
    IReadOnlyList<string?> Pool { get; }

    /// <summary>
    ///     Bucket-by-EventData variant of <see cref="BucketTimeTicksByField" />: each survivor lands in its
    ///     <paramref name="targetCodes" /> slot (matched on the field's whole-number code) else the trailing "other" slot,
    ///     which also absorbs rows that lack the field; (<paramref name="targetCodes" /> length + 1) slots per bin.
    /// </summary>
    void BucketTimeTicksByEventData(
        ReadOnlySpan<int> rankByPhysical,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        string fieldName,
        long[] targetCodes,
        int[] slotCounts,
        CancellationToken cancellationToken);

    /// <summary>
    ///     HRESULT-code variant of <see cref="BucketTimeTicksByEventData" /> for the ErrorCode dimension: each survivor
    ///     from a provider in <paramref name="eligibleProviders" /> whose <paramref name="fieldName" /> field holds a nonzero
    ///     32-bit HRESULT lands in its <paramref name="targetCodes" /> slot else the trailing "other" slot; every
    ///     ineligible-provider, absent-field, or zero/undecodable row is omitted (contributes to no slot).
    /// </summary>
    void BucketTimeTicksByEventDataHResult(
        ReadOnlySpan<int> rankByPhysical,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        string fieldName,
        IReadOnlyCollection<string> eligibleProviders,
        long[] targetCodes,
        int[] slotCounts,
        CancellationToken cancellationToken);

    /// <summary>Group-by variant keyed on the numeric event id; (<paramref name="targetIds" /> length + 1) slots per bin.</summary>
    void BucketTimeTicksByEventId(
        ReadOnlySpan<int> rankByPhysical,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        int[] targetIds,
        int[] slotCounts,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Group-by variant for a pooled string field: each survivor lands in its targetValues slot (resolved per store,
    ///     so it sums across differently-pooled logs) else the trailing "other" slot; (targetValues length + 1) slots per bin.
    /// </summary>
    void BucketTimeTicksByField(
        ReadOnlySpan<int> rankByPhysical,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        EventFieldId field,
        string[] targetValues,
        int[] slotCounts,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Additively buckets survivors (rank &gt;= 0) by UTC tick and severity slot; bucket-major
    ///     slotCounts[i*LevelSeverity.SlotCount + slot], out-of-domain ticks clamp to the end buckets.
    /// </summary>
    void BucketTimeTicksBySeverity(
        ReadOnlySpan<int> rankByPhysical,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        int[] slotCounts,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Bulk-copies the <see cref="EventFieldId.ActivityId" /> column into caller-owned arrays over the physical range
    ///     [0, <see cref="Count" />). <paramref name="hasValue" />[i] is false for an absent (null) value.
    /// </summary>
    void CopyGuidColumn(EventFieldId field, Guid[] values, bool[] hasValue);

    /// <summary>
    ///     Bulk-copies an integral column (Id, RecordId, ProcessId, ThreadId as values; TimeCreated as UTC ticks) into
    ///     caller-owned arrays over [0, <see cref="Count" />). ProcessId and ThreadId widen from int. Id and TimeCreated are
    ///     always present; <paramref name="hasValue" />[i] is false only for an absent RecordId/ProcessId/ThreadId.
    /// </summary>
    void CopyInt64Column(EventFieldId field, long[] values, bool[] hasValue);

    /// <summary>
    ///     Bulk-copies a pooled string column (Level, LogName, ComputerName, Source, TaskCategory, UserId, Description,
    ///     Xml, OwningLog) as raw pool indices over [0, <see cref="Count" />); -1 marks a null value, and each non-negative
    ///     entry indexes <see cref="Pool" />. KeywordsDisplay is not a single pooled column and is not supported here.
    /// </summary>
    void CopyPoolIndexColumn(EventFieldId field, int[] poolIndices);

    /// <summary>
    ///     HRESULT-code variant of <see cref="CountEventDataValues" /> for the ErrorCode dimension: tallies survivors
    ///     from a provider in <paramref name="eligibleProviders" /> by the nonzero 32-bit HRESULT in
    ///     <paramref name="fieldName" /> (a <c>win:HexInt32</c> code that sign-extends negative is reinterpreted unsigned, and
    ///     hex / decimal string spellings fold to one code); ineligible-provider, absent-field, and zero/undecodable rows are
    ///     omitted.
    /// </summary>
    void CountEventDataHResults(ReadOnlySpan<int> rankByPhysical, string fieldName, IReadOnlyCollection<string> eligibleProviders, IDictionary<long, int> counts, CancellationToken cancellationToken);

    /// <summary>
    ///     Group-by variant for a named EventData field of allowlisted numeric codes: tallies survivors by the field's
    ///     whole-number code (decimal and <c>0x</c>-hex spellings canonicalize to one code), so the histogram can split, for
    ///     example, by LogonType or TicketEncryptionType. Rows that lack the field are omitted.
    /// </summary>
    void CountEventDataValues(ReadOnlySpan<int> rankByPhysical, string fieldName, IDictionary<long, int> counts, CancellationToken cancellationToken);

    /// <summary>
    ///     Tallies each surviving row by its numeric event id into <paramref name="counts" /> (accumulating across a
    ///     combined view).
    /// </summary>
    void CountEventIds(ReadOnlySpan<int> rankByPhysical, IDictionary<int, int> counts, CancellationToken cancellationToken);

    /// <summary>
    ///     Tallies survivors by non-empty pooled string value of field (accumulating, so a combined view sums by logical
    ///     value across pools); resolves the top-N group-by categories.
    /// </summary>
    void CountFieldValues(ReadOnlySpan<int> rankByPhysical, EventFieldId field, IDictionary<string, int> counts, CancellationToken cancellationToken);

    EventDataFieldEnumerator EnumerateEventData(EventLocator locator);

    UserDataFieldEnumerator EnumerateUserData(EventLocator locator);

    /// <summary>
    ///     Rehydrates the full <see cref="ResolvedEvent" /> addressed by <paramref name="locator" /> (every field,
    ///     including the detail-only &lt;EventData&gt;, UserData, and XML), letting a caller re-materialize a row without
    ///     reaching into the underlying store.
    /// </summary>
    /// <param name="locator">The row to rehydrate, as minted by this reader for its own snapshot.</param>
    /// <returns>The fully reconstructed event.</returns>
    ResolvedEvent GetDetail(EventLocator locator);

    /// <summary>
    ///     The viewport variant of <see cref="GetDetail" />: rehydrates the grid-visible fields of the row addressed by
    ///     <paramref name="locator" /> and leaves the detail-only payloads unmaterialized, for the fast display path.
    /// </summary>
    /// <param name="locator">The row to rehydrate, as minted by this reader for its own snapshot.</param>
    /// <returns>The event with grid fields populated and detail-only payloads left empty.</returns>
    ResolvedEvent GetDetailLean(EventLocator locator);

    EventFieldValue GetField(EventLocator locator, EventFieldId field);

    IReadOnlyList<string> GetKeywords(EventLocator locator);

    /// <summary>The UTC-tick timestamp of <paramref name="locator" /> read straight from the tick column, with no rehydrate.</summary>
    long GetTimeTicks(EventLocator locator);

    StructuredFieldResult GetUserData(EventLocator locator, string storageKey);

    bool GetUserDataIncomplete(EventLocator locator);

    EventLocator LocatorAt(int index);

    bool TryGetEventData(EventLocator locator, string fieldName, out EventFieldValue value);

    /// <summary>UTC-tick span across survivors (rank &gt;= 0): true with [minTicks, maxTicks], false when none survive.</summary>
    bool TryGetTimeTicksRange(
        ReadOnlySpan<int> rankByPhysical,
        out long minTicks,
        out long maxTicks,
        CancellationToken cancellationToken);
}
