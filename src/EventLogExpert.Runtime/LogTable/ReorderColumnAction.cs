// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.LogTable;

internal sealed record ReorderColumnAction(ColumnName ColumnName, ColumnName TargetColumn, bool InsertAfter);
