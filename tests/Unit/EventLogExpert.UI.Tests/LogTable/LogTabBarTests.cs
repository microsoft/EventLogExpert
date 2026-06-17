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
