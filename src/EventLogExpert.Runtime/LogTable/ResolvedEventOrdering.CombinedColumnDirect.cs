// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.LogTable;

internal static partial class ResolvedEventOrdering
{
    internal delegate int CrossComparison(
        IEventColumnReader readerA,
        EventLocator a,
        IEventColumnReader readerB,
        EventLocator b);

    internal static int CompareColumnDirectAcross(
        IEventColumnReader readerA,
        EventLocator a,
        IEventColumnReader readerB,
        EventLocator b,
        ColumnName column)
    {
        EventFieldId field = ColumnDescriptors.GetFieldId(column);

        return CompareFieldValues(readerA.GetField(a, field), readerB.GetField(b, field), column);
    }

    // Read each EventFieldId at most once per head, then assign it to every MergeHead role that aliases to it, so a
    // config like GroupBy==OrderBy, OrderBy==RecordId, or the default DateAndTime within+date tie-break issues a single
    // reader lookup instead of two. Only fields the comparer SelectCachedHeadComparer picks for this (orderBy, groupBy)
    // shape can consume are read at all (grouped: group+within+date-when-within!=DateAndTime; ungrouped-ordered: within;
    // ungrouped-default: date), so the k-way merge compares cached values instead of calling reader.GetField per compare.
    internal static MergeHead CreateMergeHead(IEventColumnReader reader, EventLocator locator, ColumnName? orderBy, ColumnName? groupBy)
    {
        EventFieldValue recordId = reader.GetField(locator, EventFieldId.RecordId);
        EventFieldValue owningLog = reader.GetField(locator, EventFieldId.OwningLog);

        ColumnName withinColumn = orderBy ?? ColumnName.DateAndTime;
        EventFieldId withinFieldId = ColumnDescriptors.GetFieldId(withinColumn);
        EventFieldId dateFieldId = ColumnDescriptors.GetFieldId(ColumnName.DateAndTime);
        bool grouped = groupBy is not null;

        bool readsWithin = grouped || orderBy is not null;
        EventFieldValue within = readsWithin ? ReadField(withinFieldId) : default;

        bool readsDate = (grouped && withinColumn != ColumnName.DateAndTime) || (!grouped && orderBy is null);
        EventFieldValue date = readsDate ?
            (readsWithin && withinFieldId == dateFieldId ? within : ReadField(dateFieldId)) :
            default;

        EventFieldValue group = default;

        if (groupBy is { } groupColumn)
        {
            EventFieldId groupFieldId = ColumnDescriptors.GetFieldId(groupColumn);
            group = readsWithin && groupFieldId == withinFieldId ? within :
                readsDate && groupFieldId == dateFieldId ? date :
                ReadField(groupFieldId);
        }

        return new(locator, group, within, date, recordId, owningLog);

        EventFieldValue ReadField(EventFieldId field) =>
            field == EventFieldId.RecordId ? recordId :
            reader.GetField(locator, field);
    }

    // Cached-head mirror of SelectCrossColumnComparer + DelegatingOrderKeyComparer's identity tie-break: identical chain
    // and inversion rules (grouped negates; ungrouped argument-swaps; the identity tie-break stays ascending) over the
    // values cached in MergeHead. Correct by construction (same logic, same inputs) - guarded by the combined
    // differential tests (CombinedBulk_MatchesIncremental_* assert the bulk order equals the live incremental order).
    internal static MergeHeadComparison SelectCachedHeadComparer(
        ColumnName? orderBy,
        bool isDescending,
        ColumnName? groupBy,
        bool isGroupDescending)
    {
        if (groupBy is { } groupColumn)
        {
            ColumnName withinColumn = orderBy ?? ColumnName.DateAndTime;

            return (in a, in b) =>
            {
                int group = CompareFieldValues(a.Group, b.Group, groupColumn);

                if (group != 0) { return WithIdentity(isGroupDescending ? -Math.Sign(group) : group, a, b); }

                int within = CompareFieldValues(a.Within, b.Within, withinColumn);

                if (within == 0 && withinColumn != ColumnName.DateAndTime)
                {
                    within = CompareFieldValues(a.Date, b.Date, ColumnName.DateAndTime);
                }

                if (within == 0)
                {
                    within = FallbackCached(CompareFieldValues(a.RecordId, b.RecordId, ColumnName.RecordId), a, b);
                }

                return WithIdentity(isDescending ? -Math.Sign(within) : within, a, b);
            };
        }

        if (orderBy is null)
        {
            return isDescending ?
                (in a, in b) => WithIdentity(DefaultCached(b, a), a, b) :
                (in a, in b) => WithIdentity(DefaultCached(a, b), a, b);
        }

        ColumnName orderColumn = orderBy.Value;

        return isDescending ?
            (in a, in b) => WithIdentity(OrderedCached(b, a, orderColumn), a, b) :
            (in a, in b) => WithIdentity(OrderedCached(a, b, orderColumn), a, b);
    }

