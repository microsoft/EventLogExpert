// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Filtering.TestUtils.Constants;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Tests.TestUtils.Constants;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;
using Effects = EventLogExpert.Runtime.FilterPane.Effects;
using SetFilterAction = EventLogExpert.Runtime.FilterPane.SetFilterAction;

namespace EventLogExpert.Runtime.Tests.FilterPane;

public sealed class EffectsTests
{
    [Fact]
    public async Task HandleAddFilter_WhenComparisonValueExists_ShouldUpdateEventTableFilters()
    {
        var filterModel = FilterBuilder.CreateTestFilter(isEnabled: true);

        var (effects, mockDispatcher) = CreateEffects(true, ImmutableList.Create(filterModel));
        var action = new AddFilterAction(filterModel);

        await effects.HandleAddFilter(action, mockDispatcher);

        mockDispatcher.Received().Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task HandleAddFilter_WhenComparisonValueIsNull_ShouldNotUpdateEventTableFilters()
    {
        var filterModel = new SavedFilter
        {
            ComparisonText = string.Empty,
            Compiled = null
        };

        var (effects, mockDispatcher) = CreateEffects();
        var action = new AddFilterAction(filterModel);

        await effects.HandleAddFilter(action, mockDispatcher);

        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task HandleAddFilter_WhenFilterHasBasicFilter_ShouldRecordFilterApplied()
    {
        var filterModel = FilterBuilder.CreateTestFilter(basicFilter: CreateBasicFilter());

        var (effects, mockDispatcher) = CreateEffects();
        var action = new AddFilterAction(filterModel);

        await effects.HandleAddFilter(action, mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<RecordFilterAppliedAction>(x =>
            x != null &&
            x.Filter.ComparisonText == FilterTestConstants.FilterIdEquals100));
    }

    [Fact]
    public async Task HandleAddFilter_WhenFilterHasNoBasicFilter_ShouldRecordFilterApplied()
    {
        var filterModel = FilterBuilder.CreateTestFilter();

        var (effects, mockDispatcher) = CreateEffects();
        var action = new AddFilterAction(filterModel);

        await effects.HandleAddFilter(action, mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<RecordFilterAppliedAction>(x =>
            x != null &&
            x.Filter.ComparisonText == FilterTestConstants.FilterIdEquals100));
    }

    [Fact]
    public async Task HandleClearAllFilters_RaisesTheNotifierOncePerDispatch()
    {
        var filterPaneState = Substitute.For<IState<FilterPaneState>>();
        filterPaneState.Value.Returns(new FilterPaneState());
        var appliedFilter = Substitute.For<IStateSelection<EventLogState, Filter>>();
        appliedFilter.Value.Returns(new Filter(null, []));
        var rawEventStore = Substitute.For<IState<RawEventStoreState>>();
        rawEventStore.Value.Returns(new RawEventStoreState());
        var lensState = Substitute.For<IState<FilterLensState>>();
        lensState.Value.Returns(new FilterLensState());

        var notifier = new ClearAllFiltersNotifier(Substitute.For<ITraceLogger>());
        var raised = 0;
        notifier.Requested += () => raised++;

        var effects = new Effects(
            appliedFilter, rawEventStore, filterPaneState, lensState,
            notifier, new SetFilterDateRangeSucceededNotifier(Substitute.For<ITraceLogger>()));

        await effects.HandleClearAllFilters(Substitute.For<IDispatcher>());
        await effects.HandleClearAllFilters(Substitute.For<IDispatcher>());

        Assert.Equal(2, raised);
    }

    [Fact]
    public async Task HandleClearAllFilters_ShouldUpdateEventTableFilters()
    {
        var (effects, mockDispatcher) = CreateEffects(
            true,
            appliedFilter: new Filter(null, CreateSingleEnabledFilters()));

        await effects.HandleClearAllFilters(mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task HandleMergeFilters_ShouldUpdateEventTableFilters()
    {
        var (effects, mockDispatcher) = CreateEffects(true, CreateSingleEnabledFilters());

        await effects.HandleMergeFilters(mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task HandleRemoveAdvancedFilter_ShouldUpdateEventTableFilters()
    {
        var (effects, mockDispatcher) = CreateEffects(
            true,
            appliedFilter: new Filter(null, CreateSingleEnabledFilters()));

        await effects.HandleRemoveAdvancedFilter(mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task HandleSetFilterDateRangeSuccess_RaisesTheNotifierOncePerDispatch()
    {
        var filterPaneState = Substitute.For<IState<FilterPaneState>>();
        filterPaneState.Value.Returns(new FilterPaneState());
        var appliedFilter = Substitute.For<IStateSelection<EventLogState, Filter>>();
        appliedFilter.Value.Returns(new Filter(null, []));
        var rawEventStore = Substitute.For<IState<RawEventStoreState>>();
        rawEventStore.Value.Returns(new RawEventStoreState());
        var lensState = Substitute.For<IState<FilterLensState>>();
        lensState.Value.Returns(new FilterLensState());

        var notifier = new SetFilterDateRangeSucceededNotifier(Substitute.For<ITraceLogger>());
        var raised = 0;
        notifier.Succeeded += () => raised++;

        var effects = new Effects(
            appliedFilter, rawEventStore, filterPaneState, lensState,
            new ClearAllFiltersNotifier(Substitute.For<ITraceLogger>()), notifier);

        await effects.HandleSetFilterDateRangeSuccess(Substitute.For<IDispatcher>());
        await effects.HandleSetFilterDateRangeSuccess(Substitute.For<IDispatcher>());

        Assert.Equal(2, raised);
    }

    [Fact]
    public async Task HandleSetFilterDateRangeSuccess_ShouldUpdateEventTableFilters()
    {
        var filterModel = FilterBuilder.CreateTestFilter(isEnabled: true);

        var (effects, mockDispatcher) = CreateEffects(true, ImmutableList.Create(filterModel));
        var action = new SetFilterAction(filterModel);

        await effects.HandleSetFilterDateRangeSuccess(mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task HandleSetFilterDateRange_WhenAfterIsNull_ShouldUseRangeFromActiveLogs()
    {
        var oldest = new DateTime(2024, 1, 1, 8, 30, 45, DateTimeKind.Utc);
        var newest = new DateTime(2024, 1, 1, 14, 15, 0, DateTimeKind.Utc);
        var unrelatedBefore = new DateTime(2024, 1, 1, 23, 0, 0, DateTimeKind.Utc);

        var events = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(timeCreated: newest),
            FilterEventBuilder.CreateTestEvent(timeCreated: oldest)
        };

        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);

        var (effects, mockDispatcher) = CreateEffects(logsWithEvents: [(logData, events)]);
        var action = new SetFilterDateRangeAction(new DateFilter { Before = unrelatedBefore });

        await effects.HandleSetFilterDateRange(action, mockDispatcher);

        var expectedAfter = new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        mockDispatcher.Received(1).Dispatch(Arg.Is<SetFilterDateRangeSuccessAction>(x =>
            x != null &&
            x.DateFilter != null &&
            x.DateFilter.After == expectedAfter &&
            x.DateFilter.Before == unrelatedBefore));
    }

    [Fact]
    public async Task HandleSetFilterDateRange_WhenBeforeIsNull_ShouldUseRangeFromActiveLogs()
    {
        var oldest = new DateTime(2024, 1, 1, 8, 30, 45, DateTimeKind.Utc);
        var newest = new DateTime(2024, 1, 1, 14, 15, 0, DateTimeKind.Utc);
        var unrelatedAfter = new DateTime(2023, 12, 1, 0, 0, 0, DateTimeKind.Utc);

        var events = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(timeCreated: newest),
            FilterEventBuilder.CreateTestEvent(timeCreated: oldest)
        };

        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel);

        var (effects, mockDispatcher) = CreateEffects(logsWithEvents: [(logData, events)]);
        var action = new SetFilterDateRangeAction(new DateFilter { After = unrelatedAfter });

        await effects.HandleSetFilterDateRange(action, mockDispatcher);

        var expectedBefore = new DateTime(2024, 1, 1, 15, 0, 0, DateTimeKind.Utc);
        mockDispatcher.Received(1).Dispatch(Arg.Is<SetFilterDateRangeSuccessAction>(x =>
            x != null &&
            x.DateFilter != null &&
            x.DateFilter.After == unrelatedAfter &&
            x.DateFilter.Before == expectedBefore));
    }

    [Fact]
    public async Task HandleSetFilterDateRange_WhenBothNullAcrossMultipleLogs_ShouldComputeRange()
    {
        var logAOldest = new DateTime(2024, 1, 1, 4, 0, 0, DateTimeKind.Utc);
        var logANewest = new DateTime(2024, 1, 1, 6, 0, 0, DateTimeKind.Utc);
        var logBOldest = new DateTime(2024, 1, 5, 20, 0, 0, DateTimeKind.Utc);
        var logBNewest = new DateTime(2024, 1, 5, 22, 0, 0, DateTimeKind.Utc);

        var eventsA = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(timeCreated: logANewest),
            FilterEventBuilder.CreateTestEvent(timeCreated: logAOldest)
        };

        var eventsB = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(timeCreated: logBNewest),
            FilterEventBuilder.CreateTestEvent(timeCreated: logBOldest)
        };

        var logA = new EventLogData(
            "LogA",
            LogPathType.Channel);
        var logB = new EventLogData(
            "LogB",
            LogPathType.Channel);

        var (effects, mockDispatcher) = CreateEffects(logsWithEvents: [(logA, eventsA), (logB, eventsB)]);
        var action = new SetFilterDateRangeAction(new DateFilter());

        await effects.HandleSetFilterDateRange(action, mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<SetFilterDateRangeSuccessAction>(x =>
            x != null &&
            x.DateFilter != null &&
            x.DateFilter.After == new DateTime(2024, 1, 1, 4, 0, 0, DateTimeKind.Utc) &&
            x.DateFilter.Before == new DateTime(2024, 1, 5, 22, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task HandleSetFilterDateRange_WhenBothProvided_ShouldUseProvidedValues()
    {
        var after = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var before = new DateTime(2024, 1, 1, 14, 0, 0, DateTimeKind.Utc);

        var (effects, mockDispatcher) = CreateEffects();
        var action = new SetFilterDateRangeAction(new DateFilter { After = after, Before = before });

        await effects.HandleSetFilterDateRange(action, mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<SetFilterDateRangeSuccessAction>(x =>
            x != null &&
            x.DateFilter != null &&
            x.DateFilter.After == after &&
            x.DateFilter.Before == before));
    }

    [Fact]
    public async Task HandleSetFilterDateRange_WhenExistingDateRangeHasAfter_ShouldUseExistingAfter()
    {
        var existingAfter = new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        var newBefore = new DateTime(2024, 1, 1, 16, 0, 0, DateTimeKind.Utc);

        var (effects, mockDispatcher) = CreateEffects(
            filteredDateRange: new DateFilter { After = existingAfter });

        var action = new SetFilterDateRangeAction(new DateFilter { Before = newBefore });

        await effects.HandleSetFilterDateRange(action, mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<SetFilterDateRangeSuccessAction>(x =>
            x != null &&
            x.DateFilter != null &&
            x.DateFilter.After == existingAfter &&
            x.DateFilter.Before == newBefore));
    }

    [Fact]
    public async Task HandleSetFilterDateRange_WhenExistingDateRangeHasBefore_ShouldUseExistingBefore()
    {
        var existingBefore = new DateTime(2024, 1, 1, 16, 0, 0, DateTimeKind.Utc);
        var newAfter = new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Utc);

        var (effects, mockDispatcher) = CreateEffects(
            filteredDateRange: new DateFilter { Before = existingBefore });

        var action = new SetFilterDateRangeAction(new DateFilter { After = newAfter });

        await effects.HandleSetFilterDateRange(action, mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<SetFilterDateRangeSuccessAction>(x =>
            x != null &&
            x.DateFilter != null &&
            x.DateFilter.After == newAfter &&
            x.DateFilter.Before == existingBefore));
    }

    [Fact]
    public async Task HandleSetFilterDateRange_WhenFilterDateModelIsNull_ShouldDispatchSuccessWithNull()
    {
        var (effects, mockDispatcher) = CreateEffects();
        var action = new SetFilterDateRangeAction(null);

        await effects.HandleSetFilterDateRange(action, mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<SetFilterDateRangeSuccessAction>(x =>
            x != null &&
            x.DateFilter == null));
    }

    [Fact]
    public async Task HandleSetFilter_ShouldUpdateEventTableFilters()
    {
        var filterModel = FilterBuilder.CreateTestFilter(isEnabled: true);

        var (effects, mockDispatcher) = CreateEffects(true, ImmutableList.Create(filterModel));
        var action = new SetFilterAction(filterModel);

        await effects.HandleSetFilter(action, mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task HandleSetFilter_WhenFilterHasBasicFilter_ShouldRecordFilterApplied()
    {
        var filterModel = FilterBuilder.CreateTestFilter(basicFilter: CreateBasicFilter());

        var (effects, mockDispatcher) = CreateEffects();
        var action = new SetFilterAction(filterModel);

        await effects.HandleSetFilter(action, mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<RecordFilterAppliedAction>(x =>
            x != null &&
            x.Filter.ComparisonText == FilterTestConstants.FilterIdEquals100));
    }

    [Fact]
    public async Task HandleSetFilter_WhenFilterHasNoBasicFilter_ShouldRecordFilterApplied()
    {
        var filterModel = FilterBuilder.CreateTestFilter();

        var (effects, mockDispatcher) = CreateEffects();
        var action = new SetFilterAction(filterModel);

        await effects.HandleSetFilter(action, mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<RecordFilterAppliedAction>(x =>
            x != null &&
            x.Filter.ComparisonText == FilterTestConstants.FilterIdEquals100));
    }

    [Fact]
    public async Task HandleToggleFilterDate_ShouldUpdateEventTableFilters()
    {
        var (effects, mockDispatcher) = CreateEffects(
            true,
            filteredDateRange: new DateFilter
            {
                After = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Before = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc)
            });

        await effects.HandleToggleFilterDate(mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task HandleToggleFilterEnabled_ShouldUpdateEventTableFilters()
    {
        var (effects, mockDispatcher) = CreateEffects(true, CreateSingleEnabledFilters());

        await effects.HandleToggleFilterEnabled(mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task HandleToggleFilterExcluded_ShouldUpdateEventTableFilters()
    {
        var (effects, mockDispatcher) = CreateEffects(true, CreateSingleEnabledFilters());

        await effects.HandleToggleFilterExcluded(mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task HandleToggleIsEnabled_ShouldUpdateEventTableFilters()
    {
        var (effects, mockDispatcher) = CreateEffects(true, CreateSingleEnabledFilters());

        await effects.HandleToggleIsEnabled(mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task UpdateEventTableFilters_WhenEquivalentFiltersFromDifferentInstances_ShouldNotDispatch()
    {
        var paneFilters = ImmutableList.Create(
            FilterBuilder.CreateTestFilter(isEnabled: true,
                isExcluded: false));

        var appliedFilters = ImmutableList.Create(
            FilterBuilder.CreateTestFilter(isEnabled: true,
                isExcluded: false));

        var (effects, mockDispatcher) = CreateEffects(
            true,
            paneFilters,
            appliedFilter: new Filter(null, appliedFilters));

        await effects.HandleToggleIsEnabled(mockDispatcher);

        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task UpdateEventTableFilters_WhenFilterPaneDisabled_ShouldOnlyKeepExcludedFilters()
    {
        var filters = ImmutableList.Create(
            FilterBuilder.CreateTestFilter(isEnabled: true,
                isExcluded: false),
            FilterBuilder.CreateTestFilter(
                FilterTestConstants.FilterLevelEqualsError,
                isEnabled: true,
                isExcluded: true));

        var (effects, mockDispatcher) = CreateEffects(false, filters);

        await effects.HandleToggleIsEnabled(mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<ApplyFilterAction>(x =>
            x != null &&
            x.Filter.Filters.Count == 1 &&
            x.Filter.Filters[0].IsExcluded));
    }

    [Fact]
    public async Task UpdateEventTableFilters_WhenFilterPaneEnabled_ShouldIncludeEnabledFilters()
    {
        var filters = ImmutableList.Create(
            FilterBuilder.CreateTestFilter(isEnabled: true,
                isExcluded: false),
            FilterBuilder.CreateTestFilter(
                FilterTestConstants.FilterLevelEqualsError,
                isEnabled: false,
                isExcluded: false));

        var (effects, mockDispatcher) = CreateEffects(true, filters);

        await effects.HandleToggleIsEnabled(mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<ApplyFilterAction>(x =>
            x != null &&
            x.Filter.Filters.Count == 1 &&
            x.Filter.Filters[0].ComparisonText == FilterTestConstants.FilterIdEquals100));
    }

    [Fact]
    public async Task UpdateEventTableFilters_WhenFilterUnchanged_ShouldNotDispatch()
    {
        var filters = CreateSingleEnabledFilters();
        var (effects, mockDispatcher) = CreateEffects(
            true,
            filters,
            appliedFilter: new Filter(null, filters));

        await effects.HandleToggleIsEnabled(mockDispatcher);

        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<ApplyFilterAction>());
    }

    private static BasicFilter CreateBasicFilter() =>
        new(
            new FilterComparison
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = FilterTestConstants.FilterValue100
            },
            []);

    private static (Effects effects, IDispatcher mockDispatcher) CreateEffects(
        bool isEnabled = false,
        ImmutableList<SavedFilter>? filters = null,
        DateFilter? filteredDateRange = null,
        IReadOnlyList<(EventLogData Log, IReadOnlyList<ResolvedEvent> Events)>? logsWithEvents = null,
        Filter? appliedFilter = null)
    {
        var mockFilterPaneState = Substitute.For<IState<FilterPaneState>>();

        mockFilterPaneState.Value.Returns(new FilterPaneState
        {
            IsEnabled = isEnabled,
            Filters = filters ?? ImmutableList<SavedFilter>.Empty,
            FilteredDateRange = filteredDateRange
        });

        var mockAppliedFilter = Substitute.For<IStateSelection<EventLogState, Filter>>();
        mockAppliedFilter.Value.Returns(appliedFilter ?? new Filter(null, []));

        var logs = logsWithEvents ?? [];
        var rawStore = new RawEventStoreState();

        foreach (var (logData, logEvents) in logs)
        {
            rawStore = RawEventStoreReducers.ReduceAddTable(rawStore, new AddTableAction(logData));
            rawStore = RawEventStoreReducers.ReduceIngestRawEvents(
                rawStore,
                new IngestRawEventsAction(
                    new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>> { [logData.Id] = logEvents },
                    RawIngestMode.Append));
        }

        var mockRawEventStore = Substitute.For<IState<RawEventStoreState>>();
        mockRawEventStore.Value.Returns(rawStore);

        var mockLensState = Substitute.For<IState<FilterLensState>>();
        mockLensState.Value.Returns(new FilterLensState());

        var effects = new Effects(mockAppliedFilter, mockRawEventStore, mockFilterPaneState, mockLensState, new ClearAllFiltersNotifier(Substitute.For<ITraceLogger>()), new SetFilterDateRangeSucceededNotifier(Substitute.For<ITraceLogger>()));
        var mockDispatcher = Substitute.For<IDispatcher>();

        return (effects, mockDispatcher);
    }

    private static ImmutableList<SavedFilter> CreateSingleEnabledFilters() =>
        ImmutableList.Create(
            FilterBuilder.CreateTestFilter(isEnabled: true));
}
