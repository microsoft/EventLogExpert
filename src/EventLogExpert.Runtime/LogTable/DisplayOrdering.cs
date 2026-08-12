// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.LogTable;

public readonly record struct DisplayOrdering(
    ColumnName? OrderBy,
    bool IsDescending,
    ColumnName? GroupBy,
    bool IsGroupDescending);
