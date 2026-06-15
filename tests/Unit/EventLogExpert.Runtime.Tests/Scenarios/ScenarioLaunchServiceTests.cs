// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Scenarios;
using EventLogExpert.Scenarios.Catalog;
using Fluxor;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.Scenarios;

public sealed class ScenarioLaunchServiceTests
{
    [Fact]
    public async Task LaunchAsync_AppliesFiltersThenDate_BeforeOpening()
    {
        var (service, dispatcher, menu, scenario) = Create(new OpenLogsBatchResult(1, 0, 0, 0, []));
        var window = new DateFilter { After = DateTime.UtcNow.AddDays(-7), Before = DateTime.UtcNow };

        await service.LaunchAsync(scenario, window);

        Received.InOrder(() =>
        {
            dispatcher.Dispatch(Arg.Any<ReplaceFiltersAction>());
            dispatcher.Dispatch(Arg.Any<SetFilterDateRangeAction>());
            _ = menu.OpenLiveLogsAsync(Arg.Any<IEnumerable<string>>(), false);
        });
    }

    [Fact]
    public async Task LaunchAsync_NullWindow_ClearsDateFilter()
    {
        var (service, dispatcher, _, scenario) = Create(new OpenLogsBatchResult(1, 0, 0, 0, []));

        await service.LaunchAsync(scenario, dateWindow: null);

        dispatcher.Received().Dispatch(Arg.Is<SetFilterDateRangeAction>(action => action.DateFilter == null));
    }

    [Fact]
    public async Task LaunchAsync_ReturnsOpenCounts()
    {
        var (service, _, _, scenario) = Create(new OpenLogsBatchResult(2, 1, 0, 0, []));

        var result = await service.LaunchAsync(scenario, dateWindow: null);

        Assert.Equal(2, result.Opened);
        Assert.Equal(1, result.Empty);
        Assert.True(result.AnyOpened);
    }

    [Fact]
    public async Task LaunchAsync_SomethingOpened_DoesNotDispatchCloseAll()
    {
        var (service, dispatcher, _, scenario) = Create(new OpenLogsBatchResult(1, 0, 0, 0, []));

        await service.LaunchAsync(scenario, dateWindow: null);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<CloseAllLogsAction>());
    }

    [Fact]
    public async Task LaunchAsync_ZeroOpenButCombining_DoesNotDispatchCloseAll()
    {
        var (service, dispatcher, _, scenario) = Create(new OpenLogsBatchResult(0, 0, 1, 0, []));

        await service.LaunchAsync(scenario, dateWindow: null, combineLog: true);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<CloseAllLogsAction>());
    }

    [Fact]
    public async Task LaunchAsync_ZeroOpenFreshView_DispatchesCloseAll()
    {
        var (service, dispatcher, _, scenario) = Create(new OpenLogsBatchResult(0, 1, 0, 0, ["System"]));

        await service.LaunchAsync(scenario, dateWindow: null);

        dispatcher.Received(1).Dispatch(Arg.Any<CloseAllLogsAction>());
    }

    private static (IScenarioLaunchService Service, IDispatcher Dispatcher, IMenuActionService Menu, ScenarioDefinition Scenario)
        Create(OpenLogsBatchResult openResult)
    {
        var scenario = ScenarioTestData.Single("a", "System", 1000);
        var registry = ScenarioTestData.Registry(scenario);

        var menu = Substitute.For<IMenuActionService>();
        menu.OpenLiveLogsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<bool>()).Returns(openResult);

        var dispatcher = Substitute.For<IDispatcher>();
        var service = new ScenarioLaunchService(registry, menu, dispatcher);

        return (service, dispatcher, menu, scenario);
    }
}
