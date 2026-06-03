// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Filtering.TestUtils.Constants;
using EventLogExpert.Runtime.FilterPane;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.FilterPane;

public sealed class FilterPaneStateTests
{
    [Fact]
    public void FilterPaneState_DefaultState_HasNoFilters()
    {
        // Arrange + Act
        var state = new FilterPaneState();

        // Assert
        Assert.Empty(state.Filters);
    }

    [Fact]
    public void FilterPaneState_DefaultState_ShouldBeEnabled()
    {
        // Arrange + Act
        var state = new FilterPaneState();

        // Assert
        Assert.True(state.IsEnabled);
    }

    [Fact]
    public void FilterPaneState_DefaultState_ShouldHaveEmptyFilters()
    {
        // Arrange + Act
        var state = new FilterPaneState();

        // Assert
        Assert.Empty(state.Filters);
    }

    [Fact]
    public void FilterPaneState_DefaultState_ShouldHaveNullFilteredDateRange()
    {
        // Arrange + Act
        var state = new FilterPaneState();

        // Assert
        Assert.Null(state.FilteredDateRange);
    }
}

public sealed class FilterPaneActionTests
{
    [Fact]
    public void AddFilterAction_WithFilter_ShouldCreateAction()
    {
        // Arrange
        var filter = FilterBuilder.CreateTestFilter();

        // Act
        var action = new AddFilterAction(filter);

        // Assert
        Assert.Equal(filter, action.SavedFilter);
    }

    [Fact]
    public void ClearAllFiltersAction_ShouldCreateAction()
    {
        // Arrange + Act
        var action = new ClearAllFiltersAction();

        // Assert
        Assert.NotNull(action);
    }

    [Fact]
    public void RemoveFilterAction_ShouldCreateAction()
    {
        // Arrange
        var filterId = FilterId.Create();

        // Act
        var action = new RemoveFilterAction(filterId);

        // Assert
        Assert.Equal(filterId, action.Id);
    }

    [Fact]
    public void SetFilterAction_ShouldCreateAction()
    {
        // Arrange
        var filter = FilterBuilder.CreateTestFilter();

        // Act
        var action = new SetFilterAction(filter);

        // Assert
        Assert.Equal(filter, action.SavedFilter);
    }

    [Fact]
    public void SetFilterDateRangeAction_ShouldCreateAction()
    {
        // Arrange
        var dateModel = new DateFilter { After = DateTime.UtcNow };

        // Act
        var action = new SetFilterDateRangeAction(dateModel);

        // Assert
        Assert.Equal(dateModel, action.DateFilter);
    }

    [Fact]
    public void SetFilterDateRangeSuccessAction_ShouldCreateAction()
    {
        // Arrange
        var dateModel = new DateFilter { Before = DateTime.UtcNow };

        // Act
        var action = new SetFilterDateRangeSuccessAction(dateModel);

        // Assert
        Assert.Equal(dateModel, action.DateFilter);
    }

    [Fact]
    public void ToggleFilterDateAction_ShouldCreateAction()
    {
        // Arrange + Act
        var action = new ToggleFilterDateAction();

        // Assert
        Assert.NotNull(action);
    }

    [Fact]
    public void ToggleFilterEnabledAction_ShouldCreateAction()
    {
        // Arrange
        var filterId = FilterId.Create();

        // Act
        var action = new ToggleFilterEnabledAction(filterId);

        // Assert
        Assert.Equal(filterId, action.Id);
    }

    [Fact]
    public void ToggleFilterExcludedAction_ShouldCreateAction()
    {
        // Arrange
        var filterId = FilterId.Create();

        // Act
        var action = new ToggleFilterExcludedAction(filterId);

        // Assert
        Assert.Equal(filterId, action.Id);
    }

    [Fact]
    public void ToggleIsEnabledAction_ShouldCreateAction()
    {
        // Arrange + Act
        var action = new ToggleIsEnabledAction();

        // Assert
        Assert.NotNull(action);
    }
}

