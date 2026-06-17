// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.UI.Common.Interop;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Collections.Immutable;

namespace EventLogExpert.UI.LogTable;

public sealed partial class LogTabBar
{
    private EventLogId? _activeEventLogId;
    private HashSet<EventLogId> _emptyTabIds = [];
    private ImmutableList<LogView>? _eventTables;
    private IJSObjectReference? _logTabBarModule;
    private ElementReference _logTabBarRootRef;
    private LogTableState _logTableState = null!;
    private IJSObjectReference? _scrollSuppressorModule;
    private List<LogView> _sortedTabs = [];

    [Inject] private IEventLogCommands EventLogCommands { get; init; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private ILogTableCommands LogTableCommands { get; init; } = null!;

    [Inject] private IState<LogTableState> LogTableState { get; init; } = null!;

    protected override async ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing)
        {
            await JsModuleInterop.DisposeModuleSafelyAsync(
                _scrollSuppressorModule,
                module => module.InvokeVoidAsync("release", _logTabBarRootRef));

            await JsModuleInterop.DisposeModuleSafelyAsync(_logTabBarModule);

            _logTabBarModule = null;
            _scrollSuppressorModule = null;
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

                _scrollSuppressorModule = await JSRuntime.InvokeAsync<IJSObjectReference>(
                    "import",
                    "./_content/EventLogExpert.UI/Common/keyboardScrollSuppressor.js");

                await _scrollSuppressorModule.InvokeVoidAsync(
                    "suppress",
                    _logTabBarRootRef,
                    new[] { new { selector = "[role='button']", keys = new[] { "Enter", " " } } });
            }
            catch (JSDisconnectedException) { }
            catch (JSException) { }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        var state = LogTableState.Value;
        _logTableState = state;
        _eventTables = state.EventTables;
        _activeEventLogId = state.ActiveEventLogId;
        _emptyTabIds = ComputeEmptyTabIds(state);
        _sortedTabs = SortTabs(state.EventTables);
    }

    protected override bool ShouldRender()
    {
        var state = LogTableState.Value;

        if (ReferenceEquals(state, _logTableState)) { return false; }

        bool tablesChanged = !ReferenceEquals(state.EventTables, _eventTables);
        bool activeChanged = state.ActiveEventLogId != _activeEventLogId;
        var emptyTabIds = ComputeEmptyTabIds(state);
        bool emptinessChanged = !emptyTabIds.SetEquals(_emptyTabIds);

        _logTableState = state;

        if (!tablesChanged && !activeChanged && !emptinessChanged) { return false; }

        _eventTables = state.EventTables;
        _activeEventLogId = state.ActiveEventLogId;
        _emptyTabIds = emptyTabIds;

        if (tablesChanged) { _sortedTabs = SortTabs(state.EventTables); }

        return true;
    }

    private static HashSet<EventLogId> ComputeEmptyTabIds(LogTableState state)
    {
        var empty = new HashSet<EventLogId>();

        foreach (var table in state.EventTables)
        {
            if (table.IsCombined || table.IsLoading) { continue; }

            if (state.EventCountByLog.GetValueOrDefault(table.Id, 0) <= 0) { empty.Add(table.Id); }
        }

        return empty;
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

    private static List<LogView> SortTabs(ImmutableList<LogView> tables) =>
    [
        .. tables
            .OrderByDescending(table => table.IsCombined)
            .ThenBy(table => table.ComputerName)
            .ThenBy(table => table.LogName)
    ];

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
