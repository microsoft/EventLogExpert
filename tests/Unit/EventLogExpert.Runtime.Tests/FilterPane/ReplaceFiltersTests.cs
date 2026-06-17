// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.LogTable;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;
using Effects = EventLogExpert.Runtime.FilterPane.Effects;
using Reducers = EventLogExpert.Runtime.FilterPane.Reducers;

namespace EventLogExpert.Runtime.Tests.FilterPane;

public sealed class ReplaceFiltersTests
{
    [Fact]
    public async Task HandleReplaceFilters_DispatchesUpdateEventTableFilters()
    {
        // Arrange
        var (effects, dispatcher) = CreateEffects(isEnabled: true, filters: [FilterBuilder.CreateTestFilter(isEnabled: true)]);

        // Act
        await effects.HandleReplaceFilters(dispatcher);

        // Assert
        dispatcher.Received(1).Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public void ReduceReplaceFilters_AssignsFreshIds()
    {
        // Arrange
        var input = FilterBuilder.CreateTestFilter();
        var action = new ReplaceFiltersAction([input]);

        // Act
        var result = Reducers.ReduceReplaceFilters(new FilterPaneState(), action);

        // Assert
        Assert.NotEqual(input.Id, result.Filters[0].Id);
    }

    [Fact]
    public void ReduceReplaceFilters_PreservesOtherStateFields()
    {
        // Arrange
        var initialState = new FilterPaneState
        {
            IsEnabled = false,
            FilteredDateRange = new DateFilter
            {
                After = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
                Before = new DateTime(2026, 5, 31, 0, 0, 0, DateTimeKind.Utc),
                IsEnabled = true,
            },
        };

        var action = new ReplaceFiltersAction([FilterBuilder.CreateTestFilter()]);

        // Act
        var result = Reducers.ReduceReplaceFilters(initialState, action);

        // Assert
        Assert.False(result.IsEnabled);
        Assert.Equal(initialState.FilteredDateRange, result.FilteredDateRange);
    }

    [Fact]
    public void ReduceReplaceFilters_ReplacesFilters()
    {
        // Arrange
        var stale = FilterBuilder.CreateTestFilter(isEnabled: true);
        var initialState = new FilterPaneState { Filters = [stale] };

        var newFilter = FilterBuilder.CreateTestFilter();
        var action = new ReplaceFiltersAction([newFilter]);

        // Act
        var result = Reducers.ReduceReplaceFilters(initialState, action);

        // Assert
        Assert.Single(result.Filters);
        Assert.DoesNotContain(result.Filters, f => f.Id == stale.Id);
    }

    private static (Effects effects, IDispatcher dispatcher) CreateEffects(
        bool isEnabled,
        ImmutableList<SavedFilter> filters)
    {
        var mockFilterPaneState = Substitute.For<IState<FilterPaneState>>();
        mockFilterPaneState.Value.Returns(new FilterPaneState
        {
            IsEnabled = isEnabled,
            Filters = filters,
        });

        var mockAppliedFilter = Substitute.For<IStateSelection<EventLogState, Filter>>();
        mockAppliedFilter.Value.Returns(new Filter(null, []));

        var mockRawEventStore = Substitute.For<IState<RawEventStoreState>>();
        mockRawEventStore.Value.Returns(new RawEventStoreState());

        var effects = new Effects(mockAppliedFilter, mockRawEventStore, mockFilterPaneState);
        var dispatcher = Substitute.For<IDispatcher>();

        return (effects, dispatcher);
    }
}
