// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Store.EventTable;

public sealed record SetColumnWidthAction(ColumnName ColumnName, int Width);
