// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Store.EventTable;

public sealed record EventTableAction
{
    public sealed record AddTable(EventLogData LogData);

    public sealed record CloseAll;

    public sealed record CloseLog(EventLogId LogId);

    public sealed record LoadColumns;

    public sealed record LoadColumnsCompleted(IDictionary<ColumnName, bool> LoadedColumns);

    public sealed record SetActiveTable(EventLogId LogId);

    public sealed record SetOrderBy(ColumnName? OrderBy);

    public sealed record ToggleColumn(ColumnName ColumnName);

    public sealed record ToggleLoading(EventLogId LogId);

    public sealed record ToggleSorting;

    public sealed record UpdateCombinedEvents;

    public sealed record UpdateDisplayedEvents(IDictionary<EventLogId, IEnumerable<DisplayEventModel>> ActiveLogs);
}
