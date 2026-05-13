// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Filter;
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
        var state = new FilterPaneState();

        Assert.Empty(state.Filters);
    }

    [Fact]
    public void FilterPaneState_DefaultState_ShouldBeEnabled()
    {
        var state = new FilterPaneState();

        Assert.True(state.IsEnabled);
    }

    [Fact]
    public void FilterPaneState_DefaultState_ShouldHaveEmptyFilters()
    {
        var state = new FilterPaneState();

        Assert.Empty(state.Filters);
    }

    [Fact]
    public void FilterPaneState_DefaultState_ShouldHaveNullFilteredDateRange()
    {
        var state = new FilterPaneState();

        Assert.Null(state.FilteredDateRange);
    }
}

public sealed class FilterPaneActionTests
{
    [Fact]
    public void AddFilterAction_WithFilter_ShouldCreateAction()
    {
        var filter = FilterUtils.CreateTestFilter();
        var action = new AddFilterAction(filter);

        Assert.Equal(filter, action.SavedFilter);
    }

    [Fact]
    public void ApplyFilterGroupAction_ShouldCreateAction()
    {
        var filterGroup = new SavedFilterGroup { Name = Constants.FilterGroupName };
        var action = new ApplyFilterGroupAction(filterGroup);

        Assert.Equal(filterGroup, action.FilterGroup);
    }

    [Fact]
    public void ClearAllFiltersAction_ShouldCreateAction()
    {
        var action = new ClearAllFiltersAction();

        Assert.NotNull(action);
    }

    [Fact]
    public void RemoveFilterAction_ShouldCreateAction()
    {
        var filterId = FilterId.Create();
        var action = new RemoveFilterAction(filterId);

        Assert.Equal(filterId, action.Id);
    }

    [Fact]
    public void SaveFilterGroupAction_ShouldCreateAction()
    {
        var action = new SaveFilterGroupAction(Constants.FilterGroupName);

        Assert.Equal(Constants.FilterGroupName, action.Name);
    }

    [Fact]
    public void SetFilterAction_ShouldCreateAction()
    {
        var filter = FilterUtils.CreateTestFilter();
        var action = new SetFilterAction(filter);

        Assert.Equal(filter, action.SavedFilter);
    }

    [Fact]
    public void SetFilterDateRangeAction_ShouldCreateAction()
    {
        var dateModel = new DateFilter { After = DateTime.UtcNow };
        var action = new SetFilterDateRangeAction(dateModel);

        Assert.Equal(dateModel, action.DateFilter);
    }

    [Fact]
    public void SetFilterDateRangeSuccessAction_ShouldCreateAction()
    {
        var dateModel = new DateFilter { Before = DateTime.UtcNow };
        var action = new SetFilterDateRangeSuccessAction(dateModel);

        Assert.Equal(dateModel, action.DateFilter);
    }

    [Fact]
    public void ToggleFilterDateAction_ShouldCreateAction()
    {
        var action = new ToggleFilterDateAction();

        Assert.NotNull(action);
    }

    [Fact]
    public void ToggleFilterEnabledAction_ShouldCreateAction()
    {
        var filterId = FilterId.Create();
        var action = new ToggleFilterEnabledAction(filterId);

        Assert.Equal(filterId, action.Id);
    }

    [Fact]
    public void ToggleFilterExcludedAction_ShouldCreateAction()
    {
        var filterId = FilterId.Create();
        var action = new ToggleFilterExcludedAction(filterId);

        Assert.Equal(filterId, action.Id);
    }

    [Fact]
    public void ToggleIsEnabledAction_ShouldCreateAction()
    {
        var action = new ToggleIsEnabledAction();

        Assert.NotNull(action);
    }
}

public sealed class FilterPaneReducerTests
{
    [Fact]
    public void ReduceAddFilter_ShouldNotModifyOriginalState()
    {
        var state = new FilterPaneState();
        var filter = FilterUtils.CreateTestFilter(Constants.FilterIdEquals100);
        var action = new AddFilterAction(filter);

        Reducers.ReduceAddFilter(state, action);

        Assert.Empty(state.Filters);
    }

