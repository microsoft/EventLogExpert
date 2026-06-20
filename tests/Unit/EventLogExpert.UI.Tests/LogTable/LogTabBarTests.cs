// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.UI.LogTable;
using Fluxor;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.LogTable;

public sealed class LogTabBarTests : BunitContext
{
    private readonly IAlertDialogService _alertDialogService = Substitute.For<IAlertDialogService>();
    private readonly IEventLogCommands _eventLogCommands = Substitute.For<IEventLogCommands>();
    private readonly ILogTableCommands _logTableCommands = Substitute.For<ILogTableCommands>();
    private readonly IState<LogTableState> _logTableState = Substitute.For<IState<LogTableState>>();
    private readonly IMenuService _menuService = Substitute.For<IMenuService>();
    private readonly ITraceLogger _traceLogger = Substitute.For<ITraceLogger>();

    private IReadOnlyList<MenuItem>? _capturedMenu;

    public LogTabBarTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./_content/EventLogExpert.UI/LogTable/LogTabBar.razor.js");

        _menuService
            .When(menu => menu.OpenAt(
                Arg.Any<double>(),
                Arg.Any<double>(),
                Arg.Any<IReadOnlyList<MenuItem>>(),
                Arg.Any<bool>(),
                Arg.Any<bool>()))
            .Do(call => _capturedMenu = call.Arg<IReadOnlyList<MenuItem>>());

