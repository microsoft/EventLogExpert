// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.FilterPane;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.Store;

public sealed class FilterPaneStateTests
{
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

    [Fact]
    public void FilterPaneState_DefaultState_ShouldNotBeLoading()
    {
        var state = new FilterPaneState();

        Assert.False(state.IsLoading);
    }

    [Fact]
    public void FilterPaneState_DefaultState_ShouldNotBeXmlEnabled()
    {
        var state = new FilterPaneState();

        Assert.False(state.IsXmlEnabled);
    }
}

public sealed class FilterPaneActionTests
{
    [Fact]
    public void AddFilterAction_WithFilter_ShouldCreateAction()
    {
        var filter = new FilterModel();
        var action = new FilterPaneAction.AddFilter(filter);

        Assert.Equal(filter, action.FilterModel);
    }

    [Fact]
    public void AddFilterAction_WithNullFilter_ShouldCreateAction()
    {
        var action = new FilterPaneAction.AddFilter();

        Assert.Null(action.FilterModel);
    }

    [Fact]
    public void AddSubFilterAction_ShouldCreateAction()
    {
        var parentId = FilterId.Create();
        var action = new FilterPaneAction.AddSubFilter(parentId);

        Assert.Equal(parentId, action.ParentId);
    }

    [Fact]
    public void ApplyFilterGroupAction_ShouldCreateAction()
    {
        var filterGroup = new FilterGroupModel { Name = Constants.FilterGroupName };
        var action = new FilterPaneAction.ApplyFilterGroup(filterGroup);

        Assert.Equal(filterGroup, action.FilterGroup);
    }

    [Fact]
    public void ClearAllFiltersAction_ShouldCreateAction()
    {
        var action = new FilterPaneAction.ClearAllFilters();

        Assert.NotNull(action);
    }

    [Fact]
    public void RemoveFilterAction_ShouldCreateAction()
    {
        var filterId = FilterId.Create();
        var action = new FilterPaneAction.RemoveFilter(filterId);

        Assert.Equal(filterId, action.Id);
    }

    [Fact]
    public void RemoveSubFilterAction_ShouldCreateAction()
    {
        var parentId = FilterId.Create();
        var subFilterId = FilterId.Create();
        var action = new FilterPaneAction.RemoveSubFilter(parentId, subFilterId);

        Assert.Equal(parentId, action.ParentId);
        Assert.Equal(subFilterId, action.SubFilterId);
    }

    [Fact]
    public void SaveFilterGroupAction_ShouldCreateAction()
    {
        var action = new FilterPaneAction.SaveFilterGroup(Constants.FilterGroupName);

        Assert.Equal(Constants.FilterGroupName, action.Name);
    }

    [Fact]
    public void SetFilterAction_ShouldCreateAction()
    {
        var filter = new FilterModel();
        var action = new FilterPaneAction.SetFilter(filter);

        Assert.Equal(filter, action.FilterModel);
    }

    [Fact]
    public void SetFilterDateRangeAction_ShouldCreateAction()
    {
        var dateModel = new FilterDateModel { After = DateTime.UtcNow };
        var action = new FilterPaneAction.SetFilterDateRange(dateModel);

        Assert.Equal(dateModel, action.FilterDateModel);
    }

    [Fact]
    public void SetFilterDateRangeSuccessAction_ShouldCreateAction()
    {
        var dateModel = new FilterDateModel { Before = DateTime.UtcNow };
        var action = new FilterPaneAction.SetFilterDateRangeSuccess(dateModel);

        Assert.Equal(dateModel, action.FilterDateModel);
    }

    [Fact]
    public void ToggleFilterDateAction_ShouldCreateAction()
    {
        var action = new FilterPaneAction.ToggleFilterDate();

        Assert.NotNull(action);
    }

    [Fact]
    public void ToggleFilterEditingAction_ShouldCreateAction()
    {
        var filterId = FilterId.Create();
        var action = new FilterPaneAction.ToggleFilterEditing(filterId);

        Assert.Equal(filterId, action.Id);
    }

    [Fact]
    public void ToggleFilterEnabledAction_ShouldCreateAction()
    {
        var filterId = FilterId.Create();
        var action = new FilterPaneAction.ToggleFilterEnabled(filterId);

        Assert.Equal(filterId, action.Id);
    }

    [Fact]
    public void ToggleFilterExcludedAction_ShouldCreateAction()
    {
        var filterId = FilterId.Create();
        var action = new FilterPaneAction.ToggleFilterExcluded(filterId);

        Assert.Equal(filterId, action.Id);
    }

