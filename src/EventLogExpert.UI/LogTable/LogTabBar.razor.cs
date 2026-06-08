// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.UI.Common.Interop;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace EventLogExpert.UI.LogTable;

public sealed partial class LogTabBar
{
    private IJSObjectReference? _logTabBarModule;
    private LogTableState _logTableState = null!;
    private List<LogView> _sortedTabs = [];

    [Inject] private IEventLogCommands EventLogCommands { get; init; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private ILogTableCommands LogTableCommands { get; init; } = null!;

    [Inject] private IState<LogTableState> LogTableState { get; init; } = null!;

    protected override async ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing)
        {
            await JsModuleInterop.DisposeModuleSafelyAsync(_logTabBarModule);

            _logTabBarModule = null;
        }

        await base.DisposeAsyncCore(disposing);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                _logTabBarModule = await JSRuntime.InvokeAsync<IJSObjectReference>(
                    "import",
                    "./_content/EventLogExpert.UI/LogTable/LogTabBar.razor.js");

                await _logTabBarModule.InvokeVoidAsync("registerLogTabBarEvents");
            }
            catch (JSDisconnectedException) { }
            catch (JSException) { }
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

    private void OnTabKeyDown(KeyboardEventArgs e, LogView table)
    {
        if (e.Key != "Enter" && e.Key != " ") { return; }

        SetActiveLog(table);
    }

    private void SetActiveLog(LogView table)
    {
        if (table.IsLoading) { return; }

        LogTableCommands.SetActiveTable(table.Id);
    }
}