        Services.AddSingleton(_alertDialogService);
        Services.AddSingleton(_eventLogCommands);
        Services.AddSingleton(_logTableCommands);
        Services.AddSingleton(_logTableState);
        Services.AddSingleton(_menuService);
        Services.AddSingleton(_traceLogger);
        Services.AddFluxor(options => options.ScanAssemblies(typeof(LogTabBar).Assembly));
    }

    [Fact]
    public async Task ActiveTabChange_Rerenders()
    {
        var alpha = EventLogId.Create();
        var beta = EventLogId.Create();
        var state1 = TwoTabState(alpha, beta, alphaCount: 1, betaCount: 1);
        _logTableState.Value.Returns(state1);
        var cut = Render<LogTabBar>();
        int before = cut.RenderCount;

        var state2 = state1 with { ActiveEventLogId = beta };
        await RaiseStateChange(cut, state2);

        Assert.True(cut.RenderCount > before);
    }

    [Fact]
    public async Task AllLogsHeader_CloseAllLogs_ConfirmAccepted_Dispatches()
    {
        var allLogsId = EventLogId.Create();
        var logId = EventLogId.Create();
        _logTableState.Value.Returns(AllLogsState(allLogsId, logId, "Alpha"));
        _alertDialogService.ShowAlert("Close all logs", Arg.Any<string>(), "Close all", "Cancel").Returns(true);
        var cut = Render<LogTabBar>();
        var menu = OpenContextMenu(cut, ".tab");

        await InvokeMenuItemAsync(cut, FindItem(menu, "Close all logs"));

        _eventLogCommands.Received(1).CloseAllLogs();
    }

    [Fact]
    public async Task AllLogsHeader_CloseAllLogs_ConfirmCancelled_DoesNotDispatch()
    {
        var allLogsId = EventLogId.Create();
        var logId = EventLogId.Create();
        _logTableState.Value.Returns(AllLogsState(allLogsId, logId, "Alpha"));
        _alertDialogService.ShowAlert("Close all logs", Arg.Any<string>(), "Close all", "Cancel").Returns(false);
        var cut = Render<LogTabBar>();
        var menu = OpenContextMenu(cut, ".tab");

        await InvokeMenuItemAsync(cut, FindItem(menu, "Close all logs"));

        _eventLogCommands.DidNotReceive().CloseAllLogs();
    }

    [Fact]
    public void ChevronClick_DispatchesSetTabGroupCollapsed()
    {
        var (state, groupId, _, _, _) = GroupedState(collapsed: false, activeIsMember1: false);
        _logTableState.Value.Returns(state);
        var cut = Render<LogTabBar>();

        cut.Find("button.chevron").Click();

        _logTableCommands.Received(1).SetTabGroupCollapsed(groupId, true);
    }

    [Fact]
    public async Task CloseMenuItem_StaleTab_DoesNotDispatchCloseLog()
    {
        var alpha = EventLogId.Create();
        var beta = EventLogId.Create();
        _logTableState.Value.Returns(TwoTabState(alpha, beta, alphaCount: 1, betaCount: 1));
        var cut = Render<LogTabBar>();
        var menu = OpenContextMenu(cut, ".tab");

        // Alpha is gone by the time the captured "Close" action runs.
        _logTableState.Value.Returns(TwoTabState(beta, EventLogId.Create(), alphaCount: 1, betaCount: 1));

        await InvokeMenuItemAsync(cut, FindItem(menu, "Close"));

        _eventLogCommands.DidNotReceive().CloseLog(Arg.Any<EventLogId>(), Arg.Any<string>());
    }

    [Fact]
    public void CloseOthersInGroup_DisabledForSingleMember()
    {
        var (state, _, _) = SingleMemberGroupState();
        _logTableState.Value.Returns(state);
        var cut = Render<LogTabBar>();
        var menu = OpenContextMenu(cut, ".tab.member");

        var item = FindItem(menu, "Close others in group");
        Assert.False(item.IsEnabled);
        Assert.Equal("No other tabs in this group", item.DisabledReason);
    }

    [Fact]
    public void CloseOtherTabs_DisabledWhenNoOtherPerLogTabs()
    {
        var allLogsId = EventLogId.Create();
        var logId = EventLogId.Create();
        _logTableState.Value.Returns(AllLogsState(allLogsId, logId, "Alpha"));
        var cut = Render<LogTabBar>();
        var menu = OpenContextMenuByIndex(cut, 1);

        var item = FindItem(menu, "Close other tabs");
        Assert.False(item.IsEnabled);
        Assert.Equal("No other tabs to close", item.DisabledReason);
    }

    [Fact]
    public void CollapsedGroup_HidesInactiveMembers()
    {
        var (state, _, _, _, _) = GroupedState(collapsed: true, activeIsMember1: true);
        _logTableState.Value.Returns(state);

        var cut = Render<LogTabBar>();

        Assert.Contains("Alpha", cut.Markup);
        Assert.DoesNotContain("Beta", cut.Markup);
    }

    [Fact]
    public void CollapsedGroup_RendersDownChevron()
    {
        var (state, _, _, _, _) = GroupedState(collapsed: true, activeIsMember1: false);
        _logTableState.Value.Returns(state);

        var cut = Render<LogTabBar>();

        Assert.Contains("bi-chevron-down", cut.Markup);
    }

    [Fact]
    public async Task CollapseOnlyChange_Rerenders()
    {
        var (state1, _, _, _, _) = GroupedState(collapsed: false, activeIsMember1: false);
        _logTableState.Value.Returns(state1);
        var cut = Render<LogTabBar>();
        int before = cut.RenderCount;

        var state2 = state1 with
        {
            Groups = state1.Groups.SetItem(0, state1.Groups[0] with { IsCollapsed = true })
        };
        await RaiseStateChange(cut, state2);

        Assert.True(cut.RenderCount > before);
    }

    [Fact]
    public void CombinedHeader_RendersCombinedLabel()
    {
        var allLogsId = EventLogId.Create();
        var logId = EventLogId.Create();
        var state = new LogTableState
        {
            ActiveEventLogId = allLogsId,
            EventTables = ImmutableList.Create(
                new LogView(allLogsId) { GroupId = LogTabGroupId.AllLogs },
                new LogView(logId) { LogName = "Alpha" }),
            EventCountByLog = ImmutableDictionary<EventLogId, int>.Empty.Add(logId, 1)
        };
        _logTableState.Value.Returns(state);

        var cut = Render<LogTabBar>();

        Assert.Contains("Combined", cut.Markup);
    }

    [Fact]
    public async Task EmptinessSwapSameTotal_Rerenders()
    {
        var alpha = EventLogId.Create();
        var beta = EventLogId.Create();
        var state1 = TwoTabState(alpha, beta, alphaCount: 5, betaCount: 0);
        _logTableState.Value.Returns(state1);
        var cut = Render<LogTabBar>();
        int before = cut.RenderCount;

        var state2 = state1 with
        {
            EventCountByLog = ImmutableDictionary<EventLogId, int>.Empty.Add(alpha, 0).Add(beta, 5)
        };
        await RaiseStateChange(cut, state2);

        Assert.True(cut.RenderCount > before);
    }

    [Fact]
    public async Task EmptyToNonEmpty_Rerenders()
    {
        var alpha = EventLogId.Create();
        var beta = EventLogId.Create();
        var state1 = TwoTabState(alpha, beta, alphaCount: 0, betaCount: 1);
        _logTableState.Value.Returns(state1);
        var cut = Render<LogTabBar>();
        int before = cut.RenderCount;

        var state2 = state1 with { EventCountByLog = state1.EventCountByLog.SetItem(alpha, 1) };
        await RaiseStateChange(cut, state2);

        Assert.True(cut.RenderCount > before);
    }

    [Fact]
    public async Task EventTablesChange_Rerenders()
    {
        var alpha = EventLogId.Create();
        var beta = EventLogId.Create();
        var gamma = EventLogId.Create();
        var state1 = TwoTabState(alpha, beta, alphaCount: 1, betaCount: 1);
        _logTableState.Value.Returns(state1);
        var cut = Render<LogTabBar>();
        int before = cut.RenderCount;

        var state2 = state1 with
        {
            EventTables = state1.EventTables.Add(new LogView(gamma) { LogName = "Gamma" }),
            EventCountByLog = state1.EventCountByLog.Add(gamma, 1)
        };
        await RaiseStateChange(cut, state2);

        Assert.True(cut.RenderCount > before);
        Assert.Contains("Gamma", cut.Markup);
    }

    [Fact]
    public void ExpandedGroup_RendersHeaderNameAndMembers()
    {
        var (state, _, _, _, _) = GroupedState(collapsed: false, activeIsMember1: false);
        _logTableState.Value.Returns(state);

        var cut = Render<LogTabBar>();

        Assert.Contains("MyGroup", cut.Markup);
        Assert.Contains("Alpha", cut.Markup);
        Assert.Contains("Beta", cut.Markup);
    }

    [Fact]
    public void ExpandedGroup_RendersRightChevron()
    {
        var (state, _, _, _, _) = GroupedState(collapsed: false, activeIsMember1: false);
        _logTableState.Value.Returns(state);

        var cut = Render<LogTabBar>();

        Assert.Contains("bi-chevron-right", cut.Markup);
    }

    [Fact]
    public void FirstRender_WithPopulatedState_ShowsTabs()
    {
        var alpha = EventLogId.Create();
        var beta = EventLogId.Create();
        _logTableState.Value.Returns(TwoTabState(alpha, beta, alphaCount: 1, betaCount: 1));

        var cut = Render<LogTabBar>();

        Assert.Contains("Alpha", cut.Markup);
        Assert.Contains("Beta", cut.Markup);
    }

    [Fact]
    public void GroupClose_DispatchesCloseGroup()
    {
        var (state, groupId, _, _, _) = GroupedState(collapsed: false, activeIsMember1: false);
        _logTableState.Value.Returns(state);
        var cut = Render<LogTabBar>();

        cut.Find(".group-header > i.bi-x").Click();

        _logTableCommands.Received(1).CloseGroup(groupId);
    }

    [Fact]
    public async Task GroupHeader_Collapse_DispatchesSetTabGroupCollapsed()
    {
        var (state, groupId, _, _, _) = GroupedState(collapsed: false, activeIsMember1: false);
        _logTableState.Value.Returns(state);
        var cut = Render<LogTabBar>();
        var menu = OpenContextMenu(cut, ".group-header");

        await InvokeMenuItemAsync(cut, FindItem(menu, "Collapse"));

        _logTableCommands.Received(1).SetTabGroupCollapsed(groupId, true);
    }

    [Fact]
    public async Task GroupHeader_Rename_PromptThenDispatches()
    {
        var (state, groupId, _, _, _) = GroupedState(collapsed: false, activeIsMember1: false);
        _logTableState.Value.Returns(state);
        _alertDialogService
            .DisplayPrompt("Rename group", Arg.Any<string>(), "MyGroup", Arg.Any<Func<string, string?>?>())
            .Returns("Renamed");
        var cut = Render<LogTabBar>();
        var menu = OpenContextMenu(cut, ".group-header");

        await InvokeMenuItemAsync(cut, FindItem(menu, "Rename\u2026"));

        _logTableCommands.Received(1).RenameGroup(groupId, "Renamed");
    }

    [Fact]
    public void GroupHeader_RightClick_ShowsExpectedItems()
    {
        var (state, _, _, _, _) = GroupedState(collapsed: false, activeIsMember1: false);
        _logTableState.Value.Returns(state);
        var cut = Render<LogTabBar>();

        var menu = OpenContextMenu(cut, ".group-header");

        var labels = menu.Select(item => item.Label).ToList();
        Assert.Equal(["Rename\u2026", "Collapse", string.Empty, "Close group"], labels);
    }

    [Fact]
    public void GroupHeaderName_MouseDown_DispatchesSetActiveTable()
    {
        var (state, _, headerId, _, _) = GroupedState(collapsed: false, activeIsMember1: false);
        _logTableState.Value.Returns(state);
        var cut = Render<LogTabBar>();

        cut.Find(".group-header > span").MouseDown();

        _logTableCommands.Received(1).SetActiveTable(headerId);
    }

    [Fact]
    public async Task MemberTab_CloseOthersInGroup_Dispatches()
    {
        var (state, groupId, _, member1, _) = GroupedState(collapsed: false, activeIsMember1: false);
        _logTableState.Value.Returns(state);
        var cut = Render<LogTabBar>();
        var menu = OpenContextMenu(cut, ".tab.member");

        await InvokeMenuItemAsync(cut, FindItem(menu, "Close others in group"));

        _logTableCommands.Received(1).CloseOthersInGroup(groupId, member1);
    }

    [Fact]
    public void MemberTab_LeftMouseDown_ActivatesMember()
    {
        var (state, _, _, member1, _) = GroupedState(collapsed: false, activeIsMember1: false);
        _logTableState.Value.Returns(state);
        var cut = Render<LogTabBar>();

        cut.Find(".tab.member > span").MouseDown();

        _logTableCommands.Received(1).SetActiveTable(member1);
    }

    [Fact]
    public async Task MemberTab_MoveSubmenu_ExcludesOwnGroupAndKeepsNewGroup()
    {
        var (state, _, _, member1, _) = GroupedState(collapsed: false, activeIsMember1: false);
        _logTableState.Value.Returns(state);
        _alertDialogService
            .DisplayPrompt("New group", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Func<string, string?>?>())
            .Returns("Split");
        var cut = Render<LogTabBar>();
        var menu = OpenContextMenu(cut, ".tab.member");

        var moveTo = FindItem(menu, "Move to group");
        Assert.NotNull(moveTo.Children);
        Assert.Equal(["New group\u2026"], moveTo.Children!.Select(item => item.Label).ToList());

        await InvokeMenuItemAsync(cut, FindItem(moveTo.Children!, "New group\u2026"));

        _logTableCommands.Received(1).NewGroupFromTab(member1, "Split");
    }

    [Fact]
    public async Task MemberTab_RemoveFromGroup_Dispatches()
    {
        var (state, _, _, member1, _) = GroupedState(collapsed: false, activeIsMember1: false);
        _logTableState.Value.Returns(state);
        var cut = Render<LogTabBar>();
        var menu = OpenContextMenu(cut, ".tab.member");

        await InvokeMenuItemAsync(cut, FindItem(menu, "Remove from group"));

        _logTableCommands.Received(1).RemoveTabFromGroup(member1);
    }

    [Fact]
    public void MemberTab_RightClick_ShowsExpectedItems()
    {
        var (state, _, _, _, _) = GroupedState(collapsed: false, activeIsMember1: false);
        _logTableState.Value.Returns(state);
        var cut = Render<LogTabBar>();

        var menu = OpenContextMenu(cut, ".tab.member");

        var labels = menu.Select(item => item.Label).ToList();
        Assert.Equal(
            ["Move to group", "Remove from group", string.Empty, "Close", "Close others in group", "Close other tabs"],
            labels);
    }

    [Fact]
    public async Task NonEmptyTabCountIncrement_DoesNotRerender()
    {
        var alpha = EventLogId.Create();
        var beta = EventLogId.Create();
        var state1 = TwoTabState(alpha, beta, alphaCount: 5, betaCount: 3);
        _logTableState.Value.Returns(state1);
        var cut = Render<LogTabBar>();
        int before = cut.RenderCount;

        var state2 = state1 with { EventCountByLog = state1.EventCountByLog.SetItem(alpha, 6) };
        await RaiseStateChange(cut, state2);

        Assert.Equal(before, cut.RenderCount);
    }

    [Fact]
    public void RightClickMouseDown_DoesNotActivateTab()
    {
        var alpha = EventLogId.Create();
        var beta = EventLogId.Create();
        _logTableState.Value.Returns(TwoTabState(alpha, beta, alphaCount: 1, betaCount: 1));
        var cut = Render<LogTabBar>();

        cut.Find(".tab > span").MouseDown(new MouseEventArgs { Button = 2 });

        _logTableCommands.DidNotReceive().SetActiveTable(Arg.Any<EventLogId>());
    }

    [Fact]
    public void RightClickMouseDown_OnCloseIcon_DoesNotCloseLog()
    {
        var alpha = EventLogId.Create();
        var beta = EventLogId.Create();
        _logTableState.Value.Returns(TwoTabState(alpha, beta, alphaCount: 1, betaCount: 1));
        var cut = Render<LogTabBar>();

        cut.Find(".tab > i.bi-x").MouseDown(new MouseEventArgs { Button = 2 });

        _eventLogCommands.DidNotReceive().CloseLog(Arg.Any<EventLogId>(), Arg.Any<string>());
    }

    [Fact]
    public async Task StandaloneTab_BlankGroupName_DoesNotDispatch()
    {
        var alpha = EventLogId.Create();
        var beta = EventLogId.Create();
        _logTableState.Value.Returns(TwoTabState(alpha, beta, alphaCount: 1, betaCount: 1));
        _alertDialogService
            .DisplayPrompt("New group", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Func<string, string?>?>())
            .Returns("   ");
        var cut = Render<LogTabBar>();
        var menu = OpenContextMenu(cut, ".tab");

        await InvokeMenuItemAsync(cut, FindItem(menu, "New group from tab\u2026"));

        _logTableCommands.DidNotReceive().NewGroupFromTab(Arg.Any<EventLogId>(), Arg.Any<string>());
    }

    [Fact]
    public async Task StandaloneTab_Close_DispatchesCloseLog()
    {
        var alpha = EventLogId.Create();
        var beta = EventLogId.Create();
        _logTableState.Value.Returns(TwoTabState(alpha, beta, alphaCount: 1, betaCount: 1));
        var cut = Render<LogTabBar>();
        var menu = OpenContextMenu(cut, ".tab");

        await InvokeMenuItemAsync(cut, FindItem(menu, "Close"));

        _eventLogCommands.Received(1).CloseLog(alpha, "Alpha");
    }

    [Fact]
    public async Task StandaloneTab_CloseOtherTabs_DispatchesCloseAllButThis()
    {
        var alpha = EventLogId.Create();
        var beta = EventLogId.Create();
        _logTableState.Value.Returns(TwoTabState(alpha, beta, alphaCount: 1, betaCount: 1));
        var cut = Render<LogTabBar>();
        var menu = OpenContextMenu(cut, ".tab");

        await InvokeMenuItemAsync(cut, FindItem(menu, "Close other tabs"));

        _logTableCommands.Received(1).CloseAllButThis(alpha);
    }

    [Fact]
    public async Task StandaloneTab_MoveToExistingGroup_Dispatches()
    {
        var (state, groupId, standalone) = GroupPlusStandaloneState();
        _logTableState.Value.Returns(state);
        var cut = Render<LogTabBar>();
        var menu = OpenContextMenuByIndex(cut, 2);

        var moveTo = FindItem(menu, "Move to group");
        Assert.NotNull(moveTo.Children);
        await InvokeMenuItemAsync(cut, FindItem(moveTo.Children!, "MyGroup"));

        _logTableCommands.Received(1).MoveTabToGroup(standalone, groupId);
    }

    [Fact]
    public async Task StandaloneTab_NewGroup_StaleTab_DoesNotDispatch()
    {
        var alpha = EventLogId.Create();
        var beta = EventLogId.Create();
        _logTableState.Value.Returns(TwoTabState(alpha, beta, alphaCount: 1, betaCount: 1));
        _alertDialogService
            .DisplayPrompt("New group", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Func<string, string?>?>())
            .Returns("Diagnostics");
        var cut = Render<LogTabBar>();
        var menu = OpenContextMenu(cut, ".tab");

        // Alpha is closed while the prompt is open -> the captured menu action targets a tab no longer present.
        _logTableState.Value.Returns(TwoTabState(beta, EventLogId.Create(), alphaCount: 1, betaCount: 1));

        await InvokeMenuItemAsync(cut, FindItem(menu, "New group from tab\u2026"));

        _logTableCommands.DidNotReceive().NewGroupFromTab(Arg.Any<EventLogId>(), Arg.Any<string>());
    }

    [Fact]
    public async Task StandaloneTab_NewGroupFromTab_PromptThenDispatches()
    {
        var alpha = EventLogId.Create();
        var beta = EventLogId.Create();
        _logTableState.Value.Returns(TwoTabState(alpha, beta, alphaCount: 1, betaCount: 1));
        _alertDialogService
            .DisplayPrompt("New group", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Func<string, string?>?>())
            .Returns("Diagnostics");
        var cut = Render<LogTabBar>();
        var menu = OpenContextMenu(cut, ".tab");

        await InvokeMenuItemAsync(cut, FindItem(menu, "New group from tab\u2026"));

        _logTableCommands.Received(1).NewGroupFromTab(alpha, "Diagnostics");
    }

    [Fact]
    public void StandaloneTab_RightClick_ShowsExpectedItems()
    {
        var alpha = EventLogId.Create();
        var beta = EventLogId.Create();
        _logTableState.Value.Returns(TwoTabState(alpha, beta, alphaCount: 1, betaCount: 1));
        var cut = Render<LogTabBar>();

        var menu = OpenContextMenu(cut, ".tab");

        var labels = menu.Select(item => item.Label).ToList();
        Assert.Equal(["New group from tab\u2026", "Move to group", string.Empty, "Close", "Close other tabs"], labels);
    }

    [Fact]
    public async Task UnrelatedFieldChange_DoesNotRerender()
    {
        var alpha = EventLogId.Create();
        var beta = EventLogId.Create();
        var state1 = TwoTabState(alpha, beta, alphaCount: 1, betaCount: 1);
        _logTableState.Value.Returns(state1);
        var cut = Render<LogTabBar>();
        int before = cut.RenderCount;

        var state2 = state1 with { OrderBy = ColumnName.Source, IsDescending = false };
        await RaiseStateChange(cut, state2);

        Assert.Equal(before, cut.RenderCount);
    }

    private static LogTableState AllLogsState(EventLogId allLogsId, EventLogId logId, string logName) =>
        new()
        {
            ActiveEventLogId = allLogsId,
            EventTables = ImmutableList.Create(
                new LogView(allLogsId) { GroupId = LogTabGroupId.AllLogs },
                new LogView(logId) { LogName = logName }),
            EventCountByLog = ImmutableDictionary<EventLogId, int>.Empty.Add(logId, 1)
        };

    private static MenuItem FindItem(IEnumerable<MenuItem> items, string label) =>
        items.First(item => item.Label == label);

    private static (LogTableState State, LogTabGroupId GroupId, EventLogId HeaderId, EventLogId Member1, EventLogId Member2)
        GroupedState(bool collapsed, bool activeIsMember1)
    {
        var groupId = LogTabGroupId.Create();
        var headerId = EventLogId.Create();
        var member1 = EventLogId.Create();
        var member2 = EventLogId.Create();

        var state = new LogTableState
        {
            ActiveEventLogId = activeIsMember1 ? member1 : headerId,
            EventTables = ImmutableList.Create(
                new LogView(headerId) { GroupId = groupId, LogName = "MyGroup" },
                new LogView(member1) { LogName = "Alpha" },
                new LogView(member2) { LogName = "Beta" }),
            Groups = ImmutableList.Create(
                new LogTabGroup(groupId, "MyGroup", ImmutableHashSet.Create(member1, member2))
                {
                    IsCollapsed = collapsed
                }),
            EventCountByLog = ImmutableDictionary<EventLogId, int>.Empty.Add(member1, 1).Add(member2, 1)
        };

        return (state, groupId, headerId, member1, member2);
    }

    private static (LogTableState State, LogTabGroupId GroupId, EventLogId Standalone) GroupPlusStandaloneState()
    {
        var groupId = LogTabGroupId.Create();
        var headerId = EventLogId.Create();
        var member = EventLogId.Create();
        var standalone = EventLogId.Create();

        var state = new LogTableState
        {
            ActiveEventLogId = standalone,
            EventTables = ImmutableList.Create(
                new LogView(headerId) { GroupId = groupId, LogName = "MyGroup" },
                new LogView(member) { LogName = "Alpha" },
                new LogView(standalone) { LogName = "Gamma" }),
            Groups = ImmutableList.Create(
                new LogTabGroup(groupId, "MyGroup", ImmutableHashSet.Create(member))),
            EventCountByLog = ImmutableDictionary<EventLogId, int>.Empty.Add(member, 1).Add(standalone, 1)
        };

        return (state, groupId, standalone);
    }

    private static (LogTableState State, LogTabGroupId GroupId, EventLogId Member) SingleMemberGroupState()
    {
        var groupId = LogTabGroupId.Create();
        var headerId = EventLogId.Create();
        var member = EventLogId.Create();

        var state = new LogTableState
        {
            ActiveEventLogId = headerId,
            EventTables = ImmutableList.Create(
                new LogView(headerId) { GroupId = groupId, LogName = "Solo" },
                new LogView(member) { LogName = "OnlyMember" }),
            Groups = ImmutableList.Create(
                new LogTabGroup(groupId, "Solo", ImmutableHashSet.Create(member))),
            EventCountByLog = ImmutableDictionary<EventLogId, int>.Empty.Add(member, 1)
        };

        return (state, groupId, member);
    }

    private static LogTableState TwoTabState(EventLogId alpha, EventLogId beta, int alphaCount, int betaCount) =>
        new()
        {
            ActiveEventLogId = alpha,
            EventTables = ImmutableList.Create(
                new LogView(alpha) { LogName = "Alpha" },
                new LogView(beta) { LogName = "Beta" }),
            EventCountByLog = ImmutableDictionary<EventLogId, int>.Empty.Add(alpha, alphaCount).Add(beta, betaCount)
        };

    private async Task InvokeMenuItemAsync(IRenderedComponent<LogTabBar> cut, MenuItem item) =>
        await cut.InvokeAsync(() => item.OnClickAsync!());

    private IReadOnlyList<MenuItem> OpenContextMenu(IRenderedComponent<LogTabBar> cut, string selector)
    {
        _capturedMenu = null;
        cut.Find(selector).ContextMenu();
        Assert.NotNull(_capturedMenu);
        return _capturedMenu!;
    }

    private IReadOnlyList<MenuItem> OpenContextMenuByIndex(IRenderedComponent<LogTabBar> cut, int index)
    {
        _capturedMenu = null;
        cut.FindAll(".tab")[index].ContextMenu();
        Assert.NotNull(_capturedMenu);
        return _capturedMenu!;
    }

    private async Task RaiseStateChange(IRenderedComponent<LogTabBar> cut, LogTableState next)
    {
        _logTableState.Value.Returns(next);

        await cut.InvokeAsync(() =>
            _logTableState.StateChanged += Raise.Event<EventHandler>(_logTableState, EventArgs.Empty));
        await cut.InvokeAsync(() => Task.CompletedTask);
    }
}
