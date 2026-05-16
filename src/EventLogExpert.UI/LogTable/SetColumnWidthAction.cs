// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.LogTable;

internal sealed record SetColumnWidthAction(ColumnName ColumnName, int Width);
