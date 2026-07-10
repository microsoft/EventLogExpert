// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.LogTable;

internal readonly record struct SortContext
{
    internal SortContext(ColumnName? orderBy, bool isDescending, ColumnName? groupBy, bool isGroupDescending)
    {
        // When grouped, the comparer treats a null order-by as DateAndTime, so normalize so null and DateAndTime
        // are the same context/comparer. Ungrouped null stays RecordId-sorted.
        OrderBy = groupBy is null ? orderBy : orderBy ?? ColumnName.DateAndTime;
        IsDescending = isDescending;
        GroupBy = groupBy;
        IsGroupDescending = isGroupDescending;
    }

    internal ColumnName? OrderBy { get; }

    internal bool IsDescending { get; }

    internal ColumnName? GroupBy { get; }

    internal bool IsGroupDescending { get; }
}
