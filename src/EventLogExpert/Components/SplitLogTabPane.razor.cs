// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.EventTable;
using Fluxor;
using Microsoft.AspNetCore.Components;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Components;

public sealed partial class SplitLogTabPane
{
    [Inject] private IState<EventTableState> EventTableState { get; init; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    private static string GetTabName(EventTableModel table) =>
        table.LogType is LogType.File ?
            Path.GetFileNameWithoutExtension(table.FileName)!.Split("\\").Last() :
            $"{table.LogName} - {table.ComputerName}";

    private static string GetTabTooltip(EventTableModel table) =>
        $"{(table.LogType == LogType.File ? "Log File: " : "Live Log: ")} {table.FileName}\n" +
        $"Log Name: {table.LogName}\n" +
        $"Computer Name: {table.ComputerName}";

    private string GetTabWidth()
    {
        var logCount = EventTableState.Value.EventTables.Count;

        return logCount > 4 ? $"{100 / (logCount + 1)}vw" : "20vw";
    }

    private void SetActiveLog(EventTableModel table)
    {
        if (table.IsLoading) { return; }

        Dispatcher.Dispatch(new EventTableAction.SetActiveTable(table.Id));
    }
}
