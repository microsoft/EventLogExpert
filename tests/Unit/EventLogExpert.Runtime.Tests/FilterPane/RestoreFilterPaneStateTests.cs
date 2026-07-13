// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.LogTable;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;
using Effects = EventLogExpert.Runtime.FilterPane.Effects;
using Reducers = EventLogExpert.Runtime.FilterPane.Reducers;

namespace EventLogExpert.Runtime.Tests.FilterPane;

public sealed class RestoreFilterPaneStateTests
{
    [Fact]
    public async Task HandleRestoreFilterPaneState_DispatchesUpdateEventTableFilters()
    {
        var (effects, dispatcher) = CreateEffects(isEnabled: true, filters: [FilterBuilder.CreateTestFilter(isEnabled: true)]);

        await effects.HandleRestoreFilterPaneState(dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public void ReduceRestoreFilterPaneState_PreservesDisabledRowsAndDateVerbatim()
    {
        var disabledRow = FilterBuilder.CreateTestFilter(isEnabled: false);
        var captured = new FilterPaneState
        {
            IsEnabled = false,
            Filters = [disabledRow],
            FilteredDateRange = new DateFilter
            {
                After = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Before = new DateTime(2024, 1, 31, 0, 0, 0, DateTimeKind.Utc),
                IsEnabled = false
            }
        };

        var result = Reducers.ReduceRestoreFilterPaneState(new FilterPaneState(), new RestoreFilterPaneStateAction(captured));

        Assert.Equal(disabledRow.Id, result.Filters[0].Id);
        Assert.False(result.Filters[0].IsEnabled);
        Assert.False(result.FilteredDateRange!.IsEnabled);
        Assert.False(result.IsEnabled);
    }

    private static (Effects Effects, IDispatcher Dispatcher) CreateEffects(bool isEnabled, ImmutableList<SavedFilter> filters)
    {
        var filterPaneState = Substitute.For<IState<FilterPaneState>>();
        filterPaneState.Value.Returns(new FilterPaneState { IsEnabled = isEnabled, Filters = filters });

        var appliedFilter = Substitute.For<IStateSelection<EventLogState, Filter>>();
        appliedFilter.Value.Returns(new Filter(null, []));

        var rawEventStore = Substitute.For<IState<RawEventStoreState>>();
        rawEventStore.Value.Returns(new RawEventStoreState());

        var lensState = Substitute.For<IState<FilterLensState>>();
        lensState.Value.Returns(new FilterLensState());

        var effects = new Effects(appliedFilter, rawEventStore, filterPaneState, lensState);

        return (effects, Substitute.For<IDispatcher>());
    }
}