    // Cross-reader form of the column-direct ordering chain: reproduces the same ungrouped, no-order-by-default, or grouped
    // order as the single-reader sort, reading each side from its own reader.
    internal static CrossComparison SelectCrossColumnComparer(
        ColumnName? orderBy,
        bool isDescending,
        ColumnName? groupBy,
        bool isGroupDescending)
    {
        if (groupBy is not null)
        {
            ColumnName groupColumn = groupBy.Value;
            ColumnName withinColumn = orderBy ?? ColumnName.DateAndTime;

            return (readerA, a, readerB, b) =>
            {
                int group = CompareColumnDirectAcross(readerA, a, readerB, b, groupColumn);

                if (group != 0) { return isGroupDescending ? -Math.Sign(group) : group; }

                int within = CompareColumnDirectAcross(readerA, a, readerB, b, withinColumn);

                if (within == 0 && withinColumn != ColumnName.DateAndTime)
                {
                    within = CompareColumnDirectAcross(readerA, a, readerB, b, ColumnName.DateAndTime);
                }

                if (within == 0)
                {
                    within = FallbackTieBreakerDirectAcross(
                        CompareRecordIdDirectAcross(readerA, a, readerB, b), readerA, a, readerB, b);
                }

                return isDescending ? -Math.Sign(within) : within;
            };
        }

        if (orderBy is null)
        {
            return isDescending ?
                (readerA, a, readerB, b) => AscendingDefault(readerB, b, readerA, a) :
                AscendingDefault;

            static int AscendingDefault(IEventColumnReader readerA, EventLocator a, IEventColumnReader readerB, EventLocator b)
            {
                int byRecordId = CompareRecordIdDirectAcross(readerA, a, readerB, b);

                if (byRecordId != 0) { return byRecordId; }

                int byTime = CompareColumnDirectAcross(readerA, a, readerB, b, ColumnName.DateAndTime);

                return byTime != 0 ?
                    byTime :
                    string.Compare(
                        readerA.GetField(a, EventFieldId.OwningLog).AsString(),
                        readerB.GetField(b, EventFieldId.OwningLog).AsString(),
                        StringComparison.Ordinal);
            }
        }

        ColumnName orderColumn = orderBy.Value;

        return isDescending ?
            (readerA, a, readerB, b) => AscendingColumn(readerB, b, readerA, a) :
            AscendingColumn;

        int AscendingColumn(IEventColumnReader readerA, EventLocator a, IEventColumnReader readerB, EventLocator b) =>
            WithTieBreakerDirectAcross(
                CompareColumnDirectAcross(readerA, a, readerB, b, orderColumn), readerA, a, readerB, b);
    }

    // The per-column leaf compare, shared by the live cross comparer (CompareColumnDirectAcross) and the cached-head
    // merge comparer below, so the two stay in lockstep by construction. The combined k-way merge caches these
    // EventFieldValues once per head instead of re-reading them from the reader on every comparison.
    private static int CompareFieldValues(EventFieldValue left, EventFieldValue right, ColumnName column) =>
        column switch
        {
            ColumnName.RecordId or ColumnName.ProcessId or ColumnName.ThreadId => CompareInt64Nullable(left, right),
            ColumnName.EventId => CompareInt64(left, right),
            ColumnName.DateAndTime => CompareDateTime(left, right),
            ColumnName.ActivityId => CompareGuidNullable(left, right),
            _ => string.Compare(left.AsString(), right.AsString(), StringComparison.Ordinal)
        };

