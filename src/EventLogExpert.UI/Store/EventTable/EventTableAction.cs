// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.EventTable;

public sealed record EventTableAction
{
    public sealed record AddTable(EventLogData LogData);

    public sealed record AppendTableEvents(EventLogId LogId, IReadOnlyList<DisplayEventModel> Events);

    public sealed record AppendTableEventsBatch(IReadOnlyDictionary<EventLogId, IReadOnlyList<DisplayEventModel>> EventsByLog);

    public sealed record CloseAll;

    public sealed record CloseLog(EventLogId LogId);

    public sealed record LoadColumns;

    public sealed record LoadColumnsCompleted(
        IDictionary<ColumnName, bool> LoadedColumns,
        IDictionary<ColumnName, int> ColumnWidths,
        ImmutableList<ColumnName> ColumnOrder);

    public sealed record ReorderColumn(ColumnName ColumnName, ColumnName TargetColumn, bool InsertAfter);

    public sealed record ResetColumnDefaults;

    public sealed record SetActiveTable(EventLogId LogId);

    public sealed record SetColumnWidth(ColumnName ColumnName, int Width);

    public sealed record SetOrderBy(ColumnName? OrderBy);

    public sealed record ToggleColumn(ColumnName ColumnName);

    public sealed record ToggleLoading(EventLogId LogId);

    public sealed record ToggleSorting;

    public sealed record UpdateCombinedEvents;

    public sealed record UpdateDisplayedEvents(
        IReadOnlyDictionary<EventLogId, IReadOnlyList<DisplayEventModel>> ActiveLogs);

    public sealed record UpdateTable(EventLogId LogId, IReadOnlyList<DisplayEventModel> Events);
}