    [Fact]
    public void ToggleIsEnabledAction_ShouldCreateAction()
    {
        var action = new FilterPaneAction.ToggleIsEnabled();

        Assert.NotNull(action);
    }

    [Fact]
    public void ToggleIsLoadingAction_ShouldCreateAction()
    {
        var action = new FilterPaneAction.ToggleIsLoading();

        Assert.NotNull(action);
    }

    [Fact]
    public void ToggleIsXmlEnabledAction_ShouldCreateAction()
    {
        var action = new FilterPaneAction.ToggleIsXmlEnabled();

        Assert.NotNull(action);
    }
}

public sealed class FilterPaneReducerTests
{
    [Fact]
    public void ReduceAddFilter_ShouldNotModifyOriginalState()
    {
        var state = new FilterPaneState();
        var action = new FilterPaneAction.AddFilter();

        FilterPaneReducers.ReduceAddFilter(state, action);

        Assert.Empty(state.Filters);
    }

    [Fact]
    public void ReduceAddFilter_WithFilter_ShouldAddFilter()
    {
        var state = new FilterPaneState();
        var filter = new FilterModel { Comparison = new FilterComparison { Value = Constants.FilterIdEquals100 } };
        var action = new FilterPaneAction.AddFilter(filter);

        var result = FilterPaneReducers.ReduceAddFilter(state, action);

        Assert.Single(result.Filters);
        Assert.Equal(filter, result.Filters[0]);
    }

    [Fact]
    public void ReduceAddFilter_WithNullFilter_ShouldAddNewEditingFilter()
    {
        var state = new FilterPaneState();
        var action = new FilterPaneAction.AddFilter();

        var result = FilterPaneReducers.ReduceAddFilter(state, action);

        Assert.Single(result.Filters);
        Assert.True(result.Filters[0].IsEditing);
    }

    [Fact]
    public void ReduceAddSubFilter_WithInvalidParent_ShouldReturnOriginalState()
    {
        var state = new FilterPaneState { Filters = [new FilterModel()] };
        var action = new FilterPaneAction.AddSubFilter(FilterId.Create());

        var result = FilterPaneReducers.ReduceAddSubFilter(state, action);

        Assert.Equal(state, result);
    }

    [Fact]
    public void ReduceAddSubFilter_WithValidParent_ShouldAddSubFilter()
    {
        var parentFilter = new FilterModel();
        var state = new FilterPaneState { Filters = [parentFilter] };
        var action = new FilterPaneAction.AddSubFilter(parentFilter.Id);

        var result = FilterPaneReducers.ReduceAddSubFilter(state, action);

        Assert.Single(result.Filters);
        Assert.Single(result.Filters[0].SubFilters);
    }

    [Fact]
    public void ReduceApplyFilterGroup_WithDuplicateFilter_ShouldSkipDuplicate()
    {
        var existingFilter = new FilterModel
            { Comparison = new FilterComparison { Value = Constants.FilterIdEquals100 } };

        var state = new FilterPaneState { Filters = [existingFilter] };

        var filterGroup = new FilterGroupModel
        {
            Filters = [new FilterModel { Comparison = new FilterComparison { Value = Constants.FilterIdEquals100 } }]
        };

        var action = new FilterPaneAction.ApplyFilterGroup(filterGroup);

        var result = FilterPaneReducers.ReduceApplyFilterGroup(state, action);

        Assert.Single(result.Filters);
    }

    [Fact]
    public void ReduceApplyFilterGroup_WithEmptyFilters_ShouldReturnOriginalState()
    {
        var state = new FilterPaneState();
        var filterGroup = new FilterGroupModel { Filters = [] };
        var action = new FilterPaneAction.ApplyFilterGroup(filterGroup);

        var result = FilterPaneReducers.ReduceApplyFilterGroup(state, action);

        Assert.Equal(state, result);
    }

    [Fact]
    public void ReduceApplyFilterGroup_WithNewFilters_ShouldAddFilters()
    {
        var state = new FilterPaneState();

        var filterGroup = new FilterGroupModel
        {
            Filters =
            [
                new FilterModel
                {
                    Comparison = new FilterComparison { Value = Constants.FilterIdEquals100 },
                    Color = HighlightColor.Red
                }
            ]
        };

        var action = new FilterPaneAction.ApplyFilterGroup(filterGroup);

        var result = FilterPaneReducers.ReduceApplyFilterGroup(state, action);

        Assert.Single(result.Filters);
        Assert.Equal(Constants.FilterIdEquals100, result.Filters[0].Comparison.Value);
        Assert.Equal(HighlightColor.Red, result.Filters[0].Color);
        Assert.True(result.Filters[0].IsEnabled);
    }

