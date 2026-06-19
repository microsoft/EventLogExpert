// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.UI.LogTable;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.LogTable;

public sealed class LogTabBarTests : BunitContext
{
    private readonly IEventLogCommands _eventLogCommands = Substitute.For<IEventLogCommands>();
    private readonly ILogTableCommands _logTableCommands = Substitute.For<ILogTableCommands>();
    private readonly IState<LogTableState> _logTableState = Substitute.For<IState<LogTableState>>();

    public LogTabBarTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./_content/EventLogExpert.UI/LogTable/LogTabBar.razor.js");

        Services.AddSingleton(_eventLogCommands);
        Services.AddSingleton(_logTableCommands);
        Services.AddSingleton(_logTableState);
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
    public void ChevronClick_DispatchesSetTabGroupCollapsed()
    {
        var (state, groupId, _, _, _) = GroupedState(collapsed: false, activeIsMember1: false);
        _logTableState.Value.Returns(state);
        var cut = Render<LogTabBar>();

        cut.Find("button.chevron").Click();

        _logTableCommands.Received(1).SetTabGroupCollapsed(groupId, true);
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
    public void GroupHeaderName_MouseDown_DispatchesSetActiveTable()
    {
        var (state, _, headerId, _, _) = GroupedState(collapsed: false, activeIsMember1: false);
        _logTableState.Value.Returns(state);
        var cut = Render<LogTabBar>();

        cut.Find(".group-header > span").MouseDown();

        _logTableCommands.Received(1).SetActiveTable(headerId);
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

    private static LogTableState TwoTabState(EventLogId alpha, EventLogId beta, int alphaCount, int betaCount) =>
        new()
        {
            ActiveEventLogId = alpha,
            EventTables = ImmutableList.Create(
                new LogView(alpha) { LogName = "Alpha" },
                new LogView(beta) { LogName = "Beta" }),
            EventCountByLog = ImmutableDictionary<EventLogId, int>.Empty.Add(alpha, alphaCount).Add(beta, betaCount)
        };

    private async Task RaiseStateChange(IRenderedComponent<LogTabBar> cut, LogTableState next)
    {
        _logTableState.Value.Returns(next);

        await cut.InvokeAsync(() =>
            _logTableState.StateChanged += Raise.Event<EventHandler>(_logTableState, EventArgs.Empty));
        await cut.InvokeAsync(() => Task.CompletedTask);
    }
}
