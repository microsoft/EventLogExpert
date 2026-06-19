// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.UI.Common;
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

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IEventLogCommands EventLogCommands { get; init; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private ILogTableCommands LogTableCommands { get; init; } = null!;

    [Inject] private IState<LogTableState> LogTableState { get; init; } = null!;

    [Inject] private IMenuService MenuService { get; init; } = null!;

    [Inject] private ITraceLogger TraceLogger { get; init; } = null!;

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

    private static MenuItem DangerItem(
        string label,
        Action onClick,
        bool isEnabled = true,
        string? disabledReason = null) =>
        DangerItem(label, () => { onClick(); return Task.CompletedTask; }, isEnabled, disabledReason);

    private static MenuItem DangerItem(
        string label,
        Func<Task> onClickAsync,
        bool isEnabled = true,
        string? disabledReason = null) =>
        MenuItem.Item(label, onClickAsync, isEnabled: isEnabled, isDanger: true, disabledReason: disabledReason);

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

    private IReadOnlyList<MenuItem> BuildAllLogsMenu() =>
    [
        DangerItem("Close all logs", ConfirmCloseAllLogsAsync),
    ];

    private IReadOnlyList<MenuItem> BuildGroupHeaderMenu(LogTabGroup group) =>
    [
        MenuItem.Item("Rename\u2026", () => PromptRenameAsync(group)),
        MenuItem.Item(
            group.IsCollapsed ? "Expand" : "Collapse",
            () => LogTableCommands.SetTabGroupCollapsed(group.Id, !group.IsCollapsed)),
        MenuItem.Separator(),
        DangerItem("Close group", () => LogTableCommands.CloseGroup(group.Id)),
    ];

    private IReadOnlyList<MenuItem> BuildMemberMenu(LogView member, LogTabGroup group)
    {
        bool canCloseOthersInGroup = CanCloseOthersInGroup(group, member.Id);

        return
        [
            MenuItem.SubMenu("Move to group", BuildMoveTargets(member, group.Id)),
            MenuItem.Item("Remove from group", () => LogTableCommands.RemoveTabFromGroup(member.Id)),
            MenuItem.Separator(),
            DangerItem("Close", () => CloseLog(member)),
            DangerItem(
                "Close others in group",
                () => LogTableCommands.CloseOthersInGroup(group.Id, member.Id),
                isEnabled: canCloseOthersInGroup,
                disabledReason: canCloseOthersInGroup ? null : "No other tabs in this group"),
            CloseOtherTabsItem(member.Id),
        ];
    }

    private IReadOnlyList<MenuItem> BuildMoveTargets(LogView tab, LogTabGroupId? excludeGroupId)
    {
        var items = new List<MenuItem>();

        foreach ((LogTabGroupId logTabGroupId, string name, var _) in LogTableState.Value.Groups)
        {
            if (excludeGroupId is { } excluded && logTabGroupId == excluded) { continue; }

            items.Add(MenuItem.Item(name, () => LogTableCommands.MoveTabToGroup(tab.Id, logTabGroupId)));
        }

        if (items.Count > 0) { items.Add(MenuItem.Separator()); }

        items.Add(MenuItem.Item("New group\u2026", () => PromptNewGroupAsync(tab)));

        return items;
    }

    private IReadOnlyList<MenuItem> BuildStandaloneMenu(LogView tab) =>
    [
        MenuItem.Item("New group from tab\u2026", () => PromptNewGroupAsync(tab)),
        MenuItem.SubMenu("Move to group", BuildMoveTargets(tab, excludeGroupId: null)),
        MenuItem.Separator(),
        DangerItem("Close", () => CloseLog(tab)),
        CloseOtherTabsItem(tab.Id),
    ];

    private bool CanCloseOthersInGroup(LogTabGroup group, EventLogId keepTabId) =>
        group.MemberIds.Contains(keepTabId) &&
        LogTableState.Value.EventTables.Count(tab => tab.GroupId is null && group.MemberIds.Contains(tab.Id)) > 1;

    private bool CanCloseOtherTabs() => LogTableState.Value.EventTables.Count(tab => !tab.IsCombined) > 1;

    private void CloseGroup(LogTabGroup group) => LogTableCommands.CloseGroup(group.Id);

    private void CloseLog(LogView table)
    {
        if (LogTableState.Value.EventTables.All(tab => tab.Id != table.Id)) { return; }

        EventLogCommands.CloseLog(table.Id, table.LogName);
    }

    private MenuItem CloseOtherTabsItem(EventLogId tabId)
    {
        bool canClose = CanCloseOtherTabs();

        return DangerItem(
            "Close other tabs",
            () => LogTableCommands.CloseAllButThis(tabId),
            isEnabled: canClose,
            disabledReason: canClose ? null : "No other tabs to close");
    }

    private async Task ConfirmCloseAllLogsAsync()
    {
        if (await CloseAllLogsConfirmation.ConfirmAsync(AlertDialogService))
        {
            EventLogCommands.CloseAllLogs();
        }
    }

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

    private void OnCloseGroupKeyDown(KeyboardEventArgs e, LogTabGroup group)
    {
        if (e.Key != "Enter" && e.Key != " ") { return; }

        CloseGroup(group);
    }

    private void OnCloseLogKeyDown(KeyboardEventArgs e, LogView table)
    {
        if (e.Key != "Enter" && e.Key != " ") { return; }

        CloseLog(table);
    }

    private void OnCloseLogMouseDown(MouseEventArgs e, LogView table)
    {
        if (e.Button != 0) { return; }

        CloseLog(table);
    }

    private void OnTabKeyDown(KeyboardEventArgs e, LogView table)
    {
        if (e.Key != "Enter" && e.Key != " ") { return; }

        SetActiveLog(table);
    }

    private void OnTabMouseDown(MouseEventArgs e, LogView table)
    {
        if (e.Button != 0) { return; }

        SetActiveLog(table);
    }

    private void OpenAllLogsMenu(MouseEventArgs args) =>
        MenuService.OpenAt(args.ClientX, args.ClientY, BuildAllLogsMenu());

    private void OpenGroupHeaderMenu(MouseEventArgs args, LogTabGroup group) =>
        MenuService.OpenAt(args.ClientX, args.ClientY, BuildGroupHeaderMenu(group));

    private void OpenMemberMenu(MouseEventArgs args, LogView member, LogTabGroup group) =>
        MenuService.OpenAt(args.ClientX, args.ClientY, BuildMemberMenu(member, group));

    private void OpenStandaloneMenu(MouseEventArgs args, LogView tab) =>
        MenuService.OpenAt(args.ClientX, args.ClientY, BuildStandaloneMenu(tab));

    private async Task PromptNewGroupAsync(LogView tab)
    {
        string name = await AlertDialogService.DisplayPrompt(
            "New group",
            "Group name:",
            string.Empty,
            candidate => string.IsNullOrWhiteSpace(candidate) ? "Group name is required." : null);

        if (string.IsNullOrWhiteSpace(name)) { return; }

        if (!LogTableState.Value.EventTables.Any(table => table.Id == tab.Id && table.GroupId is null))
        {
            TraceLogger.Trace($"New group skipped: tab '{tab.LogName}' is no longer an open per-log tab.");
            return;
        }

        LogTableCommands.NewGroupFromTab(tab.Id, name.Trim());
    }

    private async Task PromptRenameAsync(LogTabGroup group)
    {
        string name = await AlertDialogService.DisplayPrompt(
            "Rename group",
            "Group name:",
            group.Name,
            candidate => string.IsNullOrWhiteSpace(candidate) ? "Group name is required." : null);

        if (string.IsNullOrWhiteSpace(name)) { return; }

        if (LogTableState.Value.Groups.All(candidate => candidate.Id != group.Id))
        {
            TraceLogger.Trace($"Rename skipped: group '{group.Name}' no longer exists.");
            return;
        }

        LogTableCommands.RenameGroup(group.Id, name.Trim());
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
