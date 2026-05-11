// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.UI.LogTable;

public sealed record LoadColumnsCompletedAction(
    IDictionary<ColumnName, bool> LoadedColumns,
    IDictionary<ColumnName, int> ColumnWidths,
    ImmutableList<ColumnName> ColumnOrder);
