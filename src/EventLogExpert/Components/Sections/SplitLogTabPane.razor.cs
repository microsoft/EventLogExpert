// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.EventTable;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using CloseLogAction = EventLogExpert.UI.Store.EventLog.CloseLogAction;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Components.Sections;

public sealed partial class SplitLogTabPane
{
    private EventTableState _eventTableState = null!;

    private List<EventTableModel> _sortedTabs = [];

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IState<EventTableState> EventTableState { get; init; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await JSRuntime.InvokeVoidAsync("registerTabPaneEvents");
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override bool ShouldRender()
    {
        if (ReferenceEquals(EventTableState.Value, _eventTableState)) { return false; }

        _eventTableState = EventTableState.Value;

        _sortedTabs =
        [
            .. EventTableState.Value.EventTables
                .OrderByDescending(table => table.IsCombined)
                .ThenBy(table => table.ComputerName)
                .ThenBy(table => table.LogName)
        ];

        return true;
    }

    private static string GetTabTooltip(EventTableModel table)
    {
        if (table.IsCombined) { return string.Empty; }

        return table.LogPathType == LogPathType.File
            ? $"Log File: {table.FileName}\n" +
                $"Log Name: {table.LogName}\n" +
                $"Computer Name: {table.ComputerName}"
            : $"Live Log: {table.LogName}\n" +
                $"Computer Name: {table.ComputerName}";
    }

    private void CloseLog(EventTableModel table) =>
        Dispatcher.Dispatch(new CloseLogAction(table.Id, table.LogName));

    private string GetActiveTab(EventTableModel table) =>
        EventTableState.Value.ActiveEventLogId == table.Id ? "tab active" : "tab";

    private string GetTabName(EventTableModel table)
    {
        if (table.IsCombined) { return "Combined"; }

        string tabName = table.LogPathType is LogPathType.File ?
            Path.GetFileNameWithoutExtension(table.FileName)!.Split("\\").Last() :
            $"{table.LogName} - {table.ComputerName}";

        if (table.IsLoading) { return tabName; }

        int count = _eventTableState.EventCountByLog.GetValueOrDefault(table.Id, 0);

        return count <= 0 ? $"(Empty) {tabName}" : tabName;
    }

    private void SetActiveLog(EventTableModel table) =>
        Dispatcher.Dispatch(new SetActiveTableAction(table.Id));
}