    private static int CompareIdentityCached(in EventLocator left, in EventLocator right)
    {
        int byLog = left.LogId.Value.CompareTo(right.LogId.Value);

        if (byLog != 0) { return byLog; }

        if (left.Generation != right.Generation) { return left.Generation < right.Generation ? -1 : 1; }

        return left.Index.CompareTo(right.Index);
    }

    private static int CompareRecordIdDirectAcross(
        IEventColumnReader readerA,
        EventLocator a,
        IEventColumnReader readerB,
        EventLocator b) =>
        CompareInt64Nullable(
            readerA.GetField(a, EventFieldId.RecordId), readerB.GetField(b, EventFieldId.RecordId));

    private static int DefaultCached(in MergeHead a, in MergeHead b)
    {
        int byRecordId = CompareFieldValues(a.RecordId, b.RecordId, ColumnName.RecordId);

        if (byRecordId != 0) { return byRecordId; }

        int byTime = CompareFieldValues(a.Date, b.Date, ColumnName.DateAndTime);

        return byTime != 0 ?
            byTime :
            string.Compare(a.OwningLog.AsString(), b.OwningLog.AsString(), StringComparison.Ordinal);
    }

    private static int FallbackCached(int recordIdResult, in MergeHead a, in MergeHead b) =>
        recordIdResult != 0 ?
            recordIdResult :
            string.Compare(a.OwningLog.AsString(), b.OwningLog.AsString(), StringComparison.Ordinal);

    private static int FallbackTieBreakerDirectAcross(
        int recordIdResult,
        IEventColumnReader readerA,
        EventLocator a,
        IEventColumnReader readerB,
        EventLocator b) =>
        recordIdResult != 0 ?
            recordIdResult :
            string.Compare(
                readerA.GetField(a, EventFieldId.OwningLog).AsString(),
                readerB.GetField(b, EventFieldId.OwningLog).AsString(),
                StringComparison.Ordinal);

    private static int OrderedCached(in MergeHead a, in MergeHead b, ColumnName orderColumn) =>
        WithTieBreakerCached(CompareFieldValues(a.Within, b.Within, orderColumn), a, b);

    private static int WithIdentity(int cross, in MergeHead a, in MergeHead b) =>
        cross != 0 ? cross : CompareIdentityCached(a.Locator, b.Locator);

    private static int WithTieBreakerCached(int primaryResult, in MergeHead a, in MergeHead b) =>
        primaryResult != 0 ?
            primaryResult :
            FallbackCached(CompareFieldValues(a.RecordId, b.RecordId, ColumnName.RecordId), a, b);

    private static int WithTieBreakerDirectAcross(
        int primaryResult,
        IEventColumnReader readerA,
        EventLocator a,
        IEventColumnReader readerB,
        EventLocator b) =>
        primaryResult != 0 ?
            primaryResult :
            FallbackTieBreakerDirectAcross(
                CompareRecordIdDirectAcross(readerA, a, readerB, b), readerA, a, readerB, b);
}

internal delegate int MergeHeadComparison(in MergeHead a, in MergeHead b);

// One combined-merge head: the locator plus the sort-relevant field values cached once (read via CreateMergeHead).
internal readonly struct MergeHead(
    EventLocator locator,
    EventFieldValue group,
    EventFieldValue within,
    EventFieldValue date,
    EventFieldValue recordId,
    EventFieldValue owningLog)
{
    internal EventLocator Locator { get; } = locator;
    internal EventFieldValue Group { get; } = group;
    internal EventFieldValue Within { get; } = within;
    internal EventFieldValue Date { get; } = date;
    internal EventFieldValue RecordId { get; } = recordId;
    internal EventFieldValue OwningLog { get; } = owningLog;
}