    [Fact]
    public void ReduceClearFilters_ShouldClearFiltersButPreserveIsEnabled()
    {
        var state = new FilterPaneState
        {
            Filters = [new FilterModel(), new FilterModel()],
            IsEnabled = false
        };

        var result = FilterPaneReducers.ReduceClearFilters(state);

        Assert.Empty(result.Filters);
        Assert.False(result.IsEnabled);
    }

    [Fact]
    public void ReduceRemoveFilter_WithInvalidFilter_ShouldReturnOriginalState()
    {
        var state = new FilterPaneState { Filters = [new FilterModel()] };
        var action = new FilterPaneAction.RemoveFilter(FilterId.Create());

        var result = FilterPaneReducers.ReduceRemoveFilter(state, action);

        Assert.Equal(state, result);
    }

    [Fact]
    public void ReduceRemoveFilter_WithValidFilter_ShouldRemoveFilter()
    {
        var filter = new FilterModel();
        var state = new FilterPaneState { Filters = [filter] };
        var action = new FilterPaneAction.RemoveFilter(filter.Id);

        var result = FilterPaneReducers.ReduceRemoveFilter(state, action);

        Assert.Empty(result.Filters);
    }

    [Fact]
    public void ReduceRemoveSubFilter_WithInvalidParent_ShouldReturnOriginalState()
    {
        var state = new FilterPaneState { Filters = [new FilterModel()] };
        var action = new FilterPaneAction.RemoveSubFilter(FilterId.Create(), FilterId.Create());

        var result = FilterPaneReducers.ReduceRemoveSubFilter(state, action);

        Assert.Equal(state, result);
    }

    [Fact]
    public void ReduceRemoveSubFilter_WithValidParentAndSubFilter_ShouldRemoveSubFilter()
    {
        var parentFilter = new FilterModel();
        var subFilter = new FilterModel();
        parentFilter.SubFilters.Add(subFilter);
        var state = new FilterPaneState { Filters = [parentFilter] };
        var action = new FilterPaneAction.RemoveSubFilter(parentFilter.Id, subFilter.Id);

        var result = FilterPaneReducers.ReduceRemoveSubFilter(state, action);

        Assert.Single(result.Filters);
        Assert.Empty(result.Filters[0].SubFilters);
    }

    [Fact]
    public void ReduceSetFilter_ShouldReplaceFilter()
    {
        var originalFilter = new FilterModel
            { Comparison = new FilterComparison { Value = Constants.FilterIdEquals100 } };

        var state = new FilterPaneState { Filters = [originalFilter] };

        var updatedFilter = originalFilter with
        {
            Comparison = new FilterComparison { Value = Constants.FilterIdEquals200 }
        };

        var action = new FilterPaneAction.SetFilter(updatedFilter);

        var result = FilterPaneReducers.ReduceSetFilter(state, action);

        Assert.Single(result.Filters);
        Assert.Equal(Constants.FilterIdEquals200, result.Filters[0].Comparison.Value);
    }

    [Fact]
    public void ReduceSetFilterDateRangeSuccess_ShouldSetDateRange()
    {
        var state = new FilterPaneState();

        var dateModel = new FilterDateModel
        {
            After = DateTime.UtcNow.AddDays(-1),
            Before = DateTime.UtcNow
        };

        var action = new FilterPaneAction.SetFilterDateRangeSuccess(dateModel);

        var result = FilterPaneReducers.ReduceSetFilterDateRangeSuccess(state, action);

        Assert.NotNull(result.FilteredDateRange);
        Assert.Equal(dateModel, result.FilteredDateRange);
    }

    [Fact]
    public void ReduceSetFilterDateRangeSuccess_WithNull_ShouldSetNullDateRange()
    {
        var state = new FilterPaneState
        {
            FilteredDateRange = new FilterDateModel { After = DateTime.UtcNow }
        };

        var action = new FilterPaneAction.SetFilterDateRangeSuccess(null);

        var result = FilterPaneReducers.ReduceSetFilterDateRangeSuccess(state, action);

        Assert.Null(result.FilteredDateRange);
    }

