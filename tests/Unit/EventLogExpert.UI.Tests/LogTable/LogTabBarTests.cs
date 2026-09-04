// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Localization;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.UI.LogTable;
using EventLogExpert.UI.Menu;
using EventLogExpert.UI.Tests.TestUtils;
using Fluxor;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
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
    private readonly IState<FilteredLogPresenceState> _presenceState =
        Substitute.For<IState<FilteredLogPresenceState>>();
    private readonly ILogTableQueries _queries = Substitute.For<ILogTableQueries>();
    private readonly ILogTabBarSource _source = Substitute.For<ILogTabBarSource>();
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

        _logTableState.Value.Returns(new LogTableState());

        _presenceState.Value.Returns(new FilteredLogPresenceState());

        _source.Current.Returns(_ => PresentationFrom(_logTableState.Value, _presenceState.Value));
        _queries.GetTabGroups().Returns(_ => _logTableState.Value.Groups);
        _queries.HasMultipleIndividualTabs()
            .Returns(_ => _logTableState.Value.EventTables.Count(table => !table.IsCombined) > 1);
        _queries.HasOtherTabsInGroup(Arg.Any<LogTabGroupId>(), Arg.Any<EventLogId>()).Returns(call =>
        {
            var state = _logTableState.Value;

            if (state.Groups.FirstOrDefault(group => group.Id == call.ArgAt<LogTabGroupId>(0)) is not { } group)
            {
                return false;
            }

            return group.MemberIds.Contains(call.ArgAt<EventLogId>(1)) &&
                state.EventTables.Count(table => table.GroupId is null && group.MemberIds.Contains(table.Id)) > 1;
        });
        _queries.HasTabGroup(Arg.Any<LogTabGroupId>())
            .Returns(call => _logTableState.Value.Groups.Any(group => group.Id == call.ArgAt<LogTabGroupId>(0)));
        _queries.IsTabOpen(Arg.Any<EventLogId>())
            .Returns(call => _logTableState.Value.EventTables.Any(table => table.Id == call.ArgAt<EventLogId>(0)));
        _queries.IsUngroupedTabOpen(Arg.Any<EventLogId>())
            .Returns(call => _logTableState.Value.EventTables.Any(
                table => table.Id == call.ArgAt<EventLogId>(0) && table.GroupId is null));

        Services.AddSingleton(_alertDialogService);
        Services.AddSingleton(_eventLogCommands);
        Services.AddSingleton(_logTableCommands);
        Services.AddSingleton(_menuService);
        Services.AddSingleton(_queries);
        Services.AddSingleton(_source);
        Services.AddSingleton(_traceLogger);
        Services.AddSingleton<IStringLocalizer<SharedResource>>(new MarkerLocalizer());
    }

    private IStringLocalizer<SharedResource> Localizer =>
        Services.GetRequiredService<IStringLocalizer<SharedResource>>();

    [Fact]
    public async Task ActiveTabChange_Rerenders()
    {
        var alpha = EventLogId.Create();
        var beta = EventLogId.Create();
        var state1 = TwoTabState(alpha, beta);
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
        _alertDialogService.ShowAlert(Localizer["CloseAllLogs_Title"].Value, Arg.Any<string>(), Localizer["CloseAllLogs_Confirm"].Value, Localizer["Modal_Cancel"].Value).Returns(true);
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
        _alertDialogService.ShowAlert(Localizer["CloseAllLogs_Title"].Value, Arg.Any<string>(), Localizer["CloseAllLogs_Confirm"].Value, Localizer["Modal_Cancel"].Value).Returns(false);
        var cut = Render<LogTabBar>();
        var menu = OpenContextMenu(cut, ".tab");

        await InvokeMenuItemAsync(cut, FindItem(menu, "Close all logs"));

        _eventLogCommands.DidNotReceive().CloseAllLogs();
    }

    [Fact]
    public void AriaLabelsAndTitles_RouteThroughMarkerLocalizer()
    {
        var (collapsedState, _, _, _, _) = GroupedState(collapsed: true, activeIsMember1: false);
        _logTableState.Value.Returns(collapsedState);
        var collapsedCut = Render<LogTabBar>();

        Assert.Equal(Localizer["TabBar_ExpandGroupAria"].Value, collapsedCut.Find("button.chevron").GetAttribute("aria-label"));
        Assert.Equal(Localizer["TabBar_ExpandGroupAria"].Value, collapsedCut.Find("button.chevron").GetAttribute("title"));

        var (expandedState, _, _, _, _) = GroupedState(collapsed: false, activeIsMember1: false);
        _logTableState.Value.Returns(expandedState);
        var expandedCut = Render<LogTabBar>();

        Assert.Equal(Localizer["TabBar_CollapseGroupAria"].Value, expandedCut.Find("button.chevron").GetAttribute("aria-label"));
        Assert.Equal(Localizer["TabBar_CollapseGroupAria"].Value, expandedCut.Find("button.chevron").GetAttribute("title"));
        Assert.Equal(Localizer["TabBar_CloseGroup"].Value, expandedCut.Find(".group-header > i.bi-x").GetAttribute("aria-label"));
        Assert.Equal(Localizer["TabBar_CloseGroup"].Value, expandedCut.Find(".group-header > i.bi-x").GetAttribute("title"));
        Assert.Equal(Localizer["TabBar_CloseLogAria"].Value, expandedCut.Find(".tab.member > i.bi-x").GetAttribute("aria-label"));
        Assert.Equal(Localizer["TabBar_CloseLogAria"].Value, expandedCut.Find(".tab.member > i.bi-x").GetAttribute("title"));
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
        _logTableState.Value.Returns(TwoTabState(alpha, beta));
        var cut = Render<LogTabBar>();
        var menu = OpenContextMenu(cut, ".tab");

        _logTableState.Value.Returns(TwoTabState(beta, EventLogId.Create()));

        await InvokeMenuItemAsync(cut, FindItem(menu, "Close"));

        _eventLogCommands.DidNotReceive().CloseLog(Arg.Any<EventLogId>(), Arg.Any<string>());
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
        Assert.Equal(Localizer["TabBar_Menu_NoOtherTabsToCloseReason"].Value, item.DisabledReason);
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
        Assert.Equal(Localizer["TabBar_Menu_NoOtherTabsInGroupReason"].Value, item.DisabledReason);
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
    public void CombinedHeader_RendersCombinedLabel()
    {
        var allLogsId = EventLogId.Create();
        var logId = EventLogId.Create();
        var state = new LogTableState
        {
            ActiveEventLogId = allLogsId,
            EventTables = ImmutableList.Create(
                new LogView(allLogsId) { GroupId = LogTabGroupId.AllLogs },
                new LogView(logId) { LogName = "Alpha" })
        };
        _logTableState.Value.Returns(state);

        var cut = Render<LogTabBar>();

        Assert.Contains(Localizer["TabBar_TabName_Combined"].Value, cut.Markup);
    }

    [Fact]
    public async Task EventTablesChange_Rerenders()
    {
        var alpha = EventLogId.Create();
        var beta = EventLogId.Create();
        var gamma = EventLogId.Create();
        var state1 = TwoTabState(alpha, beta);
        _logTableState.Value.Returns(state1);
        var cut = Render<LogTabBar>();
        int before = cut.RenderCount;

        var state2 = state1 with
        {
            EventTables = state1.EventTables.Add(new LogView(gamma) { LogName = "Gamma" })
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
        _logTableState.Value.Returns(TwoTabState(alpha, beta));

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
    public void GroupHeaderName_MouseDown_DispatchesSetActiveTable()
    {
        var (state, _, headerId, _, _) = GroupedState(collapsed: false, activeIsMember1: false);
        _logTableState.Value.Returns(state);
        var cut = Render<LogTabBar>();

        cut.Find(".group-header > span").MouseDown();

        _logTableCommands.Received(1).SetActiveTable(headerId);
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
    public async Task GroupHeader_Expand_DispatchesSetTabGroupCollapsed()
    {
        var (state, groupId, _, _, _) = GroupedState(collapsed: true, activeIsMember1: false);
        _logTableState.Value.Returns(state);
        var cut = Render<LogTabBar>();
        var menu = OpenContextMenu(cut, ".group-header");

        Assert.Contains(menu, item => item.Label == Localizer["TabBar_Menu_Expand"].Value);
        await InvokeMenuItemAsync(cut, FindItem(menu, "Expand"));

        _logTableCommands.Received(1).SetTabGroupCollapsed(groupId, false);
    }

    [Fact]
    public async Task GroupHeader_Rename_PromptThenDispatches()
    {
        var (state, groupId, _, _, _) = GroupedState(collapsed: false, activeIsMember1: false);
        _logTableState.Value.Returns(state);
        Func<string, string?>? validator = null;
        _alertDialogService
            .DisplayPrompt(
                Localizer["TabBar_Prompt_RenameGroupTitle"].Value,
                Localizer["TabBar_Prompt_GroupNameLabel"].Value,
                "MyGroup",
                Arg.Do<Func<string, string?>?>(callback => validator = callback))
            .Returns("Renamed");
        var cut = Render<LogTabBar>();
        var menu = OpenContextMenu(cut, ".group-header");

        await InvokeMenuItemAsync(cut, FindItem(menu, "Rename\u2026"));

        _logTableCommands.Received(1).RenameGroup(groupId, "Renamed");
        Assert.NotNull(validator);
        Assert.Equal(Localizer["TabBar_Prompt_GroupNameRequired"].Value, validator(" "));
        Assert.Null(validator("Renamed"));
    }

    [Fact]
    public async Task GroupHeader_Rename_StaleGroup_DoesNotDispatch()
    {
        var (state, _, _, _, _) = GroupedState(collapsed: false, activeIsMember1: false);
        _logTableState.Value.Returns(state);
        _alertDialogService
            .DisplayPrompt(Localizer["TabBar_Prompt_RenameGroupTitle"].Value, Localizer["TabBar_Prompt_GroupNameLabel"].Value, "MyGroup", Arg.Any<Func<string, string?>?>())
            .Returns("Renamed");
        var cut = Render<LogTabBar>();
        var menu = OpenContextMenu(cut, ".group-header");

        _logTableState.Value.Returns(new LogTableState());

        await InvokeMenuItemAsync(cut, FindItem(menu, "Rename\u2026"));

        _logTableCommands.DidNotReceive().RenameGroup(Arg.Any<LogTabGroupId>(), Arg.Any<string>());
    }

    [Fact]
    public void GroupHeader_RightClick_ShowsExpectedItems()
    {
        var (state, _, _, _, _) = GroupedState(collapsed: false, activeIsMember1: false);
        _logTableState.Value.Returns(state);
        var cut = Render<LogTabBar>();

        var menu = OpenContextMenu(cut, ".group-header");

        var labels = menu.Select(item => item.Label).ToList();
        Assert.Equal([Localizer["TabBar_Menu_Rename"].Value, Localizer["TabBar_Menu_Collapse"].Value, string.Empty, Localizer["TabBar_CloseGroup"].Value], labels);
    }

    [Fact]
    public void KnownEmptyPresence_RendersEmptyPrefix()
    {
        var alpha = EventLogId.Create();
        var beta = EventLogId.Create();
        _logTableState.Value.Returns(TwoTabState(alpha, beta));
        _presenceState.Value.Returns(Presence((alpha, false), (beta, true)));

        var cut = Render<LogTabBar>();

        Assert.Contains("[[TabBar_TabName_Empty(", cut.Markup);
        Assert.DoesNotContain("Beta)]]", cut.Markup);
    }

    [Fact]
    public void LoadingSpinner_RoutesAriaLabelThroughMarkerLocalizer()
    {
        var alpha = EventLogId.Create();
        var beta = EventLogId.Create();
        _logTableState.Value.Returns(new LogTableState
        {
            ActiveEventLogId = alpha,
            EventTables = ImmutableList.Create(
                new LogView(alpha) { LogName = "Alpha", IsLoading = true },
                new LogView(beta) { LogName = "Beta" })
        });

        var cut = Render<LogTabBar>();

        Assert.Equal(Localizer["TabBar_LoadingAria"].Value, cut.Find("i.loader-spin").GetAttribute("aria-label"));
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
            .DisplayPrompt(Localizer["TabBar_Prompt_NewGroupTitle"].Value, Localizer["TabBar_Prompt_GroupNameLabel"].Value, Arg.Any<string>(), Arg.Any<Func<string, string?>?>())
            .Returns("Split");
        var cut = Render<LogTabBar>();
        var menu = OpenContextMenu(cut, ".tab.member");

        var moveTo = FindItem(menu, "Move to group");
        Assert.NotNull(moveTo.Children);
        Assert.Equal([Localizer["TabBar_Menu_NewGroup"].Value], moveTo.Children!.Select(item => item.Label).ToList());

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
            [Localizer["TabBar_Menu_MoveToGroup"].Value, Localizer["TabBar_Menu_RemoveFromGroup"].Value, string.Empty, Localizer["TabBar_Menu_Close"].Value, Localizer["TabBar_Menu_CloseOthersInGroup"].Value, Localizer["TabBar_Menu_CloseOtherTabs"].Value],
            labels);
    }

    [Fact]
    public void PendingPresence_DoesNotRenderEmptyPrefix()
    {
        var alpha = EventLogId.Create();
        var beta = EventLogId.Create();
        _logTableState.Value.Returns(TwoTabState(alpha, beta));
        _presenceState.Value.Returns(new FilteredLogPresenceState());

        var cut = Render<LogTabBar>();

        Assert.DoesNotContain("[[TabBar_TabName_Empty", cut.Markup);
    }

    [Fact]
    public async Task PresenceChange_RerendersEmptyLabels()
    {
        var alpha = EventLogId.Create();
        var beta = EventLogId.Create();
        _logTableState.Value.Returns(TwoTabState(alpha, beta));
        var cut = Render<LogTabBar>();
        Assert.DoesNotContain(Localizer["TabBar_TabName_Empty", "Alpha"].Value, cut.Markup);

        await RaisePresenceChange(cut, Presence((alpha, false), (beta, true)));

        Assert.Contains("[[TabBar_TabName_Empty(", cut.Markup);
    }

    [Fact]
    public void RightClickMouseDown_DoesNotActivateTab()
    {
        var alpha = EventLogId.Create();
        var beta = EventLogId.Create();
        _logTableState.Value.Returns(TwoTabState(alpha, beta));
        var cut = Render<LogTabBar>();

        cut.Find(".tab > span").MouseDown(new MouseEventArgs { Button = 2 });

        _logTableCommands.DidNotReceive().SetActiveTable(Arg.Any<EventLogId>());
    }

    [Fact]
    public void RightClickMouseDown_OnCloseIcon_DoesNotCloseLog()
    {
        var alpha = EventLogId.Create();
        var beta = EventLogId.Create();
        _logTableState.Value.Returns(TwoTabState(alpha, beta));
        var cut = Render<LogTabBar>();

        cut.Find(".tab > i.bi-x").MouseDown(new MouseEventArgs { Button = 2 });

        _eventLogCommands.DidNotReceive().CloseLog(Arg.Any<EventLogId>(), Arg.Any<string>());
    }

    [Fact]
    public async Task StandaloneTab_BlankGroupName_DoesNotDispatch()
    {
        var alpha = EventLogId.Create();
        var beta = EventLogId.Create();
        _logTableState.Value.Returns(TwoTabState(alpha, beta));
        _alertDialogService
            .DisplayPrompt(Localizer["TabBar_Prompt_NewGroupTitle"].Value, Localizer["TabBar_Prompt_GroupNameLabel"].Value, Arg.Any<string>(), Arg.Any<Func<string, string?>?>())
            .Returns("   ");
        var cut = Render<LogTabBar>();
        var menu = OpenContextMenu(cut, ".tab");

        await InvokeMenuItemAsync(cut, FindItem(menu, "New group from tab\u2026"));

        _logTableCommands.DidNotReceive().NewGroupFromTab(Arg.Any<EventLogId>(), Arg.Any<string>());
    }

    [Fact]
    public async Task StandaloneTab_CloseOtherTabs_DispatchesCloseAllButThis()
    {
        var alpha = EventLogId.Create();
        var beta = EventLogId.Create();
        _logTableState.Value.Returns(TwoTabState(alpha, beta));
        var cut = Render<LogTabBar>();
        var menu = OpenContextMenu(cut, ".tab");

        await InvokeMenuItemAsync(cut, FindItem(menu, "Close other tabs"));

        _logTableCommands.Received(1).CloseAllButThis(alpha);
    }

    [Fact]
    public async Task StandaloneTab_Close_DispatchesCloseLog()
    {
        var alpha = EventLogId.Create();
        var beta = EventLogId.Create();
        _logTableState.Value.Returns(TwoTabState(alpha, beta));
        var cut = Render<LogTabBar>();
        var menu = OpenContextMenu(cut, ".tab");

        await InvokeMenuItemAsync(cut, FindItem(menu, "Close"));

        _eventLogCommands.Received(1).CloseLog(alpha, "Alpha");
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
    public async Task StandaloneTab_NewGroupFromTab_PromptThenDispatches()
    {
        var alpha = EventLogId.Create();
        var beta = EventLogId.Create();
        _logTableState.Value.Returns(TwoTabState(alpha, beta));
        Func<string, string?>? validator = null;
        _alertDialogService
            .DisplayPrompt(
                Localizer["TabBar_Prompt_NewGroupTitle"].Value,
                Localizer["TabBar_Prompt_GroupNameLabel"].Value,
                Arg.Any<string>(),
                Arg.Do<Func<string, string?>?>(callback => validator = callback))
            .Returns("Diagnostics");
        var cut = Render<LogTabBar>();
        var menu = OpenContextMenu(cut, ".tab");

        await InvokeMenuItemAsync(cut, FindItem(menu, "New group from tab\u2026"));

        _logTableCommands.Received(1).NewGroupFromTab(alpha, "Diagnostics");
        Assert.NotNull(validator);
        Assert.Equal(Localizer["TabBar_Prompt_GroupNameRequired"].Value, validator(" "));
        Assert.Null(validator("Diagnostics"));
    }

    [Fact]
    public async Task StandaloneTab_NewGroup_StaleTab_DoesNotDispatch()
    {
        var alpha = EventLogId.Create();
        var beta = EventLogId.Create();
        _logTableState.Value.Returns(TwoTabState(alpha, beta));
        _alertDialogService
            .DisplayPrompt(Localizer["TabBar_Prompt_NewGroupTitle"].Value, Localizer["TabBar_Prompt_GroupNameLabel"].Value, Arg.Any<string>(), Arg.Any<Func<string, string?>?>())
            .Returns("Diagnostics");
        var cut = Render<LogTabBar>();
        var menu = OpenContextMenu(cut, ".tab");

        _logTableState.Value.Returns(TwoTabState(beta, EventLogId.Create()));

        await InvokeMenuItemAsync(cut, FindItem(menu, "New group from tab\u2026"));

        _logTableCommands.DidNotReceive().NewGroupFromTab(Arg.Any<EventLogId>(), Arg.Any<string>());
    }

    [Fact]
    public void StandaloneTab_RightClick_ShowsExpectedItems()
    {
        var alpha = EventLogId.Create();
        var beta = EventLogId.Create();
        _logTableState.Value.Returns(TwoTabState(alpha, beta));
        var cut = Render<LogTabBar>();

        var menu = OpenContextMenu(cut, ".tab");

        var labels = menu.Select(item => item.Label).ToList();
        Assert.Equal([Localizer["TabBar_Menu_NewGroupFromTab"].Value, Localizer["TabBar_Menu_MoveToGroup"].Value, string.Empty, Localizer["TabBar_Menu_Close"].Value, Localizer["TabBar_Menu_CloseOtherTabs"].Value], labels);
    }

    [Fact]
    public void TabTitlesAndLiveNames_RouteArgumentsThroughMarkerLocalizer()
    {
        var fileId = EventLogId.Create();
        var liveId = EventLogId.Create();
        _logTableState.Value.Returns(new LogTableState
        {
            ActiveEventLogId = fileId,
            EventTables = ImmutableList.Create(
                new LogView(fileId)
                {
                    FileName = @"C:\logs\Application.evtx",
                    LogName = "Application",
                    ComputerName = "FILEHOST",
                    LogPathType = LogPathType.File
                },
                new LogView(liveId)
                {
                    LogName = "System",
                    ComputerName = "LIVEHOST",
                    LogPathType = LogPathType.Channel
                })
        });

        var cut = Render<LogTabBar>();
        var tabs = cut.FindAll(".tab");

        Assert.Equal(Localizer["TabBar_Tooltip_File", @"C:\logs\Application.evtx", "Application", "FILEHOST"].Value, tabs[0].GetAttribute("title"));
        Assert.Equal(Localizer["TabBar_Tooltip_Live", "System", "LIVEHOST"].Value, tabs[1].GetAttribute("title"));
        Assert.Contains(Localizer["TabBar_TabName_Live", "System", "LIVEHOST"].Value, tabs[1].TextContent);
    }

    private static LogTableState AllLogsState(EventLogId allLogsId, EventLogId logId, string logName) =>
        new()
        {
            ActiveEventLogId = allLogsId,
            EventTables = ImmutableList.Create(
                new LogView(allLogsId) { GroupId = LogTabGroupId.AllLogs },
                new LogView(logId) { LogName = logName })
        };

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
                new LogTabGroup(groupId, "MyGroup", ImmutableHashSet.Create(member)))
        };

        return (state, groupId, standalone);
    }

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
                })
        };

        return (state, groupId, headerId, member1, member2);
    }

    private static FilteredLogPresenceState Presence(params (EventLogId LogId, bool HasSurvivor)[] verdicts)
    {
        var byLog = ImmutableDictionary<EventLogId, FilteredLogPresence>.Empty;

        foreach (var (logId, hasSurvivor) in verdicts)
        {
            byLog = byLog.SetItem(
                logId,
                hasSurvivor ? FilteredLogPresence.HasSurvivor : FilteredLogPresence.NoSurvivor);
        }

        return new FilteredLogPresenceState { ByLog = byLog };
    }

    private static LogTabBarPresentation PresentationFrom(LogTableState state, FilteredLogPresenceState presence)
    {
        var knownEmpty = ImmutableHashSet.CreateBuilder<EventLogId>();

        foreach (var table in state.EventTables)
        {
            if (table.IsCombined || table.IsLoading) { continue; }

            if (presence.IsKnownEmpty(table.Id)) { knownEmpty.Add(table.Id); }
        }

        return new LogTabBarPresentation
        {
            Tabs = state.EventTables,
            Groups = state.Groups,
            ActiveTabId = state.ActiveEventLogId,
            KnownEmptyTabIds = knownEmpty.ToImmutable()
        };
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
                new LogTabGroup(groupId, "Solo", ImmutableHashSet.Create(member)))
        };

        return (state, groupId, member);
    }

    private static LogTableState TwoTabState(EventLogId alpha, EventLogId beta) =>
        new()
        {
            ActiveEventLogId = alpha,
            EventTables = ImmutableList.Create(
                new LogView(alpha) { LogName = "Alpha" },
                new LogView(beta) { LogName = "Beta" })
        };

    private MenuItem FindItem(IEnumerable<MenuItem> items, string label) =>
        items.First(item => item.Label == LocalizedMenuLabel(label));

    private async Task InvokeMenuItemAsync(IRenderedComponent<LogTabBar> cut, MenuItem item) =>
        await cut.InvokeAsync(() => item.OnClickAsync!());

    private string LocalizedMenuLabel(string label) => label switch
    {
        "Close all logs" => Localizer["TabBar_Menu_CloseAllLogs"].Value,
        "Close" => Localizer["TabBar_Menu_Close"].Value,
        "Close other tabs" => Localizer["TabBar_Menu_CloseOtherTabs"].Value,
        "Close others in group" => Localizer["TabBar_Menu_CloseOthersInGroup"].Value,
        "Expand" => Localizer["TabBar_Menu_Expand"].Value,
        "Collapse" => Localizer["TabBar_Menu_Collapse"].Value,
        "Move to group" => Localizer["TabBar_Menu_MoveToGroup"].Value,
        "New group from tab\u2026" => Localizer["TabBar_Menu_NewGroupFromTab"].Value,
        "New group\u2026" => Localizer["TabBar_Menu_NewGroup"].Value,
        "Remove from group" => Localizer["TabBar_Menu_RemoveFromGroup"].Value,
        "Rename\u2026" => Localizer["TabBar_Menu_Rename"].Value,
        _ => label
    };

    private IReadOnlyList<MenuItem> OpenContextMenu(IRenderedComponent<LogTabBar> cut, string selector)
    {
        _capturedMenu = null;
        cut.Find(selector).ContextMenu(new MouseEventArgs { Button = 2 });
        Assert.NotNull(_capturedMenu);
        return _capturedMenu!;
    }

    private IReadOnlyList<MenuItem> OpenContextMenuByIndex(IRenderedComponent<LogTabBar> cut, int index)
    {
        _capturedMenu = null;
        cut.FindAll(".tab")[index].ContextMenu(new MouseEventArgs { Button = 2 });
        Assert.NotNull(_capturedMenu);
        return _capturedMenu!;
    }

    private async Task RaisePresenceChange(IRenderedComponent<LogTabBar> cut, FilteredLogPresenceState next)
    {
        _presenceState.Value.Returns(next);

        await cut.InvokeAsync(() => _source.Changed += Raise.Event<Action>());
        await cut.InvokeAsync(() => Task.CompletedTask);
    }

    private async Task RaiseStateChange(IRenderedComponent<LogTabBar> cut, LogTableState next)
    {
        _logTableState.Value.Returns(next);

        await cut.InvokeAsync(() => _source.Changed += Raise.Event<Action>());
        await cut.InvokeAsync(() => Task.CompletedTask);
    }
}
