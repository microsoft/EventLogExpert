// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;

namespace EventLogExpert.UI.Store.EventTable;

public sealed record EventTableAction
{
    public sealed record CloseAll;

    public sealed record CloseLog(string LogName);

    public sealed record NewTable(string? FileName, string LogName, LogType LogType);

    public sealed record SetActiveTable(Guid TableId);

    public sealed record SetOrderBy(ColumnName? OrderBy);

    public sealed record ToggleLoading(string LogName);

    public sealed record ToggleSorting;

    public sealed record UpdateDisplayedEvents(Dictionary<string, IEnumerable<DisplayEventModel>> ActiveLogs);
}