    [Fact]
    public void ReduceToggleFilterDate_WithDateRange_ShouldToggleIsEnabled()
    {
        var state = new FilterPaneState
        {
            FilteredDateRange = new FilterDateModel { IsEnabled = false }
        };

        var result = FilterPaneReducers.ReduceToggleFilterDate(state);

        Assert.True(result.FilteredDateRange!.IsEnabled);
    }

    [Fact]
    public void ReduceToggleFilterDate_WithNullDateRange_ShouldReturnOriginalState()
    {
        var state = new FilterPaneState { FilteredDateRange = null };

        var result = FilterPaneReducers.ReduceToggleFilterDate(state);

        Assert.Equal(state, result);
    }

    [Fact]
    public void ReduceToggleFilterEditing_ShouldToggleIsEditing()
    {
        var filter = new FilterModel { IsEditing = false };
        var state = new FilterPaneState { Filters = [filter] };
        var action = new FilterPaneAction.ToggleFilterEditing(filter.Id);

        var result = FilterPaneReducers.ReduceToggleFilterEditing(state, action);

        Assert.True(result.Filters[0].IsEditing);
    }

    [Fact]
    public void ReduceToggleFilterEditing_WithInvalidId_ShouldNotModifyFilters()
    {
        var filter = new FilterModel { IsEditing = false };
        var state = new FilterPaneState { Filters = [filter] };
        var action = new FilterPaneAction.ToggleFilterEditing(FilterId.Create());

        var result = FilterPaneReducers.ReduceToggleFilterEditing(state, action);

        Assert.False(result.Filters[0].IsEditing);
    }

    [Fact]
    public void ReduceToggleFilterEnabled_ShouldToggleIsEnabled()
    {
        var filter = new FilterModel { IsEnabled = false };
        var state = new FilterPaneState { Filters = [filter] };
        var action = new FilterPaneAction.ToggleFilterEnabled(filter.Id);

        var result = FilterPaneReducers.ReduceToggleFilterEnabled(state, action);

        Assert.True(result.Filters[0].IsEnabled);
    }

    [Fact]
    public void ReduceToggleFilterEnabled_WithInvalidId_ShouldNotModifyFilters()
    {
        var filter = new FilterModel { IsEnabled = true };
        var state = new FilterPaneState { Filters = [filter] };
        var action = new FilterPaneAction.ToggleFilterEnabled(FilterId.Create());

        var result = FilterPaneReducers.ReduceToggleFilterEnabled(state, action);

        Assert.True(result.Filters[0].IsEnabled);
    }

    [Fact]
    public void ReduceToggleFilterExcluded_ShouldToggleIsExcluded()
    {
        var filter = new FilterModel { IsExcluded = false };
        var state = new FilterPaneState { Filters = [filter] };
        var action = new FilterPaneAction.ToggleFilterExcluded(filter.Id);

        var result = FilterPaneReducers.ReduceToggleFilterExcluded(state, action);

        Assert.True(result.Filters[0].IsExcluded);
    }

    [Fact]
    public void ReduceToggleFilterExcluded_WithInvalidId_ShouldNotModifyFilters()
    {
        var filter = new FilterModel { IsExcluded = false };
        var state = new FilterPaneState { Filters = [filter] };
        var action = new FilterPaneAction.ToggleFilterExcluded(FilterId.Create());

        var result = FilterPaneReducers.ReduceToggleFilterExcluded(state, action);

        Assert.False(result.Filters[0].IsExcluded);
    }

    [Fact]
    public void ReduceToggleIsEnabled_ShouldToggleValue()
    {
        var state = new FilterPaneState { IsEnabled = false };

        var result = FilterPaneReducers.ReduceToggleIsEnabled(state);

        Assert.True(result.IsEnabled);
    }

    [Fact]
    public void ReduceToggleIsLoading_ShouldToggleValue()
    {
        var state = new FilterPaneState { IsLoading = false };

        var result = FilterPaneReducers.ReduceToggleIsLoading(state);

        Assert.True(result.IsLoading);
    }

    [Fact]
    public void ReduceToggleIsXmlEnabled_ShouldToggleValue()
    {
        var state = new FilterPaneState { IsXmlEnabled = false };

        var result = FilterPaneReducers.ReduceToggleIsXmlEnabled(state);

        Assert.True(result.IsXmlEnabled);
    }

