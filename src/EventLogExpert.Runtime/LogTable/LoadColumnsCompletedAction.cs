// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.Runtime.LogTable;

internal sealed record LoadColumnsCompletedAction(
    ImmutableDictionary<ColumnName, bool> LoadedColumns,
    ImmutableDictionary<ColumnName, int> ColumnWidths,
    ImmutableList<ColumnName> ColumnOrder);
