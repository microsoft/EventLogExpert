// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.LogTable;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EventLogExpert.Components.Sections;

public sealed partial class LogTabBar
{
    private LogTableState _logTableState = null!;

    private List<LogView> _sortedTabs = [];

    [Inject] private IEventLogCommands EventLogCommands { get; init; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private ILogTableCommands LogTableCommands { get; init; } = null!;

    [Inject] private IState<LogTableState> LogTableState { get; init; } = null!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await JSRuntime.InvokeVoidAsync("registerLogTabBarEvents");
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override bool ShouldRender()
    {
        if (ReferenceEquals(LogTableState.Value, _logTableState)) { return false; }

        _logTableState = LogTableState.Value;

        _sortedTabs =
        [
            .. LogTableState.Value.EventTables
                .OrderByDescending(table => table.IsCombined)
                .ThenBy(table => table.ComputerName)
                .ThenBy(table => table.LogName)
        ];

        return true;
    }

    private static string GetTabTooltip(LogView table)
    {
        if (table.IsCombined) { return string.Empty; }

        return table.LogPathType == LogPathType.File
            ? $"Log File: {table.FileName}\n" +
                $"Log Name: {table.LogName}\n" +
                $"Computer Name: {table.ComputerName}"
            : $"Live Log: {table.LogName}\n" +
                $"Computer Name: {table.ComputerName}";
    }

    private void CloseLog(LogView table) => EventLogCommands.CloseLog(table.Id, table.LogName);

    private string GetActiveTab(LogView table) =>
        LogTableState.Value.ActiveEventLogId == table.Id ? "tab active" : "tab";

    private string GetTabName(LogView table)
    {
        if (table.IsCombined) { return "Combined"; }

        string tabName = table.LogPathType is LogPathType.File ?
            Path.GetFileNameWithoutExtension(table.FileName)!.Split("\\").Last() :
            $"{table.LogName} - {table.ComputerName}";

        if (table.IsLoading) { return tabName; }

        int count = _logTableState.EventCountByLog.GetValueOrDefault(table.Id, 0);

        return count <= 0 ? $"(Empty) {tabName}" : tabName;
    }

    private void SetActiveLog(LogView table) =>
        LogTableCommands.SetActiveTable(table.Id);
}
