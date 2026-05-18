// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Common;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Filtering.Runtime;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterCache;
using EventLogExpert.Runtime.FilterGroup;
using EventLogExpert.Runtime.FilterProgress;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Tests.TestUtils;
using EventLogExpert.Runtime.Tests.TestUtils.Constants;
using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Filtering.TestUtils.Constants;
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
        // Arrange
        var filterModel = FilterFixtures.CreateTestFilter(isEnabled: true);

        var (effects, mockDispatcher) = CreateEffects(true, ImmutableList.Create(filterModel));
        var action = new AddFilterAction(filterModel);

        // Act
        await effects.HandleAddFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.Received().Dispatch(Arg.Any<ApplyFilterAction>());
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<SetFilterProgressAction>());
    }

    [Fact]
    public async Task HandleAddFilter_WhenComparisonValueIsNull_ShouldNotUpdateEventTableFilters()
    {
        // Arrange
        var filterModel = new SavedFilter
        {
            ComparisonText = string.Empty,
            Compiled = null
        };

        var (effects, mockDispatcher) = CreateEffects();
        var action = new AddFilterAction(filterModel);

        // Act
        await effects.HandleAddFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task HandleAddFilter_WhenFilterHasBasicFilter_ShouldAddToRecentFilters()
    {
        // Arrange
        var filterModel = FilterFixtures.CreateTestFilter(basicFilter: CreateBasicFilter());

        var (effects, mockDispatcher) = CreateEffects();
        var action = new AddFilterAction(filterModel);

        // Act
        await effects.HandleAddFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<AddRecentFilterAction>(x =>
            x.Filter == FilterTestConstants.FilterIdEquals100));
    }

    [Fact]
    public async Task HandleAddFilter_WhenFilterHasNoBasicFilter_ShouldAddToRecentFilters()
    {
        // Arrange
        var filterModel = FilterFixtures.CreateTestFilter();

        var (effects, mockDispatcher) = CreateEffects();
        var action = new AddFilterAction(filterModel);

        // Act
        await effects.HandleAddFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<AddRecentFilterAction>(x =>
            x.Filter == FilterTestConstants.FilterIdEquals100));
    }

    [Fact]
    public async Task HandleApplyFilterGroup_ShouldUpdateEventTableFilters()
    {
        // Arrange
        var (effects, mockDispatcher) = CreateEffects(true, CreateSingleEnabledFilters());

        // Act
        await effects.HandleApplyFilterGroup(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task HandleClearAllFilters_ShouldUpdateEventTableFilters()
    {
        // Arrange
        var (effects, mockDispatcher) = CreateEffects(
            true,
            appliedFilter: new Filter(null, CreateSingleEnabledFilters()));

        // Act
        await effects.HandleClearAllFilters(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task HandleRemoveAdvancedFilter_ShouldUpdateEventTableFilters()
    {
        // Arrange
        var (effects, mockDispatcher) = CreateEffects(
            true,
            appliedFilter: new Filter(null, CreateSingleEnabledFilters()));

        // Act
        await effects.HandleRemoveAdvancedFilter(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task HandleSaveFilterGroup_ShouldCopyFilterProperties()
    {
        // Arrange
        var filters = ImmutableList.Create(
            FilterFixtures.CreateTestFilter(
                FilterTestConstants.FilterIdEquals100,
                HighlightColor.Red,
                isExcluded: true));

        var (effects, mockDispatcher) = CreateEffects(filters: filters);
        var action = new SaveFilterGroupAction(FilterTestConstants.FilterGroupName);

        // Act
        await effects.HandleSaveFilterGroup(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<AddGroupAction>(x =>
            x.FilterGroup != null &&
            x.FilterGroup.Filters[0].Color == HighlightColor.Red &&
            x.FilterGroup.Filters[0].IsExcluded == true &&
            x.FilterGroup.Filters[0].ComparisonText == FilterTestConstants.FilterIdEquals100));
    }

    [Fact]
    public async Task HandleSaveFilterGroup_ShouldDispatchAddGroupAction()
    {
        // Arrange
        var filters = ImmutableList.Create(
            FilterFixtures.CreateTestFilter(
                FilterTestConstants.FilterIdEquals100,
                HighlightColor.Blue,
                isExcluded: false));

        var (effects, mockDispatcher) = CreateEffects(filters: filters);
        var action = new SaveFilterGroupAction(FilterTestConstants.FilterGroupName);

        // Act
        await effects.HandleSaveFilterGroup(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<AddGroupAction>(x =>
            x.FilterGroup != null &&
            x.FilterGroup.Name == FilterTestConstants.FilterGroupName &&
            x.FilterGroup.Filters.Count == 1));
    }

    [Fact]
    public async Task HandleSaveFilterGroup_WhenPaneEmpty_ShouldNotDispatchAddGroupAction()
    {
        // Arrange
        var (effects, mockDispatcher) = CreateEffects(filters: ImmutableList<SavedFilter>.Empty);
        var action = new SaveFilterGroupAction(FilterTestConstants.FilterGroupName);

        // Act
        await effects.HandleSaveFilterGroup(action, mockDispatcher);

        // Assert
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<AddGroupAction>());
    }

    [Fact]
    public async Task HandleSaveFilterGroup_WithMultipleFilters_ShouldSaveAll()
    {
        // Arrange
        var filters = ImmutableList.Create(
            FilterFixtures.CreateTestFilter(FilterTestConstants.FilterIdEquals100, HighlightColor.Blue),
            FilterFixtures.CreateTestFilter(FilterTestConstants.FilterLevelEqualsError, HighlightColor.Red));

        var (effects, mockDispatcher) = CreateEffects(filters: filters);
        var action = new SaveFilterGroupAction(FilterTestConstants.FilterGroupName);

        // Act
        await effects.HandleSaveFilterGroup(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<AddGroupAction>(x =>
            x.FilterGroup != null &&
            x.FilterGroup.Filters.Count == 2));
    }

    [Fact]
    public async Task HandleSetFilter_ShouldUpdateEventTableFilters()
    {
        // Arrange
        var filterModel = FilterFixtures.CreateTestFilter(isEnabled: true);

        var (effects, mockDispatcher) = CreateEffects(true, ImmutableList.Create(filterModel));
        var action = new SetFilterAction(filterModel);

        // Act
        await effects.HandleSetFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task HandleSetFilter_WhenFilterHasBasicFilter_ShouldAddToRecentFilters()
    {
        // Arrange
        var filterModel = FilterFixtures.CreateTestFilter(basicFilter: CreateBasicFilter());

        var (effects, mockDispatcher) = CreateEffects();
        var action = new SetFilterAction(filterModel);

        // Act
        await effects.HandleSetFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<AddRecentFilterAction>(x =>
            x.Filter == FilterTestConstants.FilterIdEquals100));
    }

    [Fact]
    public async Task HandleSetFilter_WhenFilterHasNoBasicFilter_ShouldAddToRecentFilters()
    {
        // Arrange
        var filterModel = FilterFixtures.CreateTestFilter();

        var (effects, mockDispatcher) = CreateEffects();
        var action = new SetFilterAction(filterModel);

        // Act
        await effects.HandleSetFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<AddRecentFilterAction>(x =>
            x.Filter == FilterTestConstants.FilterIdEquals100));
    }

    [Fact]
    public async Task HandleSetFilterDateRange_WhenAfterIsNull_ShouldUseRangeFromActiveLogs()
    {
        // Arrange
        var oldest = new DateTime(2024, 1, 1, 8, 30, 45, DateTimeKind.Utc);
        var newest = new DateTime(2024, 1, 1, 14, 15, 0, DateTimeKind.Utc);
        var unrelatedBefore = new DateTime(2024, 1, 1, 23, 0, 0, DateTimeKind.Utc);

        var events = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(timeCreated: newest),
            FilterEventBuilder.CreateTestEvent(timeCreated: oldest)
        };

        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, events);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher) = CreateEffects(activeLogs: activeLogs);
        var action = new SetFilterDateRangeAction(new DateFilter { Before = unrelatedBefore });

        // Act
        await effects.HandleSetFilterDateRange(action, mockDispatcher);

        // Assert
        var expectedAfter = new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        mockDispatcher.Received(1).Dispatch(Arg.Is<SetFilterDateRangeSuccessAction>(x =>
            x.DateFilter != null &&
            x.DateFilter.After == expectedAfter &&
            x.DateFilter.Before == unrelatedBefore));
    }

    [Fact]
    public async Task HandleSetFilterDateRange_WhenBeforeIsNull_ShouldUseRangeFromActiveLogs()
    {
        // Arrange
        var oldest = new DateTime(2024, 1, 1, 8, 30, 45, DateTimeKind.Utc);
        var newest = new DateTime(2024, 1, 1, 14, 15, 0, DateTimeKind.Utc);
        var unrelatedAfter = new DateTime(2023, 12, 1, 0, 0, 0, DateTimeKind.Utc);

        var events = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(timeCreated: newest),
            FilterEventBuilder.CreateTestEvent(timeCreated: oldest)
        };

        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, events);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher) = CreateEffects(activeLogs: activeLogs);
        var action = new SetFilterDateRangeAction(new DateFilter { After = unrelatedAfter });

        // Act
        await effects.HandleSetFilterDateRange(action, mockDispatcher);

        // Assert
        var expectedBefore = new DateTime(2024, 1, 1, 15, 0, 0, DateTimeKind.Utc);
        mockDispatcher.Received(1).Dispatch(Arg.Is<SetFilterDateRangeSuccessAction>(x =>
            x.DateFilter != null &&
            x.DateFilter.After == unrelatedAfter &&
            x.DateFilter.Before == expectedBefore));
    }

    [Fact]
    public async Task HandleSetFilterDateRange_WhenBothNullAcrossMultipleLogs_ShouldComputeRange()
    {
        // Arrange
        var logAOldest = new DateTime(2024, 1, 1, 4, 0, 0, DateTimeKind.Utc);
        var logANewest = new DateTime(2024, 1, 1, 6, 0, 0, DateTimeKind.Utc);
        var logBOldest = new DateTime(2024, 1, 5, 20, 0, 0, DateTimeKind.Utc);
        var logBNewest = new DateTime(2024, 1, 5, 22, 0, 0, DateTimeKind.Utc);

        var logA = new EventLogData(
            "LogA",
            LogPathType.Channel,
            [FilterEventBuilder.CreateTestEvent(timeCreated: logANewest), FilterEventBuilder.CreateTestEvent(timeCreated: logAOldest)]);
        var logB = new EventLogData(
            "LogB",
            LogPathType.Channel,
            [FilterEventBuilder.CreateTestEvent(timeCreated: logBNewest), FilterEventBuilder.CreateTestEvent(timeCreated: logBOldest)]);

        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty
            .Add("LogA", logA)
            .Add("LogB", logB);

        var (effects, mockDispatcher) = CreateEffects(activeLogs: activeLogs);
        var action = new SetFilterDateRangeAction(new DateFilter());

        // Act
        await effects.HandleSetFilterDateRange(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<SetFilterDateRangeSuccessAction>(x =>
            x.DateFilter != null &&
            x.DateFilter.After == new DateTime(2024, 1, 1, 4, 0, 0, DateTimeKind.Utc) &&
            x.DateFilter.Before == new DateTime(2024, 1, 5, 22, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task HandleSetFilterDateRange_WhenBothProvided_ShouldUseProvidedValues()
    {
        // Arrange
        var after = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var before = new DateTime(2024, 1, 1, 14, 0, 0, DateTimeKind.Utc);

        var (effects, mockDispatcher) = CreateEffects();
        var action = new SetFilterDateRangeAction(new DateFilter { After = after, Before = before });

        // Act
        await effects.HandleSetFilterDateRange(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<SetFilterDateRangeSuccessAction>(x =>
            x.DateFilter != null &&
            x.DateFilter.After == after &&
            x.DateFilter.Before == before));
    }

    [Fact]
    public async Task HandleSetFilterDateRange_WhenExistingDateRangeHasAfter_ShouldUseExistingAfter()
    {
        // Arrange
        var existingAfter = new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        var newBefore = new DateTime(2024, 1, 1, 16, 0, 0, DateTimeKind.Utc);

        var (effects, mockDispatcher) = CreateEffects(
            filteredDateRange: new DateFilter { After = existingAfter });

        var action = new SetFilterDateRangeAction(new DateFilter { Before = newBefore });

        // Act
        await effects.HandleSetFilterDateRange(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<SetFilterDateRangeSuccessAction>(x =>
            x.DateFilter != null &&
            x.DateFilter.After == existingAfter &&
            x.DateFilter.Before == newBefore));
    }

    [Fact]
    public async Task HandleSetFilterDateRange_WhenExistingDateRangeHasBefore_ShouldUseExistingBefore()
    {
        // Arrange
        var existingBefore = new DateTime(2024, 1, 1, 16, 0, 0, DateTimeKind.Utc);
        var newAfter = new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Utc);

        var (effects, mockDispatcher) = CreateEffects(
            filteredDateRange: new DateFilter { Before = existingBefore });

        var action = new SetFilterDateRangeAction(new DateFilter { After = newAfter });

        // Act
        await effects.HandleSetFilterDateRange(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<SetFilterDateRangeSuccessAction>(x =>
            x.DateFilter != null &&
            x.DateFilter.After == newAfter &&
            x.DateFilter.Before == existingBefore));
    }

    [Fact]
    public async Task HandleSetFilterDateRange_WhenFilterDateModelIsNull_ShouldDispatchSuccessWithNull()
    {
        // Arrange
        var (effects, mockDispatcher) = CreateEffects();
        var action = new SetFilterDateRangeAction(null);

        // Act
        await effects.HandleSetFilterDateRange(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<SetFilterDateRangeSuccessAction>(x =>
            x.DateFilter == null));
    }

    [Fact]
    public async Task HandleSetFilterDateRangeSuccess_ShouldUpdateEventTableFilters()
    {
        // Arrange
        var filterModel = FilterFixtures.CreateTestFilter(isEnabled: true);

        var (effects, mockDispatcher) = CreateEffects(true, ImmutableList.Create(filterModel));
        var action = new SetFilterAction(filterModel);

        // Act
        await effects.HandleSetFilterDateRangeSuccess(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task HandleToggleFilterDate_ShouldUpdateEventTableFilters()
    {
        // Arrange
        var (effects, mockDispatcher) = CreateEffects(
            true,
            filteredDateRange: new DateFilter
            {
                After = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Before = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc)
            });

        // Act
        await effects.HandleToggleFilterDate(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task HandleToggleFilterEnabled_ShouldUpdateEventTableFilters()
    {
        // Arrange
        var (effects, mockDispatcher) = CreateEffects(true, CreateSingleEnabledFilters());

        // Act
        await effects.HandleToggleFilterEnabled(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task HandleToggleFilterExcluded_ShouldUpdateEventTableFilters()
    {
        // Arrange
        var (effects, mockDispatcher) = CreateEffects(true, CreateSingleEnabledFilters());

        // Act
        await effects.HandleToggleFilterExcluded(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task HandleToggleIsEnabled_ShouldUpdateEventTableFilters()
    {
        // Arrange
        var (effects, mockDispatcher) = CreateEffects(true, CreateSingleEnabledFilters());

        // Act
        await effects.HandleToggleIsEnabled(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task UpdateEventTableFilters_WhenEquivalentFiltersFromDifferentInstances_ShouldNotDispatch()
    {
        // Arrange
        var paneFilters = ImmutableList.Create(
            FilterFixtures.CreateTestFilter(isEnabled: true,
                isExcluded: false));

        var appliedFilters = ImmutableList.Create(
            FilterFixtures.CreateTestFilter(isEnabled: true,
                isExcluded: false));

        var (effects, mockDispatcher) = CreateEffects(
            true,
            paneFilters,
            appliedFilter: new Filter(null, appliedFilters));

        // Act
        await effects.HandleToggleIsEnabled(mockDispatcher);

        // Assert
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task UpdateEventTableFilters_WhenFilterPaneDisabled_ShouldOnlyKeepExcludedFilters()
    {
        // Arrange
        var filters = ImmutableList.Create(
            FilterFixtures.CreateTestFilter(isEnabled: true,
                isExcluded: false),
            FilterFixtures.CreateTestFilter(
                FilterTestConstants.FilterLevelEqualsError,
                isEnabled: true,
                isExcluded: true));

        var (effects, mockDispatcher) = CreateEffects(false, filters);

        // Act
        await effects.HandleToggleIsEnabled(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<ApplyFilterAction>(x =>
            x.Filter.Filters.Count == 1 &&
            x.Filter.Filters[0].IsExcluded));
    }

    [Fact]
    public async Task UpdateEventTableFilters_WhenFilterPaneEnabled_ShouldIncludeEnabledFilters()
    {
        // Arrange
        var filters = ImmutableList.Create(
            FilterFixtures.CreateTestFilter(isEnabled: true,
                isExcluded: false),
            FilterFixtures.CreateTestFilter(
                FilterTestConstants.FilterLevelEqualsError,
                isEnabled: false,
                isExcluded: false));

        var (effects, mockDispatcher) = CreateEffects(true, filters);

        // Act
        await effects.HandleToggleIsEnabled(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<ApplyFilterAction>(x =>
            x.Filter.Filters.Count == 1 &&
            x.Filter.Filters[0].ComparisonText == FilterTestConstants.FilterIdEquals100));
    }

    [Fact]
    public async Task UpdateEventTableFilters_WhenFilterUnchanged_ShouldNotDispatch()
    {
        // Arrange
        var filters = CreateSingleEnabledFilters();
        var (effects, mockDispatcher) = CreateEffects(
            true,
            filters,
            appliedFilter: new Filter(null, filters));

        // Act
        await effects.HandleToggleIsEnabled(mockDispatcher);

        // Assert
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<SetFilterProgressAction>());
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
        ImmutableDictionary<string, EventLogData>? activeLogs = null,
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

        var mockEventDateRange =
            Substitute.For<IStateSelection<EventLogState, (DateTime After, DateTime Before)?>>();

        var logs = activeLogs ?? ImmutableDictionary<string, EventLogData>.Empty;
        mockEventDateRange.Value.Returns(logs.Values.TryGetEventDateRange(out var range) ? range : null);

        var effects = new Effects(mockAppliedFilter, mockEventDateRange, mockFilterPaneState);
        var mockDispatcher = Substitute.For<IDispatcher>();

        return (effects, mockDispatcher);
    }

    private static ImmutableList<SavedFilter> CreateSingleEnabledFilters() =>
        ImmutableList.Create(
            FilterFixtures.CreateTestFilter(isEnabled: true));
}