    [Fact]
    public void ReduceAddFilter_WithFilter_ShouldAddFilter()
    {
        var state = new FilterPaneState();
        var filter = FilterUtils.CreateTestFilter(Constants.FilterIdEquals100);
        var action = new AddFilterAction(filter);

        var result = Reducers.ReduceAddFilter(state, action);

        Assert.Single(result.Filters);
        Assert.Equal(filter, result.Filters[0]);
    }

    [Fact]
    public void ReduceApplyFilterGroup_PreservesBasicFilter()
    {
        // Saved-to-group "re-edit as Basic" path: applying a Basic group filter must keep BasicFilter.
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
                FilterUtils.CreateTestFilter(
                    Constants.FilterIdEquals100,
                    basicFilter: basicFilter)
            ]
        };

        var result = Reducers.ReduceApplyFilterGroup(
            state,
            new ApplyFilterGroupAction(filterGroup));

        Assert.Single(result.Filters);
        Assert.Equal(basicFilter, result.Filters[0].BasicFilter);
        Assert.True(result.Filters[0].IsEnabled);
    }

    [Fact]
    public void ReduceApplyFilterGroup_ShouldPreserveIsExcludedOnAppliedFilters()
    {
        // Regression: IsExcluded was previously dropped when copying grouped filters into the pane.
        var state = new FilterPaneState();

        var filterGroup = new SavedFilterGroup
        {
            Filters =
            [
                FilterUtils.CreateTestFilter(Constants.FilterIdEquals100, isExcluded: true)
            ]
        };

        var action = new ApplyFilterGroupAction(filterGroup);

        var result = Reducers.ReduceApplyFilterGroup(state, action);

        Assert.Single(result.Filters);
        Assert.True(result.Filters[0].IsExcluded);
        Assert.True(result.Filters[0].IsEnabled);
    }

    [Fact]
    public void ReduceApplyFilterGroup_WithDuplicateFilter_ShouldSkipDuplicate()
    {
        var existingFilter = FilterUtils.CreateTestFilter(Constants.FilterIdEquals100);

        var state = new FilterPaneState { Filters = [existingFilter] };

        var filterGroup = new SavedFilterGroup
        {
            Filters = [FilterUtils.CreateTestFilter(Constants.FilterIdEquals100)]
        };

        var action = new ApplyFilterGroupAction(filterGroup);

        var result = Reducers.ReduceApplyFilterGroup(state, action);

        Assert.Single(result.Filters);
    }

    [Fact]
    public void ReduceApplyFilterGroup_WithEmptyFilters_ShouldReturnOriginalState()
    {
        var state = new FilterPaneState();
        var filterGroup = new SavedFilterGroup { Filters = [] };
        var action = new ApplyFilterGroupAction(filterGroup);

        var result = Reducers.ReduceApplyFilterGroup(state, action);

        Assert.Equal(state, result);
    }

    [Fact]
    public void ReduceApplyFilterGroup_WithNewFilters_ShouldAddFilters()
    {
        var state = new FilterPaneState();

        var filterGroup = new SavedFilterGroup
        {
            Filters =
            [
                FilterUtils.CreateTestFilter(Constants.FilterIdEquals100, color: HighlightColor.Red)
            ]
        };

        var action = new ApplyFilterGroupAction(filterGroup);

        var result = Reducers.ReduceApplyFilterGroup(state, action);

        Assert.Single(result.Filters);
        Assert.Equal(Constants.FilterIdEquals100, result.Filters[0].ComparisonText);
        Assert.Equal(HighlightColor.Red, result.Filters[0].Color);
        Assert.True(result.Filters[0].IsEnabled);
    }

    [Fact]
    public void ReduceApplyFilterGroup_WithSameComparisonButDifferentExclusion_ShouldKeepBoth()
    {
        // Dedupe key includes IsExcluded so include and exclude of the same expression are distinct.
        var existingInclude = FilterUtils.CreateTestFilter(Constants.FilterIdEquals100, isExcluded: false);

        var state = new FilterPaneState { Filters = [existingInclude] };

        var filterGroup = new SavedFilterGroup
        {
            Filters =
            [
                FilterUtils.CreateTestFilter(Constants.FilterIdEquals100, isExcluded: true)
            ]
        };

        var action = new ApplyFilterGroupAction(filterGroup);

        var result = Reducers.ReduceApplyFilterGroup(state, action);

        Assert.Equal(2, result.Filters.Count);
        Assert.False(result.Filters[0].IsExcluded);
        Assert.True(result.Filters[1].IsExcluded);
    }

    [Fact]
    public void ReduceClearFilters_ShouldClearFiltersButPreserveIsEnabled()
    {
        var state = new FilterPaneState
        {
            Filters = [FilterUtils.CreateTestFilter(), FilterUtils.CreateTestFilter()],
            IsEnabled = false
        };

        var result = Reducers.ReduceClearFilters(state);

        Assert.Empty(result.Filters);
        Assert.False(result.IsEnabled);
    }

    [Fact]
    public void ReduceRemoveFilter_WithInvalidFilter_ShouldReturnOriginalState()
    {
        var state = new FilterPaneState { Filters = [FilterUtils.CreateTestFilter()] };
        var action = new RemoveFilterAction(FilterId.Create());

        var result = Reducers.ReduceRemoveFilter(state, action);

        Assert.Equal(state, result);
    }

    [Fact]
    public void ReduceRemoveFilter_WithValidFilter_ShouldRemoveFilter()
    {
        var filter = FilterUtils.CreateTestFilter();
        var state = new FilterPaneState { Filters = [filter] };
        var action = new RemoveFilterAction(filter.Id);

        var result = Reducers.ReduceRemoveFilter(state, action);

        Assert.Empty(result.Filters);
    }

    [Fact]
    public void ReduceSetFilter_ShouldReplaceFilter()
    {
        var originalFilter = FilterUtils.CreateTestFilter(Constants.FilterIdEquals100);

        var state = new FilterPaneState { Filters = [originalFilter] };

        var updatedFilter = FilterUtils.CreateTestFilter(Constants.FilterIdEquals200, id: originalFilter.Id);

        var action = new SetFilterAction(updatedFilter);

        var result = Reducers.ReduceSetFilter(state, action);

        Assert.Single(result.Filters);
        Assert.Equal(Constants.FilterIdEquals200, result.Filters[0].ComparisonText);
    }

    [Fact]
    public void ReduceSetFilter_WhenIdFound_ShouldNotDuplicate()
    {
        var existing = FilterUtils.CreateTestFilter(Constants.FilterIdEquals100);
        var state = new FilterPaneState { Filters = [existing] };

        var replacement = FilterUtils.CreateTestFilter(Constants.FilterIdEquals200, id: existing.Id);

        var action = new SetFilterAction(replacement);

        var result = Reducers.ReduceSetFilter(state, action);

        Assert.Single(result.Filters);
        Assert.Equal(existing.Id, result.Filters[0].Id);
        Assert.Equal(Constants.FilterIdEquals200, result.Filters[0].ComparisonText);
    }

    [Fact]
    public void ReduceSetFilter_WhenIdFound_ShouldReplaceInPlaceWithoutReordering()
    {
        // Regression: previous Where(...).Concat([new]) implementation reordered the list.
        var first = FilterUtils.CreateTestFilter(Constants.FilterIdEquals100);
        var middle = FilterUtils.CreateTestFilter(Constants.FilterIdEquals200);
        var last = FilterUtils.CreateTestFilter(Constants.FilterLevelEqualsError);

        var state = new FilterPaneState { Filters = [first, middle, last] };

        var replacement = FilterUtils.CreateTestFilter(Constants.FilterIdGreaterThan100, id: middle.Id);

        var action = new SetFilterAction(replacement);

        var result = Reducers.ReduceSetFilter(state, action);

        Assert.Equal(3, result.Filters.Count);
        Assert.Equal(Constants.FilterIdEquals100, result.Filters[0].ComparisonText);
        Assert.Equal(Constants.FilterIdGreaterThan100, result.Filters[1].ComparisonText);
        Assert.Equal(Constants.FilterLevelEqualsError, result.Filters[2].ComparisonText);
    }

    [Fact]
    public void ReduceSetFilter_WhenIdNotFound_ShouldAppend()
    {
        // Upsert contract: ContextMenu and other callers dispatch SetFilter with a new Id.
        var existing = FilterUtils.CreateTestFilter(Constants.FilterIdEquals100);
        var state = new FilterPaneState { Filters = [existing] };

        var newFilter = FilterUtils.CreateTestFilter(Constants.FilterIdEquals200);
        var action = new SetFilterAction(newFilter);

        var result = Reducers.ReduceSetFilter(state, action);

        Assert.Equal(2, result.Filters.Count);
        Assert.Equal(Constants.FilterIdEquals100, result.Filters[0].ComparisonText);
        Assert.Equal(Constants.FilterIdEquals200, result.Filters[1].ComparisonText);
    }

    [Fact]
    public void ReduceSetFilterDateRangeSuccess_ShouldSetDateRange()
    {
        var state = new FilterPaneState();

        var dateModel = new DateFilter
        {
            After = DateTime.UtcNow.AddDays(-1),
            Before = DateTime.UtcNow
        };

        var action = new SetFilterDateRangeSuccessAction(dateModel);

        var result = Reducers.ReduceSetFilterDateRangeSuccess(state, action);

        Assert.NotNull(result.FilteredDateRange);
        Assert.Equal(dateModel, result.FilteredDateRange);
    }

    [Fact]
    public void ReduceSetFilterDateRangeSuccess_WithNull_ShouldSetNullDateRange()
    {
        var state = new FilterPaneState
        {
            FilteredDateRange = new DateFilter { After = DateTime.UtcNow }
        };

        var action = new SetFilterDateRangeSuccessAction(null);

        var result = Reducers.ReduceSetFilterDateRangeSuccess(state, action);

        Assert.Null(result.FilteredDateRange);
    }

    [Fact]
    public void ReduceToggleFilterDate_WithDateRange_ShouldToggleIsEnabled()
    {
        var state = new FilterPaneState
        {
            FilteredDateRange = new DateFilter { IsEnabled = false }
        };

        var result = Reducers.ReduceToggleFilterDate(state);

        Assert.NotNull(result.FilteredDateRange);
        Assert.True(result.FilteredDateRange.IsEnabled);
    }

    [Fact]
    public void ReduceToggleFilterDate_WithNullDateRange_ShouldReturnOriginalState()
    {
        var state = new FilterPaneState { FilteredDateRange = null };

        var result = Reducers.ReduceToggleFilterDate(state);

        Assert.Equal(state, result);
    }

    [Fact]
    public void ReduceToggleFilterEnabled_ShouldToggleIsEnabled()
    {
        var filter = FilterUtils.CreateTestFilter(isEnabled: false);
        var state = new FilterPaneState { Filters = [filter] };
        var action = new ToggleFilterEnabledAction(filter.Id);

        var result = Reducers.ReduceToggleFilterEnabled(state, action);

        Assert.True(result.Filters[0].IsEnabled);
    }

    [Fact]
    public void ReduceToggleFilterEnabled_WithInvalidId_ShouldNotModifyFilters()
    {
        var filter = FilterUtils.CreateTestFilter(isEnabled: true);
        var state = new FilterPaneState { Filters = [filter] };
        var action = new ToggleFilterEnabledAction(FilterId.Create());

        var result = Reducers.ReduceToggleFilterEnabled(state, action);

        Assert.True(result.Filters[0].IsEnabled);
    }

    [Fact]
    public void ReduceToggleFilterExcluded_ShouldToggleIsExcluded()
    {
        var filter = FilterUtils.CreateTestFilter(isExcluded: false);
        var state = new FilterPaneState { Filters = [filter] };
        var action = new ToggleFilterExcludedAction(filter.Id);

        var result = Reducers.ReduceToggleFilterExcluded(state, action);

        Assert.True(result.Filters[0].IsExcluded);
    }

    [Fact]
    public void ReduceToggleFilterExcluded_WithInvalidId_ShouldNotModifyFilters()
    {
        var filter = FilterUtils.CreateTestFilter(isExcluded: false);
        var state = new FilterPaneState { Filters = [filter] };
        var action = new ToggleFilterExcludedAction(FilterId.Create());

        var result = Reducers.ReduceToggleFilterExcluded(state, action);

        Assert.False(result.Filters[0].IsExcluded);
    }

    [Fact]
    public void ReduceToggleIsEnabled_ShouldToggleValue()
    {
        var state = new FilterPaneState { IsEnabled = false };

        var result = Reducers.ReduceToggleIsEnabled(state);

        Assert.True(result.IsEnabled);
    }
}

