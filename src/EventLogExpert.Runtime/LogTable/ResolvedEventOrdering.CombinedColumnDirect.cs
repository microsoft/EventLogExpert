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
        EventFieldId field = ColumnFieldMap.ToFieldId(column);
        EventFieldValue left = readerA.GetField(a, field);
        EventFieldValue right = readerB.GetField(b, field);

        return column switch
        {
            ColumnName.RecordId or ColumnName.ProcessId or ColumnName.ThreadId => CompareInt64Nullable(left, right),
            ColumnName.EventId => CompareInt64(left, right),
            ColumnName.DateAndTime => CompareDateTime(left, right),
            ColumnName.ActivityId => CompareGuidNullable(left, right),
            _ => string.Compare(left.AsString(), right.AsString(), StringComparison.Ordinal)
        };
    }

    // Cross-reader twin of SelectColumnComparer: must reproduce its ordering chain exactly, reading each side from its
    // own reader.
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

    private static int CompareRecordIdDirectAcross(
        IEventColumnReader readerA,
        EventLocator a,
        IEventColumnReader readerB,
        EventLocator b) =>
        CompareInt64Nullable(
            readerA.GetField(a, EventFieldId.RecordId), readerB.GetField(b, EventFieldId.RecordId));

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
