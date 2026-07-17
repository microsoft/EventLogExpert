// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.LogTable;

internal static partial class ResolvedEventOrdering
{
    // RecordId for one log while the timeline is hidden; timestamp for a combined view of several, or for a single log
    // while the timeline is shown (so the table reads in the same order as the time axis). An explicit sort or grouping
    // always wins.
    internal static ColumnName? ResolveDefaultOrderBy(ColumnName? orderBy, ColumnName? groupBy, int logCount, bool timelineVisible)
    {
        if (orderBy is not null || groupBy is not null) { return orderBy; }

        return logCount > 1 || timelineVisible ? ColumnName.DateAndTime : null;
    }

    private static int CompareDateTime(EventFieldValue left, EventFieldValue right)
    {
        left.TryGetDateTime(out DateTime leftValue);
        right.TryGetDateTime(out DateTime rightValue);

        // Ticks, not AsString(): reproduces DateTime.CompareTo and stays kind-agnostic.
        return leftValue.Ticks.CompareTo(rightValue.Ticks);
    }

    private static int CompareGuidNullable(EventFieldValue left, EventFieldValue right)
    {
        bool leftAbsent = left.Kind == EventFieldValueKind.Null;
        bool rightAbsent = right.Kind == EventFieldValueKind.Null;

        if (leftAbsent || rightAbsent)
        {
            return leftAbsent == rightAbsent ? 0 : (leftAbsent ? -1 : 1);
        }

        left.TryGetGuid(out Guid leftValue);
        right.TryGetGuid(out Guid rightValue);

        // Guid.CompareTo, not AsString(): reproduces Nullable.Compare(Guid?).
        return leftValue.CompareTo(rightValue);
    }

    private static int CompareInt64(EventFieldValue left, EventFieldValue right)
    {
        left.TryGetInt64(out long leftValue);
        right.TryGetInt64(out long rightValue);

        return leftValue.CompareTo(rightValue);
    }

    private static int CompareInt64Nullable(EventFieldValue left, EventFieldValue right)
    {
        bool leftAbsent = left.Kind == EventFieldValueKind.Null;
        bool rightAbsent = right.Kind == EventFieldValueKind.Null;

        // Absent sorts first, reproducing Nullable.Compare's null-low ordering.
        if (leftAbsent || rightAbsent)
        {
            return leftAbsent == rightAbsent ? 0 : (leftAbsent ? -1 : 1);
        }

        left.TryGetInt64(out long leftValue);
        right.TryGetInt64(out long rightValue);

        return leftValue.CompareTo(rightValue);
    }
}