    [Fact]
    public void ReduceToggleIsXmlEnabled_ShouldToggleValueToFalse()
    {
        var state = new FilterPaneState { IsXmlEnabled = true };

        var result = FilterPaneReducers.ReduceToggleIsXmlEnabled(state);

        Assert.False(result.IsXmlEnabled);
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
                new FilterModel { Comparison = new FilterComparison { Value = Constants.FilterIdEquals100 } },
                new FilterModel { Comparison = new FilterComparison { Value = Constants.FilterIdEquals200 } }
            ],
            FilteredDateRange = new FilterDateModel { After = DateTime.UtcNow },
            IsEnabled = false,
            IsXmlEnabled = true,
            IsLoading = true
        };

        state = FilterPaneReducers.ReduceClearFilters(state);

        Assert.Empty(state.Filters);
        Assert.Null(state.FilteredDateRange);
        Assert.False(state.IsEnabled);
        Assert.False(state.IsXmlEnabled);
        Assert.False(state.IsLoading);
    }

    [Fact]
    public void CompleteFilterLifecycle_ShouldManageFilterProperly()
    {
        var state = new FilterPaneState();

        var filter = new FilterModel { Comparison = new FilterComparison { Value = Constants.FilterIdEquals100 } };
        state = FilterPaneReducers.ReduceAddFilter(state, new FilterPaneAction.AddFilter(filter));
        Assert.Single(state.Filters);

        state = FilterPaneReducers.ReduceToggleFilterEnabled(
            state,
            new FilterPaneAction.ToggleFilterEnabled(filter.Id));

        Assert.True(state.Filters[0].IsEnabled);

        state = FilterPaneReducers.ReduceToggleFilterExcluded(
            state,
            new FilterPaneAction.ToggleFilterExcluded(filter.Id));

        Assert.True(state.Filters[0].IsExcluded);

        state = FilterPaneReducers.ReduceRemoveFilter(state, new FilterPaneAction.RemoveFilter(filter.Id));
        Assert.Empty(state.Filters);
    }

    [Fact]
    public void DateRangeFiltering_ShouldManageDateRange()
    {
        var state = new FilterPaneState();

        var dateModel = new FilterDateModel
        {
            After = DateTime.UtcNow.AddDays(-7),
            Before = DateTime.UtcNow,
            IsEnabled = true
        };

        state = FilterPaneReducers.ReduceSetFilterDateRangeSuccess(
            state,
            new FilterPaneAction.SetFilterDateRangeSuccess(dateModel));

        Assert.NotNull(state.FilteredDateRange);
        Assert.True(state.FilteredDateRange!.IsEnabled);

        state = FilterPaneReducers.ReduceToggleFilterDate(state);
        Assert.False(state.FilteredDateRange!.IsEnabled);

        state = FilterPaneReducers.ReduceSetFilterDateRangeSuccess(
            state,
            new FilterPaneAction.SetFilterDateRangeSuccess(null));

        Assert.Null(state.FilteredDateRange);
    }

    [Fact]
    public void FilterEditing_ShouldToggleIndependently()
    {
        var filter1 = new FilterModel { IsEditing = false };
        var filter2 = new FilterModel { IsEditing = false };
        var state = new FilterPaneState { Filters = [filter1, filter2] };

        state = FilterPaneReducers.ReduceToggleFilterEditing(
            state,
            new FilterPaneAction.ToggleFilterEditing(filter1.Id));

        Assert.True(state.Filters[0].IsEditing);
        Assert.False(state.Filters[1].IsEditing);

        state = FilterPaneReducers.ReduceToggleFilterEditing(
            state,
            new FilterPaneAction.ToggleFilterEditing(filter1.Id));

        Assert.False(state.Filters[0].IsEditing);
        Assert.False(state.Filters[1].IsEditing);
    }

    [Fact]
    public void FilterGroupApplication_ShouldAddMultipleFilters()
    {
        var state = new FilterPaneState();

        var filterGroup = new FilterGroupModel
        {
            Name = Constants.FilterGroupName,
            Filters =
            [
                new FilterModel { Comparison = new FilterComparison { Value = Constants.FilterIdEquals100 } },
                new FilterModel { Comparison = new FilterComparison { Value = Constants.FilterIdEquals200 } },
                new FilterModel { Comparison = new FilterComparison { Value = Constants.FilterLevelEqualsError } }
            ]
        };

        state = FilterPaneReducers.ReduceApplyFilterGroup(
            state,
            new FilterPaneAction.ApplyFilterGroup(filterGroup));

        Assert.Equal(3, state.Filters.Count);
        Assert.All(state.Filters, filter => Assert.True(filter.IsEnabled));
    }

    [Fact]
    public void ImmutableCollections_ShouldPreserveImmutability()
    {
        var filter = new FilterModel { Comparison = new FilterComparison { Value = Constants.FilterIdEquals100 } };
        var originalFilters = ImmutableList<FilterModel>.Empty.Add(filter);
        var state = new FilterPaneState { Filters = originalFilters };

        var newFilter = new FilterModel { Comparison = new FilterComparison { Value = Constants.FilterIdEquals200 } };
        var newState = FilterPaneReducers.ReduceAddFilter(state, new FilterPaneAction.AddFilter(newFilter));

        Assert.Single(state.Filters);
        Assert.Equal(2, newState.Filters.Count);
        Assert.NotSame(state.Filters, newState.Filters);
    }

    [Fact]
    public void MultipleFilters_ShouldMaintainIndependentStates()
    {
        var state = new FilterPaneState();

        var filter1 = new FilterModel { Comparison = new FilterComparison { Value = Constants.FilterIdEquals100 } };
        var filter2 = new FilterModel { Comparison = new FilterComparison { Value = Constants.FilterIdEquals200 } };

        var filter3 = new FilterModel
            { Comparison = new FilterComparison { Value = Constants.FilterLevelEqualsError } };

        state = FilterPaneReducers.ReduceAddFilter(state, new FilterPaneAction.AddFilter(filter1));
        state = FilterPaneReducers.ReduceAddFilter(state, new FilterPaneAction.AddFilter(filter2));
        state = FilterPaneReducers.ReduceAddFilter(state, new FilterPaneAction.AddFilter(filter3));

        state = FilterPaneReducers.ReduceToggleFilterEnabled(
            state,
            new FilterPaneAction.ToggleFilterEnabled(filter2.Id));

        Assert.False(state.Filters[0].IsEnabled);
        Assert.True(state.Filters[1].IsEnabled);
        Assert.False(state.Filters[2].IsEnabled);
    }

    [Fact]
    public void SetFilter_ShouldReplaceExistingFilterWithSameId()
    {
        var filter = new FilterModel { Comparison = new FilterComparison { Value = Constants.FilterIdEquals100 } };
        var state = new FilterPaneState { Filters = [filter] };

        var updatedFilter = filter with
        {
            Comparison = new FilterComparison { Value = Constants.FilterIdEquals200 },
            IsEnabled = false
        };

        state = FilterPaneReducers.ReduceSetFilter(state, new FilterPaneAction.SetFilter(updatedFilter));

        Assert.Single(state.Filters);
        Assert.Equal(Constants.FilterIdEquals200, state.Filters[0].Comparison.Value);
        Assert.False(state.Filters[0].IsEnabled);
    }

    [Fact]
    public void SubFilterManagement_ShouldHandleNestedFilters()
    {
        var state = new FilterPaneState();

        var parentFilter = new FilterModel
            { Comparison = new FilterComparison { Value = Constants.FilterIdEquals100 } };

        state = FilterPaneReducers.ReduceAddFilter(state, new FilterPaneAction.AddFilter(parentFilter));

        state = FilterPaneReducers.ReduceAddSubFilter(
            state,
            new FilterPaneAction.AddSubFilter(parentFilter.Id));

        Assert.Single(state.Filters[0].SubFilters);

        var subFilterId = state.Filters[0].SubFilters[0].Id;

        state = FilterPaneReducers.ReduceRemoveSubFilter(
            state,
            new FilterPaneAction.RemoveSubFilter(parentFilter.Id, subFilterId));

        Assert.Empty(state.Filters[0].SubFilters);
    }

    [Fact]
    public void ToggleOperations_ShouldAllWorkIndependently()
    {
        var state = new FilterPaneState
        {
            IsEnabled = false,
            IsXmlEnabled = false,
            IsLoading = false
        };

        state = FilterPaneReducers.ReduceToggleIsEnabled(state);
        Assert.True(state.IsEnabled);
        Assert.False(state.IsXmlEnabled);
        Assert.False(state.IsLoading);

        state = FilterPaneReducers.ReduceToggleIsXmlEnabled(state);
        Assert.True(state.IsEnabled);
        Assert.True(state.IsXmlEnabled);
        Assert.False(state.IsLoading);

        state = FilterPaneReducers.ReduceToggleIsLoading(state);
        Assert.True(state.IsEnabled);
        Assert.True(state.IsXmlEnabled);
        Assert.True(state.IsLoading);
    }
}
