// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Scenarios;
using Fluxor;
using NSubstitute;
using FilterPaneReducers = EventLogExpert.Runtime.FilterPane.Reducers;

namespace EventLogExpert.Runtime.Tests.Scenarios;

public sealed class ScenarioApplyServiceTests
{
    [Fact]
    public void ApplyInApp_Merge_DispatchesMergeFiltersWithBuiltRows()
    {
        var scenario = ScenarioTestData.Single("a", "System", 1000);
        var registry = ScenarioTestData.Registry(scenario);
        var dispatcher = Substitute.For<IDispatcher>();
        var service = new ScenarioApplyService(registry, dispatcher);

        var expected = registry.BuildFilterSet(scenario);

        service.ApplyInApp(scenario, replace: false);

        dispatcher.Received(1).Dispatch(Arg.Is<MergeFiltersAction>(action =>
            action != null
            && action.Filters.Count == expected.Count
            && action.Filters[0].ComparisonText == expected[0].ComparisonText));
        dispatcher.DidNotReceive().Dispatch(Arg.Any<ReplaceFiltersAction>());
    }

    [Fact]
    public void ApplyInApp_Merge_ThroughReducer_IsIdempotent()
    {
        var scenario = ScenarioTestData.Single("a", "System", 1000);
        var registry = ScenarioTestData.Registry(scenario);
        var dispatcher = Substitute.For<IDispatcher>();
        var service = new ScenarioApplyService(registry, dispatcher);

        MergeFiltersAction? captured = null;
        dispatcher.When(target => target.Dispatch(Arg.Any<MergeFiltersAction>()))
            .Do(call => captured = call.ArgAt<MergeFiltersAction>(0));

        service.ApplyInApp(scenario, replace: false);

        Assert.NotNull(captured);

        var afterFirst = FilterPaneReducers.ReduceMergeFilters(new FilterPaneState(), captured);
        var afterSecond = FilterPaneReducers.ReduceMergeFilters(afterFirst, captured);

        Assert.Single(afterFirst.Filters);
        Assert.Equal(afterFirst.Filters.Count, afterSecond.Filters.Count);
    }

    [Fact]
    public void ApplyInApp_NullScenario_Throws()
    {
        var registry = ScenarioTestData.Registry(ScenarioTestData.Single("a", "System", 1000));
        var service = new ScenarioApplyService(registry, Substitute.For<IDispatcher>());

        Assert.Throws<ArgumentNullException>(() => service.ApplyInApp(null!, replace: false));
    }

    [Fact]
    public void ApplyInApp_Replace_DispatchesReplaceFilters()
    {
        var scenario = ScenarioTestData.Single("a", "System", 1000);
        var registry = ScenarioTestData.Registry(scenario);
        var dispatcher = Substitute.For<IDispatcher>();
        var service = new ScenarioApplyService(registry, dispatcher);

        var expected = registry.BuildFilterSet(scenario);

        service.ApplyInApp(scenario, replace: true);

        dispatcher.Received(1).Dispatch(Arg.Is<ReplaceFiltersAction>(action =>
            action != null
            && action.Filters.Count == expected.Count
            && action.Filters[0].ComparisonText == expected[0].ComparisonText));
        dispatcher.DidNotReceive().Dispatch(Arg.Any<MergeFiltersAction>());
    }

    [Fact]
    public void ApplyInApp_Replace_ThroughReducer_SwapsRows()
    {
        var scenario = ScenarioTestData.Single("a", "System", 1000);
        var registry = ScenarioTestData.Registry(scenario);
        var dispatcher = Substitute.For<IDispatcher>();
        var service = new ScenarioApplyService(registry, dispatcher);

        ReplaceFiltersAction? captured = null;
        dispatcher.When(target => target.Dispatch(Arg.Any<ReplaceFiltersAction>()))
            .Do(call => captured = call.ArgAt<ReplaceFiltersAction>(0));

        var seeded = FilterPaneReducers.ReduceMergeFilters(
            new FilterPaneState(),
            new MergeFiltersAction(registry.BuildFilterSet(ScenarioTestData.Single("b", "System", 2000))));

        service.ApplyInApp(scenario, replace: true);

        Assert.NotNull(captured);

        var replaced = FilterPaneReducers.ReduceReplaceFilters(seeded, captured);

        Assert.Single(replaced.Filters);
        Assert.Equal("Id == 1000", replaced.Filters[0].ComparisonText);
    }
}
