// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterPane;
using Fluxor;
using NSubstitute;
using EventLogReducers = EventLogExpert.Runtime.EventLog.Reducers;
using FilterPaneEffects = EventLogExpert.Runtime.FilterPane.Effects;
using FilterPaneReducers = EventLogExpert.Runtime.FilterPane.Reducers;

namespace EventLogExpert.Runtime.Tests.Scenarios;

public sealed class ScenarioApplyIntegrationTests
{
    [Fact]
    public void MergeFiltersAction_ReapplyingScenario_IsIdempotent()
    {
        var registry = ScenarioTestData.Registry(ScenarioTestData.Single("a", "System", 1000));
        var filters = registry.BuildFilterSet(registry.Scenarios[0]);

        var afterFirst = FilterPaneReducers.ReduceMergeFilters(new FilterPaneState(), new MergeFiltersAction(filters));
        var afterSecond = FilterPaneReducers.ReduceMergeFilters(afterFirst, new MergeFiltersAction(filters));

        Assert.Single(afterFirst.Filters);
        Assert.Equal(afterFirst.Filters.Count, afterSecond.Filters.Count);
    }

    [Fact]
    public async Task ReplaceFiltersAction_FromScenario_LandsOnAppliedFilter()
    {
        var registry = ScenarioTestData.Registry(ScenarioTestData.Single("a", "System", 1000));
        var filters = registry.BuildFilterSet(registry.Scenarios[0]);

        var paneState = FilterPaneReducers.ReduceReplaceFilters(new FilterPaneState(), new ReplaceFiltersAction(filters));

        Assert.Single(paneState.Filters);
        Assert.True(paneState.Filters[0].IsEnabled);

        var paneStateAccessor = Substitute.For<IState<FilterPaneState>>();
        paneStateAccessor.Value.Returns(paneState);

        var appliedFilter = Substitute.For<IStateSelection<EventLogState, Filter>>();
        appliedFilter.Value.Returns(new Filter(null, []));

        var eventDateRange = Substitute.For<IStateSelection<EventLogState, (DateTime After, DateTime Before)?>>();
        eventDateRange.Value.Returns(((DateTime, DateTime)?)null);

        var effects = new FilterPaneEffects(appliedFilter, eventDateRange, paneStateAccessor);
        var dispatcher = Substitute.For<IDispatcher>();

        ApplyFilterAction? captured = null;
        dispatcher.When(target => target.Dispatch(Arg.Any<ApplyFilterAction>()))
            .Do(call => captured = (ApplyFilterAction)call[0]);

        await effects.HandleReplaceFilters(dispatcher);

        Assert.NotNull(captured);

        var eventLogState = EventLogReducers.ReduceApplyFilter(new EventLogState(), captured);

        Assert.Single(eventLogState.AppliedFilter.Filters);
        Assert.Equal("Id == 1000", eventLogState.AppliedFilter.Filters[0].ComparisonText);
    }
}