public sealed class FilterPaneIntegrationTests
{
    [Fact]
    public void ClearAllFilters_ShouldResetToDefaultExceptIsEnabled()
    {
        var state = new FilterPaneState
        {
            Filters =
            [
                FilterUtils.CreateTestFilter(Constants.FilterIdEquals100),
                FilterUtils.CreateTestFilter(Constants.FilterIdEquals200)
            ],
            FilteredDateRange = new DateFilter { After = DateTime.UtcNow },
            IsEnabled = false
        };

        state = Reducers.ReduceClearFilters(state);

        Assert.Empty(state.Filters);
        Assert.Null(state.FilteredDateRange);
        Assert.False(state.IsEnabled);
    }

    [Fact]
    public void CompleteFilterLifecycle_ShouldManageFilterProperly()
    {
        var state = new FilterPaneState();

        var filter = FilterUtils.CreateTestFilter(Constants.FilterIdEquals100);
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
        var state = new FilterPaneState();

        var dateModel = new DateFilter
        {
            After = DateTime.UtcNow.AddDays(-7),
            Before = DateTime.UtcNow,
            IsEnabled = true
        };

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
        var state = new FilterPaneState();

        var filterGroup = new SavedFilterGroup
        {
            Name = Constants.FilterGroupName,
            Filters =
            [
                FilterUtils.CreateTestFilter(Constants.FilterIdEquals100),
                FilterUtils.CreateTestFilter(Constants.FilterIdEquals200),
                FilterUtils.CreateTestFilter(Constants.FilterLevelEqualsError)
            ]
        };

        state = Reducers.ReduceApplyFilterGroup(
            state,
            new ApplyFilterGroupAction(filterGroup));

        Assert.Equal(3, state.Filters.Count);
        Assert.All(state.Filters, filter => Assert.True(filter.IsEnabled));
    }

