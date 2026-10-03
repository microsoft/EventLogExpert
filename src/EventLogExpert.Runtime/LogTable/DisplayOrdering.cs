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
}
