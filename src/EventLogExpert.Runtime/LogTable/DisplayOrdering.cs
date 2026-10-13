// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.LogTable;

public readonly record struct DisplayOrdering(
    ColumnName? OrderBy,
    bool IsDescending,
    ColumnName? GroupBy,
    bool IsGroupDescending)
{
    public ColumnName? RequestedOrderBy { get; init; } = OrderBy;

    public bool RequestedIsDescending { get; init; } = IsDescending;

    public ColumnName? RequestedGroupBy { get; init; } = GroupBy;

    public bool RequestedIsGroupDescending { get; init; } = IsGroupDescending;

    // The column that actually orders the displayed rows, resolving the implicit default (DateAndTime for a combined or
    // timeline view, else RecordId) when no explicit sort is set, so a header can report aria-sort instead of "none".
    public ColumnName? EffectiveOrderBy { get; init; } = OrderBy;
}