public sealed class FilterPaneReducerTests
{
    [Fact]
    public void ReduceAddFilter_ShouldNotModifyOriginalState()
    {
        // Arrange
        var state = new FilterPaneState();
        var filter = FilterBuilder.CreateTestFilter();
        var action = new AddFilterAction(filter);

        // Act
        Reducers.ReduceAddFilter(state, action);

        // Assert
        Assert.Empty(state.Filters);
    }

    [Fact]
    public void ReduceAddFilter_WithFilter_ShouldAddFilter()
    {
        // Arrange
        var state = new FilterPaneState();
        var filter = FilterBuilder.CreateTestFilter();
        var action = new AddFilterAction(filter);

        // Act
        var result = Reducers.ReduceAddFilter(state, action);

        // Assert
        Assert.Single(result.Filters);
        Assert.Equal(filter, result.Filters[0]);
    }

    [Fact]
    public void ReduceClearFilters_ShouldClearFiltersButPreserveIsEnabled()
    {
        // Arrange
        var state = new FilterPaneState
        {
            Filters = [FilterBuilder.CreateTestFilter(), FilterBuilder.CreateTestFilter()],
            IsEnabled = false
        };

        // Act
        var result = Reducers.ReduceClearFilters(state);

        // Assert
        Assert.Empty(result.Filters);
        Assert.False(result.IsEnabled);
    }

    [Fact]
    public void ReduceMergeFilters_DifferentCaseSameTuple_DedupesAsDuplicate()
    {
        var existing = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(existing);
        var state = new FilterPaneState { Filters = [existing] };
        var differentCase = SavedFilter.TryCreate("LEVEL == 4");
        Assert.NotNull(differentCase);

        var result = Reducers.ReduceMergeFilters(state, new MergeFiltersAction([differentCase]));

        Assert.Single(result.Filters);
    }

