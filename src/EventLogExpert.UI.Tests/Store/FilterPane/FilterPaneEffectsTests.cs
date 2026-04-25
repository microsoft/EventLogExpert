// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.FilterCache;
using EventLogExpert.UI.Store.FilterGroup;
using EventLogExpert.UI.Store.FilterPane;
using EventLogExpert.UI.Tests.TestUtils;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.Store.FilterPane;

public sealed class FilterPaneEffectsTests
{
    [Fact]
    public async Task HandleAddFilter_WhenComparisonValueExists_ShouldUpdateEventTableFilters()
    {
        // Arrange
        var filterModel = FilterUtils.CreateTestFilter(Constants.FilterIdEquals100, isEnabled: true);

        var (effects, mockDispatcher) = CreateEffects(true, ImmutableList.Create(filterModel));
        var action = new FilterPaneAction.AddFilter(filterModel);

        // Act
        await effects.HandleAddFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.Received().Dispatch(Arg.Any<EventLogAction.SetFilters>());
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<FilterPaneAction.SetIsLoading>());
    }

    [Fact]
    public async Task HandleAddFilter_WhenComparisonValueIsNull_ShouldNotUpdateEventTableFilters()
    {
        // Arrange — empty comparison cannot compile, so the placeholder filter has no Compiled artifact
        // and the effect must skip dispatching SetFilters.
        var filterModel = new FilterModel
        {
            ComparisonText = string.Empty,
            Compiled = null
        };

        var (effects, mockDispatcher) = CreateEffects();
        var action = new FilterPaneAction.AddFilter(filterModel);

        // Act
        await effects.HandleAddFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<EventLogAction.SetFilters>());
    }

    [Fact]
    public async Task HandleAddFilter_WhenFilterIsCached_ShouldNotAddToRecentFilters()
    {
        // Arrange
        var filterModel = FilterUtils.CreateTestFilter(Constants.FilterIdEquals100, FilterType.Cached);

        var (effects, mockDispatcher) = CreateEffects();
        var action = new FilterPaneAction.AddFilter(filterModel);

        // Act
        await effects.HandleAddFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<FilterCacheAction.AddRecentFilter>());
    }

    [Fact]
    public async Task HandleAddFilter_WhenFilterIsNotCached_ShouldAddToRecentFilters()
    {
        // Arrange
        var filterModel = FilterUtils.CreateTestFilter(Constants.FilterIdEquals100, FilterType.Advanced);

        var (effects, mockDispatcher) = CreateEffects();
        var action = new FilterPaneAction.AddFilter(filterModel);

        // Act
        await effects.HandleAddFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<FilterCacheAction.AddRecentFilter>(x =>
            x.Filter == Constants.FilterIdEquals100));
    }

    [Fact]
    public async Task HandleApplyFilterGroup_ShouldUpdateEventTableFilters()
    {
        // Arrange
        var (effects, mockDispatcher) = CreateEffects(true, CreateSingleEnabledFilters());

        // Act
        await effects.HandleApplyFilterGroup(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Any<EventLogAction.SetFilters>());
    }

    [Fact]
    public async Task HandleClearAllFilters_ShouldUpdateEventTableFilters()
    {
        // Effect must dispatch even when pane state is empty if applied still has filters.
        var (effects, mockDispatcher) = CreateEffects(
            isEnabled: true,
            appliedFilter: new EventFilter(null, CreateSingleEnabledFilters()));

        // Act
        await effects.HandleClearAllFilters(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Any<EventLogAction.SetFilters>());
    }

    [Fact]
    public async Task HandleRemoveAdvancedFilter_ShouldUpdateEventTableFilters()
    {
        // Pane state empty, applied still has the removed filter.
        var (effects, mockDispatcher) = CreateEffects(
            isEnabled: true,
            appliedFilter: new EventFilter(null, CreateSingleEnabledFilters()));

        // Act
        await effects.HandleRemoveAdvancedFilter(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Any<EventLogAction.SetFilters>());
    }

    [Fact]
    public async Task HandleSaveFilterGroup_ShouldCopyFilterProperties()
    {
        // Arrange
        var filters = ImmutableList.Create(
            FilterUtils.CreateTestFilter(
                Constants.FilterIdEquals100,
                color: HighlightColor.Red,
                isExcluded: true));

        var (effects, mockDispatcher) = CreateEffects(filters: filters);
        var action = new FilterPaneAction.SaveFilterGroup(Constants.FilterGroupName);

        // Act
        await effects.HandleSaveFilterGroup(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<FilterGroupAction.AddGroup>(x =>
            x.FilterGroup != null &&
            x.FilterGroup.Filters[0].Color == HighlightColor.Red &&
            x.FilterGroup.Filters[0].IsExcluded == true &&
            x.FilterGroup.Filters[0].ComparisonText == Constants.FilterIdEquals100));
    }

    [Fact]
    public async Task HandleSaveFilterGroup_ShouldDispatchAddGroupAction()
    {
        // Arrange
        var filters = ImmutableList.Create(
            FilterUtils.CreateTestFilter(
                Constants.FilterIdEquals100,
                color: HighlightColor.Blue,
                isExcluded: false));

        var (effects, mockDispatcher) = CreateEffects(filters: filters);
        var action = new FilterPaneAction.SaveFilterGroup(Constants.FilterGroupName);

        // Act
        await effects.HandleSaveFilterGroup(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<FilterGroupAction.AddGroup>(x =>
            x.FilterGroup != null &&
            x.FilterGroup.Name == Constants.FilterGroupName &&
            x.FilterGroup.Filters.Count == 1));
    }

    [Fact]
    public async Task HandleSaveFilterGroup_WithMultipleFilters_ShouldSaveAll()
    {
        // Arrange
        var filters = ImmutableList.Create(
            FilterUtils.CreateTestFilter(Constants.FilterIdEquals100, color: HighlightColor.Blue),
            FilterUtils.CreateTestFilter(Constants.FilterLevelEqualsError, color: HighlightColor.Red));

        var (effects, mockDispatcher) = CreateEffects(filters: filters);
        var action = new FilterPaneAction.SaveFilterGroup(Constants.FilterGroupName);

        // Act
        await effects.HandleSaveFilterGroup(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<FilterGroupAction.AddGroup>(x =>
            x.FilterGroup != null &&
            x.FilterGroup.Filters.Count == 2));
    }

    [Fact]
    public async Task HandleSetFilter_ShouldUpdateEventTableFilters()
    {
        // Arrange
        var filterModel = FilterUtils.CreateTestFilter(Constants.FilterIdEquals100, isEnabled: true);

        var (effects, mockDispatcher) = CreateEffects(true, ImmutableList.Create(filterModel));
        var action = new FilterPaneAction.SetFilter(filterModel);

        // Act
        await effects.HandleSetFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Any<EventLogAction.SetFilters>());
    }

    [Fact]
    public async Task HandleSetFilter_WhenFilterIsCached_ShouldNotAddToRecentFilters()
    {
        // Arrange
        var filterModel = FilterUtils.CreateTestFilter(Constants.FilterIdEquals100, FilterType.Cached);

        var (effects, mockDispatcher) = CreateEffects();
        var action = new FilterPaneAction.SetFilter(filterModel);

        // Act
        await effects.HandleSetFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<FilterCacheAction.AddRecentFilter>());
    }

    [Fact]
    public async Task HandleSetFilter_WhenFilterIsNotCached_ShouldAddToRecentFilters()
    {
        // Arrange
        var filterModel = FilterUtils.CreateTestFilter(Constants.FilterIdEquals100, FilterType.Advanced);

        var (effects, mockDispatcher) = CreateEffects();
        var action = new FilterPaneAction.SetFilter(filterModel);

        // Act
        await effects.HandleSetFilter(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<FilterCacheAction.AddRecentFilter>(x =>
            x.Filter == Constants.FilterIdEquals100));
    }

    [Fact]
    public async Task HandleSetFilterDateRange_WhenAfterIsNull_ShouldUseEnvelopeFromActiveLogs()
    {
        // Arrange
        var oldest = new DateTime(2024, 1, 1, 8, 30, 45, DateTimeKind.Utc);
        var newest = new DateTime(2024, 1, 1, 14, 15, 0, DateTimeKind.Utc);
        var unrelatedBefore = new DateTime(2024, 1, 1, 23, 0, 0, DateTimeKind.Utc);

        var events = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(timeCreated: newest),
            EventUtils.CreateTestEvent(timeCreated: oldest)
        };

        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, events);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher) = CreateEffects(activeLogs: activeLogs);
        var action = new FilterPaneAction.SetFilterDateRange(new FilterDateModel { Before = unrelatedBefore });

        // Act
        await effects.HandleSetFilterDateRange(action, mockDispatcher);

        // Assert
        var expectedAfter = new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        mockDispatcher.Received(1).Dispatch(Arg.Is<FilterPaneAction.SetFilterDateRangeSuccess>(x =>
            x.FilterDateModel != null &&
            x.FilterDateModel.After == expectedAfter &&
            x.FilterDateModel.Before == unrelatedBefore));
    }

    [Fact]
    public async Task HandleSetFilterDateRange_WhenBeforeIsNull_ShouldUseEnvelopeFromActiveLogs()
    {
        // Arrange
        var oldest = new DateTime(2024, 1, 1, 8, 30, 45, DateTimeKind.Utc);
        var newest = new DateTime(2024, 1, 1, 14, 15, 0, DateTimeKind.Utc);
        var unrelatedAfter = new DateTime(2023, 12, 1, 0, 0, 0, DateTimeKind.Utc);

        var events = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(timeCreated: newest),
            EventUtils.CreateTestEvent(timeCreated: oldest)
        };

        var logData = new EventLogData(Constants.LogNameTestLog, PathType.LogName, events);
        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty.Add(Constants.LogNameTestLog, logData);

        var (effects, mockDispatcher) = CreateEffects(activeLogs: activeLogs);
        var action = new FilterPaneAction.SetFilterDateRange(new FilterDateModel { After = unrelatedAfter });

        // Act
        await effects.HandleSetFilterDateRange(action, mockDispatcher);

        // Assert
        var expectedBefore = new DateTime(2024, 1, 1, 15, 0, 0, DateTimeKind.Utc);
        mockDispatcher.Received(1).Dispatch(Arg.Is<FilterPaneAction.SetFilterDateRangeSuccess>(x =>
            x.FilterDateModel != null &&
            x.FilterDateModel.After == unrelatedAfter &&
            x.FilterDateModel.Before == expectedBefore));
    }

    [Fact]
    public async Task HandleSetFilterDateRange_WhenBothNullAcrossMultipleLogs_ShouldComputeEnvelope()
    {
        // Logs A and B don't overlap; envelope must span both, and intersection (the previous bug)
        // would have inverted After/Before.
        var logAOldest = new DateTime(2024, 1, 1, 4, 0, 0, DateTimeKind.Utc);
        var logANewest = new DateTime(2024, 1, 1, 6, 0, 0, DateTimeKind.Utc);
        var logBOldest = new DateTime(2024, 1, 5, 20, 0, 0, DateTimeKind.Utc);
        var logBNewest = new DateTime(2024, 1, 5, 22, 0, 0, DateTimeKind.Utc);

        var logA = new EventLogData(
            "LogA",
            PathType.LogName,
            [EventUtils.CreateTestEvent(timeCreated: logANewest), EventUtils.CreateTestEvent(timeCreated: logAOldest)]);
        var logB = new EventLogData(
            "LogB",
            PathType.LogName,
            [EventUtils.CreateTestEvent(timeCreated: logBNewest), EventUtils.CreateTestEvent(timeCreated: logBOldest)]);

        var activeLogs = ImmutableDictionary<string, EventLogData>.Empty
            .Add("LogA", logA)
            .Add("LogB", logB);

        var (effects, mockDispatcher) = CreateEffects(activeLogs: activeLogs);
        var action = new FilterPaneAction.SetFilterDateRange(new FilterDateModel());

        // Act
        await effects.HandleSetFilterDateRange(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<FilterPaneAction.SetFilterDateRangeSuccess>(x =>
            x.FilterDateModel != null &&
            x.FilterDateModel.After == new DateTime(2024, 1, 1, 4, 0, 0, DateTimeKind.Utc) &&
            x.FilterDateModel.Before == new DateTime(2024, 1, 5, 22, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task HandleSetFilterDateRange_WhenBothProvided_ShouldUseProvidedValues()
    {
        // Arrange
        var after = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var before = new DateTime(2024, 1, 1, 14, 0, 0, DateTimeKind.Utc);

        var (effects, mockDispatcher) = CreateEffects();
        var action = new FilterPaneAction.SetFilterDateRange(new FilterDateModel { After = after, Before = before });

        // Act
        await effects.HandleSetFilterDateRange(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<FilterPaneAction.SetFilterDateRangeSuccess>(x =>
            x.FilterDateModel != null &&
            x.FilterDateModel.After == after &&
            x.FilterDateModel.Before == before));
    }

    [Fact]
    public async Task HandleSetFilterDateRange_WhenExistingDateRangeHasAfter_ShouldUseExistingAfter()
    {
        // Arrange
        var existingAfter = new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        var newBefore = new DateTime(2024, 1, 1, 16, 0, 0, DateTimeKind.Utc);

        var (effects, mockDispatcher) = CreateEffects(
            filteredDateRange: new FilterDateModel { After = existingAfter });

        var action = new FilterPaneAction.SetFilterDateRange(new FilterDateModel { Before = newBefore });

        // Act
        await effects.HandleSetFilterDateRange(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<FilterPaneAction.SetFilterDateRangeSuccess>(x =>
            x.FilterDateModel != null &&
            x.FilterDateModel.After == existingAfter &&
            x.FilterDateModel.Before == newBefore));
    }

    [Fact]
    public async Task HandleSetFilterDateRange_WhenExistingDateRangeHasBefore_ShouldUseExistingBefore()
    {
        // Arrange
        var existingBefore = new DateTime(2024, 1, 1, 16, 0, 0, DateTimeKind.Utc);
        var newAfter = new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Utc);

        var (effects, mockDispatcher) = CreateEffects(
            filteredDateRange: new FilterDateModel { Before = existingBefore });

        var action = new FilterPaneAction.SetFilterDateRange(new FilterDateModel { After = newAfter });

        // Act
        await effects.HandleSetFilterDateRange(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<FilterPaneAction.SetFilterDateRangeSuccess>(x =>
            x.FilterDateModel != null &&
            x.FilterDateModel.After == newAfter &&
            x.FilterDateModel.Before == existingBefore));
    }

    [Fact]
    public async Task HandleSetFilterDateRange_WhenFilterDateModelIsNull_ShouldDispatchSuccessWithNull()
    {
        // Arrange
        var (effects, mockDispatcher) = CreateEffects();
        var action = new FilterPaneAction.SetFilterDateRange(null);

        // Act
        await effects.HandleSetFilterDateRange(action, mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<FilterPaneAction.SetFilterDateRangeSuccess>(x =>
            x.FilterDateModel == null));
    }

    [Fact]
    public async Task HandleSetFilterDateRangeSuccess_ShouldUpdateEventTableFilters()
    {
        // Arrange
        var (effects, mockDispatcher) = CreateEffects(
            isEnabled: true,
            filteredDateRange: new FilterDateModel
            {
                After = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Before = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc)
            });

        // Act
        await effects.HandleSetFilterDateRangeSuccess(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Any<EventLogAction.SetFilters>());
    }

    [Fact]
    public async Task HandleToggleFilterDate_ShouldUpdateEventTableFilters()
    {
        // Arrange
        var (effects, mockDispatcher) = CreateEffects(
            isEnabled: true,
            filteredDateRange: new FilterDateModel
            {
                After = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Before = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc)
            });

        // Act
        await effects.HandleToggleFilterDate(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Any<EventLogAction.SetFilters>());
    }

    [Fact]
    public async Task HandleToggleFilterEnabled_ShouldUpdateEventTableFilters()
    {
        // Arrange
        var (effects, mockDispatcher) = CreateEffects(true, CreateSingleEnabledFilters());

        // Act
        await effects.HandleToggleFilterEnabled(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Any<EventLogAction.SetFilters>());
    }

    [Fact]
    public async Task HandleToggleFilterExcluded_ShouldUpdateEventTableFilters()
    {
        // Arrange
        var (effects, mockDispatcher) = CreateEffects(true, CreateSingleEnabledFilters());

        // Act
        await effects.HandleToggleFilterExcluded(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Any<EventLogAction.SetFilters>());
    }

    [Fact]
    public async Task HandleToggleIsEnabled_ShouldUpdateEventTableFilters()
    {
        // Arrange
        var (effects, mockDispatcher) = CreateEffects(true, CreateSingleEnabledFilters());

        // Act
        await effects.HandleToggleIsEnabled(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Any<EventLogAction.SetFilters>());
    }

    [Fact]
    public async Task UpdateEventTableFilters_WhenEquivalentFiltersFromDifferentInstances_ShouldNotDispatch()
    {
        // Structurally equivalent but distinct instances; HasFilteringChanged must short-circuit.
        var paneFilters = ImmutableList.Create(
            FilterUtils.CreateTestFilter(
                Constants.FilterIdEquals100,
                isEnabled: true,
                isExcluded: false));

        var appliedFilters = ImmutableList.Create(
            FilterUtils.CreateTestFilter(
                Constants.FilterIdEquals100,
                isEnabled: true,
                isExcluded: false));

        var (effects, mockDispatcher) = CreateEffects(
            isEnabled: true,
            filters: paneFilters,
            appliedFilter: new EventFilter(null, appliedFilters));

        // Act
        await effects.HandleToggleIsEnabled(mockDispatcher);

        // Assert
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<EventLogAction.SetFilters>());
    }

    [Fact]
    public async Task UpdateEventTableFilters_WhenFilterPaneDisabled_ShouldOnlyKeepExcludedFilters()
    {
        // Arrange
        var filters = ImmutableList.Create(
            FilterUtils.CreateTestFilter(
                Constants.FilterIdEquals100,
                isEnabled: true,
                isExcluded: false),
            FilterUtils.CreateTestFilter(
                Constants.FilterLevelEqualsError,
                isEnabled: true,
                isExcluded: true));

        var (effects, mockDispatcher) = CreateEffects(false, filters);

        // Act
        await effects.HandleToggleIsEnabled(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<EventLogAction.SetFilters>(x =>
            x.EventFilter.Filters.Count == 1 &&
            x.EventFilter.Filters[0].IsExcluded));
    }

    [Fact]
    public async Task UpdateEventTableFilters_WhenFilterPaneEnabled_ShouldIncludeEnabledFilters()
    {
        // Arrange
        var filters = ImmutableList.Create(
            FilterUtils.CreateTestFilter(
                Constants.FilterIdEquals100,
                isEnabled: true,
                isExcluded: false),
            FilterUtils.CreateTestFilter(
                Constants.FilterLevelEqualsError,
                isEnabled: false,
                isExcluded: false));

        var (effects, mockDispatcher) = CreateEffects(true, filters);

        // Act
        await effects.HandleToggleIsEnabled(mockDispatcher);

        // Assert
        mockDispatcher.Received(1).Dispatch(Arg.Is<EventLogAction.SetFilters>(x =>
            x.EventFilter.Filters.Count == 1 &&
            x.EventFilter.Filters[0].ComparisonText == Constants.FilterIdEquals100));
    }

    [Fact]
    public async Task UpdateEventTableFilters_WhenFilterUnchanged_ShouldNotDispatch()
    {
        // No-op guard must skip both loading toggles and the SetFilters dispatch.
        var filters = CreateSingleEnabledFilters();
        var (effects, mockDispatcher) = CreateEffects(
            isEnabled: true,
            filters: filters,
            appliedFilter: new EventFilter(null, filters));

        // Act
        await effects.HandleToggleIsEnabled(mockDispatcher);

        // Assert
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<FilterPaneAction.SetIsLoading>());
        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<EventLogAction.SetFilters>());
    }

    private static (FilterPaneEffects effects, IDispatcher mockDispatcher) CreateEffects(
        bool isEnabled = false,
        ImmutableList<FilterModel>? filters = null,
        FilterDateModel? filteredDateRange = null,
        ImmutableDictionary<string, EventLogData>? activeLogs = null,
        EventFilter? appliedFilter = null)
    {
        var mockFilterPaneState = Substitute.For<IState<FilterPaneState>>();

        mockFilterPaneState.Value.Returns(new FilterPaneState
        {
            IsEnabled = isEnabled,
            Filters = filters ?? ImmutableList<FilterModel>.Empty,
            FilteredDateRange = filteredDateRange
        });

        var mockEventLogState = Substitute.For<IState<EventLogState>>();

        mockEventLogState.Value.Returns(new EventLogState
        {
            ActiveLogs = activeLogs ?? ImmutableDictionary<string, EventLogData>.Empty,
            AppliedFilter = appliedFilter ?? new EventFilter(null, [])
        });

        var effects = new FilterPaneEffects(mockEventLogState, mockFilterPaneState);
        var mockDispatcher = Substitute.For<IDispatcher>();

        return (effects, mockDispatcher);
    }

    private static ImmutableList<FilterModel> CreateSingleEnabledFilters() =>
        ImmutableList.Create(
            FilterUtils.CreateTestFilter(Constants.FilterIdEquals100, isEnabled: true));
}
