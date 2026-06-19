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
    private ImmutableList<LogTabGroup> _groups = [];
    private IJSObjectReference? _logTabBarModule;
    private ElementReference _logTabBarRootRef;
    private LogTableState _logTableState = null!;
    private IJSObjectReference? _scrollSuppressorModule;
    private List<TabRow> _tabRows = [];

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
        _groups = state.Groups;
        _activeEventLogId = state.ActiveEventLogId;
        _emptyTabIds = ComputeEmptyTabIds(state);
        _tabRows = BuildTabRows(state);
    }

    protected override bool ShouldRender()
    {
        var state = LogTableState.Value;

        if (ReferenceEquals(state, _logTableState)) { return false; }

        bool tablesChanged = !ReferenceEquals(state.EventTables, _eventTables);
        bool groupsChanged = !ReferenceEquals(state.Groups, _groups);
        bool activeChanged = state.ActiveEventLogId != _activeEventLogId;
        var emptyTabIds = ComputeEmptyTabIds(state);
        bool emptinessChanged = !emptyTabIds.SetEquals(_emptyTabIds);

        _logTableState = state;

        if (!tablesChanged && !groupsChanged && !activeChanged && !emptinessChanged) { return false; }

        _eventTables = state.EventTables;
        _groups = state.Groups;
        _activeEventLogId = state.ActiveEventLogId;
        _emptyTabIds = emptyTabIds;

        if (tablesChanged || groupsChanged) { _tabRows = BuildTabRows(state); }

        return true;
    }

    private static List<TabRow> BuildTabRows(LogTableState state)
    {
        var headerGroupIds = new HashSet<LogTabGroupId>();

        foreach (var table in state.EventTables)
        {
            if (table.GroupId is { IsAll: false } groupId) { headerGroupIds.Add(groupId); }
        }

        var memberToGroupId = new Dictionary<EventLogId, LogTabGroupId>();

        foreach (var group in state.Groups)
        {
            if (!headerGroupIds.Contains(group.Id)) { continue; }

            foreach (var memberId in group.MemberIds) { memberToGroupId[memberId] = group.Id; }
        }

        var membersByGroup = new Dictionary<LogTabGroupId, List<LogView>>();
        var standalone = new List<LogView>();

        foreach (var table in state.EventTables)
        {
            if (table.GroupId is not null) { continue; }

            if (memberToGroupId.TryGetValue(table.Id, out var groupId))
            {
                if (!membersByGroup.TryGetValue(groupId, out var members))
                {
                    members = [];
                    membersByGroup[groupId] = members;
                }

                members.Add(table);
            }
            else
            {
                standalone.Add(table);
            }
        }

        var rows = new List<TabRow>();

        if (state.EventTables.FirstOrDefault(table => table.GroupId?.IsAll == true) is { } allLogs)
        {
            rows.Add(new AllLogsRow(allLogs));
        }

        foreach (var header in state.EventTables)
        {
            if (header.GroupId is not { IsAll: false } headerGroupId) { continue; }

            if (state.Groups.FirstOrDefault(candidate => candidate.Id == headerGroupId) is not { } group)
            {
                continue;
            }

            var members = membersByGroup.GetValueOrDefault(group.Id, []);

            rows.Add(new GroupRow(
                header,
                group,
                [.. members.OrderBy(member => member.ComputerName).ThenBy(member => member.LogName)]));
        }

        rows.AddRange(standalone
            .OrderBy(table => table.ComputerName)
            .ThenBy(table => table.LogName)
            .Select(table => new StandaloneRow(table)));

        return rows;
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

    private void CloseGroup(LogTabGroup group) => LogTableCommands.CloseGroup(group.Id);

    private void CloseLog(LogView table) => EventLogCommands.CloseLog(table.Id, table.LogName);

    private string GetActiveTab(LogView table) =>
        LogTableState.Value.ActiveEventLogId == table.Id ? "tab active" : "tab";

    private string GetTabClass(LogView table, bool isMember)
    {
        string active = GetActiveTab(table);

        return isMember ? $"{active} member" : active;
    }

    private string GetTabName(LogView table)
    {
        if (table.GroupId?.IsAll == true) { return "Combined"; }

        if (table.IsCombined) { return table.LogName; }

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

    private void ToggleCollapse(LogTabGroup group) =>
        LogTableCommands.SetTabGroupCollapsed(group.Id, !group.IsCollapsed);

    private IReadOnlyList<LogView> VisibleMembers(GroupRow row) =>
        row.Group.IsCollapsed
            ? [.. row.Members.Where(member => member.Id == _activeEventLogId)]
            : row.Members;

    private sealed record AllLogsRow(LogView Header) : TabRow;

    private sealed record GroupRow(LogView Header, LogTabGroup Group, IReadOnlyList<LogView> Members) : TabRow;

    private sealed record StandaloneRow(LogView Tab) : TabRow;

    private abstract record TabRow;
}