    [Fact]
    public void ReduceMergeFilters_PreservesBasicFilter()
    {
        // Arrange
        var basicFilter = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            },
            []);

        var state = new FilterPaneState();

        var filters = ImmutableList.Create(FilterBuilder.CreateTestFilter(basicFilter: basicFilter));

        // Act
        var result = Reducers.ReduceMergeFilters(state, new MergeFiltersAction(filters));

        // Assert
        Assert.Single(result.Filters);
        Assert.Equal(basicFilter, result.Filters[0].BasicFilter);
        Assert.True(result.Filters[0].IsEnabled);
    }

    [Fact]
    public void ReduceMergeFilters_RegeneratesFilterIdsToAvoidRazorKeyCollision()
    {
        var source = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(source);

        var state = new FilterPaneState();

        var result = Reducers.ReduceMergeFilters(state, new MergeFiltersAction([source]));

        Assert.Single(result.Filters);
        Assert.NotEqual(source.Id, result.Filters[0].Id);
    }

    [Fact]
    public void ReduceMergeFilters_SameTextDifferentMode_KeepsBothAsDistinctFilters()
    {
        var advanced = SavedFilter.TryCreate("Level == 4", mode: FilterMode.Advanced);
        Assert.NotNull(advanced);
        var state = new FilterPaneState { Filters = [advanced] };
        var basic = SavedFilter.TryCreate("Level == 4", mode: FilterMode.Basic);
        Assert.NotNull(basic);

        var result = Reducers.ReduceMergeFilters(state, new MergeFiltersAction([basic]));

        Assert.Equal(2, result.Filters.Count);
        Assert.Contains(result.Filters, f => f.Mode == FilterMode.Advanced);
        Assert.Contains(result.Filters, f => f.Mode == FilterMode.Basic);
    }

    [Fact]
    public void ReduceMergeFilters_ShouldPreserveIsExcludedOnAppliedFilters()
    {
        var state = new FilterPaneState();
        var filters = ImmutableList.Create(FilterBuilder.CreateTestFilter(isExcluded: true));

        var result = Reducers.ReduceMergeFilters(state, new MergeFiltersAction(filters));

        Assert.Single(result.Filters);
        Assert.True(result.Filters[0].IsExcluded);
        Assert.True(result.Filters[0].IsEnabled);
    }

    [Fact]
    public void ReduceMergeFilters_WithDuplicateFilter_ShouldSkipDuplicate()
    {
        var existingFilter = FilterBuilder.CreateTestFilter();
        var state = new FilterPaneState { Filters = [existingFilter] };
        var filters = ImmutableList.Create(FilterBuilder.CreateTestFilter());

        var result = Reducers.ReduceMergeFilters(state, new MergeFiltersAction(filters));

        Assert.Single(result.Filters);
    }

    [Fact]
    public void ReduceMergeFilters_WithEmptyFilters_ShouldReturnOriginalState()
    {
        var state = new FilterPaneState();

        var result = Reducers.ReduceMergeFilters(state, new MergeFiltersAction([]));

        Assert.Equal(state, result);
    }

    [Fact]
    public void ReduceMergeFilters_WithNewFilters_ShouldAddFilters()
    {
        var state = new FilterPaneState();
        var filters = ImmutableList.Create(
            FilterBuilder.CreateTestFilter(FilterTestConstants.FilterIdEquals100, HighlightColor.Red));

        var result = Reducers.ReduceMergeFilters(state, new MergeFiltersAction(filters));

        Assert.Single(result.Filters);
        Assert.Equal(FilterTestConstants.FilterIdEquals100, result.Filters[0].ComparisonText);
        Assert.Equal(HighlightColor.Red, result.Filters[0].Color);
        Assert.True(result.Filters[0].IsEnabled);
    }

    [Fact]
    public void ReduceMergeFilters_WithSameComparisonButDifferentExclusion_ShouldKeepBoth()
    {
        var existingInclude = FilterBuilder.CreateTestFilter();
        var state = new FilterPaneState { Filters = [existingInclude] };
        var filters = ImmutableList.Create(FilterBuilder.CreateTestFilter(isExcluded: true));

        var result = Reducers.ReduceMergeFilters(state, new MergeFiltersAction(filters));

        Assert.Equal(2, result.Filters.Count);
        Assert.False(result.Filters[0].IsExcluded);
        Assert.True(result.Filters[1].IsExcluded);
    }

    [Fact]
    public void ReduceRemoveFilter_WithInvalidFilter_ShouldReturnOriginalState()
    {
        // Arrange
        var state = new FilterPaneState { Filters = [FilterBuilder.CreateTestFilter()] };
        var action = new RemoveFilterAction(FilterId.Create());

        // Act
        var result = Reducers.ReduceRemoveFilter(state, action);

        // Assert
        Assert.Equal(state, result);
    }

    [Fact]
    public void ReduceRemoveFilter_WithValidFilter_ShouldRemoveFilter()
    {
        // Arrange
        var filter = FilterBuilder.CreateTestFilter();
        var state = new FilterPaneState { Filters = [filter] };
        var action = new RemoveFilterAction(filter.Id);

        // Act
        var result = Reducers.ReduceRemoveFilter(state, action);

        // Assert
        Assert.Empty(result.Filters);
    }

    [Fact]
    public void ReduceSetFilter_ShouldReplaceFilter()
    {
        // Arrange
        var originalFilter = FilterBuilder.CreateTestFilter();

        var state = new FilterPaneState { Filters = [originalFilter] };

        var updatedFilter = FilterBuilder.CreateTestFilter(FilterTestConstants.FilterIdEquals200, id: originalFilter.Id);

        var action = new SetFilterAction(updatedFilter);

        // Act
        var result = Reducers.ReduceSetFilter(state, action);

        // Assert
        Assert.Single(result.Filters);
        Assert.Equal(FilterTestConstants.FilterIdEquals200, result.Filters[0].ComparisonText);
    }

    [Fact]
    public void ReduceSetFilter_WhenIdFound_ShouldNotDuplicate()
    {
        // Arrange
        var existing = FilterBuilder.CreateTestFilter();
        var state = new FilterPaneState { Filters = [existing] };

        var replacement = FilterBuilder.CreateTestFilter(FilterTestConstants.FilterIdEquals200, id: existing.Id);

        var action = new SetFilterAction(replacement);

        // Act
        var result = Reducers.ReduceSetFilter(state, action);

        // Assert
        Assert.Single(result.Filters);
        Assert.Equal(existing.Id, result.Filters[0].Id);
        Assert.Equal(FilterTestConstants.FilterIdEquals200, result.Filters[0].ComparisonText);
    }

    [Fact]
    public void ReduceSetFilter_WhenIdFound_ShouldReplaceInPlaceWithoutReordering()
    {
        // Arrange
        var first = FilterBuilder.CreateTestFilter();
        var middle = FilterBuilder.CreateTestFilter(FilterTestConstants.FilterIdEquals200);
        var last = FilterBuilder.CreateTestFilter(FilterTestConstants.FilterLevelEqualsError);

        var state = new FilterPaneState { Filters = [first, middle, last] };

        var replacement = FilterBuilder.CreateTestFilter(FilterTestConstants.FilterIdGreaterThan100, id: middle.Id);

        var action = new SetFilterAction(replacement);

        // Act
        var result = Reducers.ReduceSetFilter(state, action);

        // Assert
        Assert.Equal(3, result.Filters.Count);
        Assert.Equal(FilterTestConstants.FilterIdEquals100, result.Filters[0].ComparisonText);
        Assert.Equal(FilterTestConstants.FilterIdGreaterThan100, result.Filters[1].ComparisonText);
        Assert.Equal(FilterTestConstants.FilterLevelEqualsError, result.Filters[2].ComparisonText);
    }

    [Fact]
    public void ReduceSetFilter_WhenIdNotFound_ShouldAppend()
    {
        // Arrange
        var existing = FilterBuilder.CreateTestFilter();
        var state = new FilterPaneState { Filters = [existing] };

        var newFilter = FilterBuilder.CreateTestFilter(FilterTestConstants.FilterIdEquals200);
        var action = new SetFilterAction(newFilter);

        // Act
        var result = Reducers.ReduceSetFilter(state, action);

        // Assert
        Assert.Equal(2, result.Filters.Count);
        Assert.Equal(FilterTestConstants.FilterIdEquals100, result.Filters[0].ComparisonText);
        Assert.Equal(FilterTestConstants.FilterIdEquals200, result.Filters[1].ComparisonText);
    }

    [Fact]
    public void ReduceSetFilterDateRangeSuccess_ShouldSetDateRange()
    {
        // Arrange
        var state = new FilterPaneState();

        var dateModel = new DateFilter
        {
            After = DateTime.UtcNow.AddDays(-1),
            Before = DateTime.UtcNow
        };

        var action = new SetFilterDateRangeSuccessAction(dateModel);

        // Act
        var result = Reducers.ReduceSetFilterDateRangeSuccess(state, action);

        // Assert
        Assert.NotNull(result.FilteredDateRange);
        Assert.Equal(dateModel, result.FilteredDateRange);
    }

    [Fact]
    public void ReduceSetFilterDateRangeSuccess_WithNull_ShouldSetNullDateRange()
    {
        // Arrange
        var state = new FilterPaneState
        {
            FilteredDateRange = new DateFilter { After = DateTime.UtcNow }
        };

        var action = new SetFilterDateRangeSuccessAction(null);

        // Act
        var result = Reducers.ReduceSetFilterDateRangeSuccess(state, action);

        // Assert
        Assert.Null(result.FilteredDateRange);
    }

    [Fact]
    public void ReduceToggleFilterDate_WithDateRange_ShouldToggleIsEnabled()
    {
        // Arrange
        var state = new FilterPaneState
        {
            FilteredDateRange = new DateFilter { IsEnabled = false }
        };

        // Act
        var result = Reducers.ReduceToggleFilterDate(state);

        // Assert
        Assert.NotNull(result.FilteredDateRange);
        Assert.True(result.FilteredDateRange.IsEnabled);
    }

    [Fact]
    public void ReduceToggleFilterDate_WithNullDateRange_ShouldReturnOriginalState()
    {
        // Arrange
        var state = new FilterPaneState { FilteredDateRange = null };

        // Act
        var result = Reducers.ReduceToggleFilterDate(state);

        // Assert
        Assert.Equal(state, result);
    }

    [Fact]
    public void ReduceToggleFilterEnabled_ShouldToggleIsEnabled()
    {
        // Arrange
        var filter = FilterBuilder.CreateTestFilter(isEnabled: false);
        var state = new FilterPaneState { Filters = [filter] };
        var action = new ToggleFilterEnabledAction(filter.Id);

        // Act
        var result = Reducers.ReduceToggleFilterEnabled(state, action);

        // Assert
        Assert.True(result.Filters[0].IsEnabled);
    }

    [Fact]
    public void ReduceToggleFilterEnabled_WithInvalidId_ShouldNotModifyFilters()
    {
        // Arrange
        var filter = FilterBuilder.CreateTestFilter(isEnabled: true);
        var state = new FilterPaneState { Filters = [filter] };
        var action = new ToggleFilterEnabledAction(FilterId.Create());

        // Act
        var result = Reducers.ReduceToggleFilterEnabled(state, action);

        // Assert
        Assert.True(result.Filters[0].IsEnabled);
    }

    [Fact]
    public void ReduceToggleFilterExcluded_ShouldToggleIsExcluded()
    {
        // Arrange
        var filter = FilterBuilder.CreateTestFilter(isExcluded: false);
        var state = new FilterPaneState { Filters = [filter] };
        var action = new ToggleFilterExcludedAction(filter.Id);

        // Act
        var result = Reducers.ReduceToggleFilterExcluded(state, action);

        // Assert
        Assert.True(result.Filters[0].IsExcluded);
    }

    [Fact]
    public void ReduceToggleFilterExcluded_WithInvalidId_ShouldNotModifyFilters()
    {
        // Arrange
        var filter = FilterBuilder.CreateTestFilter(isExcluded: false);
        var state = new FilterPaneState { Filters = [filter] };
        var action = new ToggleFilterExcludedAction(FilterId.Create());

        // Act
        var result = Reducers.ReduceToggleFilterExcluded(state, action);

        // Assert
        Assert.False(result.Filters[0].IsExcluded);
    }

    [Fact]
    public void ReduceToggleIsEnabled_ShouldToggleValue()
    {
        // Arrange
        var state = new FilterPaneState { IsEnabled = false };

        // Act
        var result = Reducers.ReduceToggleIsEnabled(state);

        // Assert
        Assert.True(result.IsEnabled);
    }
}