    [Fact]
    public void ImmutableCollections_ShouldPreserveImmutability()
    {
        var filter = FilterUtils.CreateTestFilter(Constants.FilterIdEquals100);
        var originalFilters = ImmutableList<SavedFilter>.Empty.Add(filter);
        var state = new FilterPaneState { Filters = originalFilters };

        var newFilter = FilterUtils.CreateTestFilter(Constants.FilterIdEquals200);
        var newState = Reducers.ReduceAddFilter(state, new AddFilterAction(newFilter));

        Assert.Single(state.Filters);
        Assert.Equal(2, newState.Filters.Count);
        Assert.NotSame(state.Filters, newState.Filters);
    }

    [Fact]
    public void MultipleFilters_ShouldMaintainIndependentStates()
    {
        var state = new FilterPaneState();

        var filter1 = FilterUtils.CreateTestFilter(Constants.FilterIdEquals100);
        var filter2 = FilterUtils.CreateTestFilter(Constants.FilterIdEquals200);

        var filter3 = FilterUtils.CreateTestFilter(Constants.FilterLevelEqualsError);

        state = Reducers.ReduceAddFilter(state, new AddFilterAction(filter1));
        state = Reducers.ReduceAddFilter(state, new AddFilterAction(filter2));
        state = Reducers.ReduceAddFilter(state, new AddFilterAction(filter3));

        state = Reducers.ReduceToggleFilterEnabled(
            state,
            new ToggleFilterEnabledAction(filter2.Id));

        Assert.False(state.Filters[0].IsEnabled);
        Assert.True(state.Filters[1].IsEnabled);
        Assert.False(state.Filters[2].IsEnabled);
    }

    [Fact]
    public void SetFilter_ShouldReplaceExistingFilterWithSameId()
    {
        var filter = FilterUtils.CreateTestFilter(Constants.FilterIdEquals100);
        var state = new FilterPaneState { Filters = [filter] };

        var updatedFilter = FilterUtils.CreateTestFilter(
            Constants.FilterIdEquals200,
            isEnabled: false,
            id: filter.Id);

        state = Reducers.ReduceSetFilter(state, new SetFilterAction(updatedFilter));

        Assert.Single(state.Filters);
        Assert.Equal(Constants.FilterIdEquals200, state.Filters[0].ComparisonText);
        Assert.False(state.Filters[0].IsEnabled);
    }
}
