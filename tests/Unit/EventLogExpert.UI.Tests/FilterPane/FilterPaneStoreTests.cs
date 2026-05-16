// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Filtering.Runtime;
using EventLogExpert.UI.FilterPane;
using EventLogExpert.UI.Tests.TestUtils;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.FilterPane;

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
        var filter = FilterUtils.CreateTestFilter();

        // Act
        var action = new AddFilterAction(filter);

        // Assert
        Assert.Equal(filter, action.SavedFilter);
    }

    [Fact]
    public void ApplyFilterGroupAction_ShouldCreateAction()
    {
        // Arrange
        var filterGroup = new SavedFilterGroup { Name = Constants.FilterGroupName };

        // Act
        var action = new ApplyFilterGroupAction(filterGroup);

        // Assert
        Assert.Equal(filterGroup, action.FilterGroup);
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
    public void SaveFilterGroupAction_ShouldCreateAction()
    {
        // Arrange + Act
        var action = new SaveFilterGroupAction(Constants.FilterGroupName);

        // Assert
        Assert.Equal(Constants.FilterGroupName, action.Name);
    }

    [Fact]
    public void SetFilterAction_ShouldCreateAction()
    {
        // Arrange
        var filter = FilterUtils.CreateTestFilter();

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
        var filter = FilterUtils.CreateTestFilter();
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
        var filter = FilterUtils.CreateTestFilter();
        var action = new AddFilterAction(filter);

        // Act
        var result = Reducers.ReduceAddFilter(state, action);

        // Assert
        Assert.Single(result.Filters);
        Assert.Equal(filter, result.Filters[0]);
    }

    [Fact]
    public void ReduceApplyFilterGroup_PreservesBasicFilter()
    {
        // Arrange
        var basicFilter = new BasicFilter(
            new BasicFilterCondition
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            },
            []);

        var state = new FilterPaneState();

        var filterGroup = new SavedFilterGroup
        {
            Filters =
            [
                FilterUtils.CreateTestFilter(basicFilter: basicFilter)
            ]
        };

        // Act
        var result = Reducers.ReduceApplyFilterGroup(
            state,
            new ApplyFilterGroupAction(filterGroup));

        // Assert
        Assert.Single(result.Filters);
        Assert.Equal(basicFilter, result.Filters[0].BasicFilter);
        Assert.True(result.Filters[0].IsEnabled);
    }

    [Fact]
    public void ReduceApplyFilterGroup_ShouldPreserveIsExcludedOnAppliedFilters()
    {
        // Arrange
        var state = new FilterPaneState();

        var filterGroup = new SavedFilterGroup
        {
            Filters =
            [
                FilterUtils.CreateTestFilter(isExcluded: true)
            ]
        };

        var action = new ApplyFilterGroupAction(filterGroup);

        // Act
        var result = Reducers.ReduceApplyFilterGroup(state, action);

        // Assert
        Assert.Single(result.Filters);
        Assert.True(result.Filters[0].IsExcluded);
        Assert.True(result.Filters[0].IsEnabled);
    }

    [Fact]
    public void ReduceApplyFilterGroup_WithDuplicateFilter_ShouldSkipDuplicate()
    {
        // Arrange
        var existingFilter = FilterUtils.CreateTestFilter();

        var state = new FilterPaneState { Filters = [existingFilter] };

        var filterGroup = new SavedFilterGroup
        {
            Filters = [FilterUtils.CreateTestFilter()]
        };

        var action = new ApplyFilterGroupAction(filterGroup);

        // Act
        var result = Reducers.ReduceApplyFilterGroup(state, action);

        // Assert
        Assert.Single(result.Filters);
    }

    [Fact]
    public void ReduceApplyFilterGroup_WithEmptyFilters_ShouldReturnOriginalState()
    {
        // Arrange
        var state = new FilterPaneState();
        var filterGroup = new SavedFilterGroup { Filters = [] };
        var action = new ApplyFilterGroupAction(filterGroup);

        // Act
        var result = Reducers.ReduceApplyFilterGroup(state, action);

        // Assert
        Assert.Equal(state, result);
    }

    [Fact]
    public void ReduceApplyFilterGroup_WithNewFilters_ShouldAddFilters()
    {
        // Arrange
        var state = new FilterPaneState();

        var filterGroup = new SavedFilterGroup
        {
            Filters =
            [
                FilterUtils.CreateTestFilter(Constants.FilterIdEquals100, HighlightColor.Red)
            ]
        };

        var action = new ApplyFilterGroupAction(filterGroup);

        // Act
        var result = Reducers.ReduceApplyFilterGroup(state, action);

        // Assert
        Assert.Single(result.Filters);
        Assert.Equal(Constants.FilterIdEquals100, result.Filters[0].ComparisonText);
        Assert.Equal(HighlightColor.Red, result.Filters[0].Color);
        Assert.True(result.Filters[0].IsEnabled);
    }

    [Fact]
    public void ReduceApplyFilterGroup_WithSameComparisonButDifferentExclusion_ShouldKeepBoth()
    {
        // Arrange
        var existingInclude = FilterUtils.CreateTestFilter();

        var state = new FilterPaneState { Filters = [existingInclude] };

        var filterGroup = new SavedFilterGroup
        {
            Filters =
            [
                FilterUtils.CreateTestFilter(isExcluded: true)
            ]
        };

        var action = new ApplyFilterGroupAction(filterGroup);

        // Act
        var result = Reducers.ReduceApplyFilterGroup(state, action);

        // Assert
        Assert.Equal(2, result.Filters.Count);
        Assert.False(result.Filters[0].IsExcluded);
        Assert.True(result.Filters[1].IsExcluded);
    }

    [Fact]
    public void ReduceClearFilters_ShouldClearFiltersButPreserveIsEnabled()
    {
        // Arrange
        var state = new FilterPaneState
        {
            Filters = [FilterUtils.CreateTestFilter(), FilterUtils.CreateTestFilter()],
            IsEnabled = false
        };

        // Act
        var result = Reducers.ReduceClearFilters(state);

        // Assert
        Assert.Empty(result.Filters);
        Assert.False(result.IsEnabled);
    }

    [Fact]
    public void ReduceRemoveFilter_WithInvalidFilter_ShouldReturnOriginalState()
    {
        // Arrange
        var state = new FilterPaneState { Filters = [FilterUtils.CreateTestFilter()] };
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
        var filter = FilterUtils.CreateTestFilter();
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
        var originalFilter = FilterUtils.CreateTestFilter();

        var state = new FilterPaneState { Filters = [originalFilter] };

        var updatedFilter = FilterUtils.CreateTestFilter(Constants.FilterIdEquals200, id: originalFilter.Id);

        var action = new SetFilterAction(updatedFilter);

        // Act
        var result = Reducers.ReduceSetFilter(state, action);

        // Assert
        Assert.Single(result.Filters);
        Assert.Equal(Constants.FilterIdEquals200, result.Filters[0].ComparisonText);
    }

    [Fact]
    public void ReduceSetFilter_WhenIdFound_ShouldNotDuplicate()
    {
        // Arrange
        var existing = FilterUtils.CreateTestFilter();
        var state = new FilterPaneState { Filters = [existing] };

        var replacement = FilterUtils.CreateTestFilter(Constants.FilterIdEquals200, id: existing.Id);

        var action = new SetFilterAction(replacement);

        // Act
        var result = Reducers.ReduceSetFilter(state, action);

        // Assert
        Assert.Single(result.Filters);
        Assert.Equal(existing.Id, result.Filters[0].Id);
        Assert.Equal(Constants.FilterIdEquals200, result.Filters[0].ComparisonText);
    }

    [Fact]
    public void ReduceSetFilter_WhenIdFound_ShouldReplaceInPlaceWithoutReordering()
    {
        // Arrange
        var first = FilterUtils.CreateTestFilter();
        var middle = FilterUtils.CreateTestFilter(Constants.FilterIdEquals200);
        var last = FilterUtils.CreateTestFilter(Constants.FilterLevelEqualsError);

        var state = new FilterPaneState { Filters = [first, middle, last] };

        var replacement = FilterUtils.CreateTestFilter(Constants.FilterIdGreaterThan100, id: middle.Id);

        var action = new SetFilterAction(replacement);

        // Act
        var result = Reducers.ReduceSetFilter(state, action);

        // Assert
        Assert.Equal(3, result.Filters.Count);
        Assert.Equal(Constants.FilterIdEquals100, result.Filters[0].ComparisonText);
        Assert.Equal(Constants.FilterIdGreaterThan100, result.Filters[1].ComparisonText);
        Assert.Equal(Constants.FilterLevelEqualsError, result.Filters[2].ComparisonText);
    }

    [Fact]
    public void ReduceSetFilter_WhenIdNotFound_ShouldAppend()
    {
        // Arrange
        var existing = FilterUtils.CreateTestFilter();
        var state = new FilterPaneState { Filters = [existing] };

        var newFilter = FilterUtils.CreateTestFilter(Constants.FilterIdEquals200);
        var action = new SetFilterAction(newFilter);

        // Act
        var result = Reducers.ReduceSetFilter(state, action);

        // Assert
        Assert.Equal(2, result.Filters.Count);
        Assert.Equal(Constants.FilterIdEquals100, result.Filters[0].ComparisonText);
        Assert.Equal(Constants.FilterIdEquals200, result.Filters[1].ComparisonText);
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
        var filter = FilterUtils.CreateTestFilter(isEnabled: false);
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
        var filter = FilterUtils.CreateTestFilter(isEnabled: true);
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
        var filter = FilterUtils.CreateTestFilter(isExcluded: false);
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
        var filter = FilterUtils.CreateTestFilter(isExcluded: false);
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
                FilterUtils.CreateTestFilter(),
                FilterUtils.CreateTestFilter(Constants.FilterIdEquals200)
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
        var filter = FilterUtils.CreateTestFilter();

        // Act + Assert (one assertion per state transition)
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

        // Act + Assert (set, toggle, clear)
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
    public void FilterGroupApplication_ShouldAddMultipleFilters()
    {
        // Arrange
        var state = new FilterPaneState();
        var filterGroup = new SavedFilterGroup
        {
            Name = Constants.FilterGroupName,
            Filters =
            [
                FilterUtils.CreateTestFilter(),
                FilterUtils.CreateTestFilter(Constants.FilterIdEquals200),
                FilterUtils.CreateTestFilter(Constants.FilterLevelEqualsError)
            ]
        };

        // Act
        state = Reducers.ReduceApplyFilterGroup(
            state,
            new ApplyFilterGroupAction(filterGroup));

        // Assert
        Assert.Equal(3, state.Filters.Count);
        Assert.All(state.Filters, filter => Assert.True(filter.IsEnabled));
    }

    [Fact]
    public void ImmutableCollections_ShouldPreserveImmutability()
    {
        // Arrange
        var filter = FilterUtils.CreateTestFilter();
        var originalFilters = ImmutableList<SavedFilter>.Empty.Add(filter);
        var state = new FilterPaneState { Filters = originalFilters };

        var newFilter = FilterUtils.CreateTestFilter(Constants.FilterIdEquals200);

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
        var filter1 = FilterUtils.CreateTestFilter();
        var filter2 = FilterUtils.CreateTestFilter(Constants.FilterIdEquals200);
        var filter3 = FilterUtils.CreateTestFilter(Constants.FilterLevelEqualsError);

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
        var filter = FilterUtils.CreateTestFilter();
        var state = new FilterPaneState { Filters = [filter] };

        var updatedFilter = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals200,
            isEnabled: false,
            id: filter.Id);

        // Act
        state = Reducers.ReduceSetFilter(state, new SetFilterAction(updatedFilter));

        // Assert
        Assert.Single(state.Filters);
        Assert.Equal(Constants.FilterIdEquals200, state.Filters[0].ComparisonText);
        Assert.False(state.Filters[0].IsEnabled);
    }
}