public sealed class FilterPaneIntegrationTests
{
    [Fact]
    public void ClearAllFilters_ShouldResetToDefaultExceptIsEnabled()
    {
        // Arrange
        var state = new FilterPaneState
        {
            Filters =
            [
                FilterBuilder.CreateTestFilter(),
                FilterBuilder.CreateTestFilter(FilterTestConstants.FilterIdEquals200)
            ],
            FilteredDateRange = new DateFilter { After = DateTime.UtcNow },
            IsEnabled = false
        };

        // Act
        state = Reducers.ReduceClearFilters(state);

        // Assert
        Assert.Empty(state.Filters);
        Assert.Null(state.FilteredDateRange);
        Assert.False(state.IsEnabled);
    }

    [Fact]
    public void CompleteFilterLifecycle_ShouldManageFilterProperly()
    {
        // Arrange
        var state = new FilterPaneState();
        var filter = FilterBuilder.CreateTestFilter();

        // Act + Assert
        state = Reducers.ReduceAddFilter(state, new AddFilterAction(filter));
        Assert.Single(state.Filters);

        state = Reducers.ReduceToggleFilterEnabled(
            state,
            new ToggleFilterEnabledAction(filter.Id));

        Assert.True(state.Filters[0].IsEnabled);

        state = Reducers.ReduceToggleFilterExcluded(
            state,
            new ToggleFilterExcludedAction(filter.Id));

        Assert.True(state.Filters[0].IsExcluded);

        state = Reducers.ReduceRemoveFilter(state, new RemoveFilterAction(filter.Id));
        Assert.Empty(state.Filters);
    }

    [Fact]
    public void DateRangeFiltering_ShouldManageDateRange()
    {
        // Arrange
        var state = new FilterPaneState();
        var dateModel = new DateFilter
        {
            After = DateTime.UtcNow.AddDays(-7),
            Before = DateTime.UtcNow,
            IsEnabled = true
        };

        // Act + Assert
        state = Reducers.ReduceSetFilterDateRangeSuccess(
            state,
            new SetFilterDateRangeSuccessAction(dateModel));

        Assert.NotNull(state.FilteredDateRange);
        Assert.True(state.FilteredDateRange.IsEnabled);

        state = Reducers.ReduceToggleFilterDate(state);
        Assert.NotNull(state.FilteredDateRange);
        Assert.False(state.FilteredDateRange.IsEnabled);

        state = Reducers.ReduceSetFilterDateRangeSuccess(
            state,
            new SetFilterDateRangeSuccessAction(null));

        Assert.Null(state.FilteredDateRange);
    }

    [Fact]
    public void ImmutableCollections_ShouldPreserveImmutability()
    {
        // Arrange
        var filter = FilterBuilder.CreateTestFilter();
        var originalFilters = ImmutableList<SavedFilter>.Empty.Add(filter);
        var state = new FilterPaneState { Filters = originalFilters };

        var newFilter = FilterBuilder.CreateTestFilter(FilterTestConstants.FilterIdEquals200);

        // Act
        var newState = Reducers.ReduceAddFilter(state, new AddFilterAction(newFilter));

        // Assert
        Assert.Single(state.Filters);
        Assert.Equal(2, newState.Filters.Count);
        Assert.NotSame(state.Filters, newState.Filters);
    }

    [Fact]
    public void MultipleFilters_ShouldMaintainIndependentStates()
    {
        // Arrange
        var state = new FilterPaneState();
        var filter1 = FilterBuilder.CreateTestFilter();
        var filter2 = FilterBuilder.CreateTestFilter(FilterTestConstants.FilterIdEquals200);
        var filter3 = FilterBuilder.CreateTestFilter(FilterTestConstants.FilterLevelEqualsError);

        // Act
        state = Reducers.ReduceAddFilter(state, new AddFilterAction(filter1));
        state = Reducers.ReduceAddFilter(state, new AddFilterAction(filter2));
        state = Reducers.ReduceAddFilter(state, new AddFilterAction(filter3));

        state = Reducers.ReduceToggleFilterEnabled(
            state,
            new ToggleFilterEnabledAction(filter2.Id));

        // Assert
        Assert.False(state.Filters[0].IsEnabled);
        Assert.True(state.Filters[1].IsEnabled);
        Assert.False(state.Filters[2].IsEnabled);
    }

    [Fact]
    public void SetFilter_ShouldReplaceExistingFilterWithSameId()
    {
        // Arrange
        var filter = FilterBuilder.CreateTestFilter();
        var state = new FilterPaneState { Filters = [filter] };

        var updatedFilter = FilterBuilder.CreateTestFilter(
            FilterTestConstants.FilterIdEquals200,
            isEnabled: false,
            id: filter.Id);

        // Act
        state = Reducers.ReduceSetFilter(state, new SetFilterAction(updatedFilter));

        // Assert
        Assert.Single(state.Filters);
        Assert.Equal(FilterTestConstants.FilterIdEquals200, state.Filters[0].ComparisonText);
        Assert.False(state.Filters[0].IsEnabled);
    }
}
