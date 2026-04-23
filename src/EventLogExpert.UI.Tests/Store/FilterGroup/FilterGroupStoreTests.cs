// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.FilterGroup;
using EventLogExpert.UI.Tests.TestUtils.Constants;

namespace EventLogExpert.UI.Tests.Store.FilterGroup;

public sealed class FilterGroupStoreTests
{
    [Fact]
    public void FilterGroupAction_AddFilter_ShouldStoreParentId()
    {
        // Arrange
        var parentId = FilterGroupId.Create();

        // Act
        var action = new FilterGroupAction.AddFilter(parentId);

        // Assert
        Assert.Equal(parentId, action.ParentId);
    }

    [Fact]
    public void FilterGroupAction_AddGroup_WithGroup_ShouldStoreGroup()
    {
        // Arrange
        var group = new FilterGroupModel { Name = Constants.FilterGroupName };

        // Act
        var action = new FilterGroupAction.AddGroup(group);

        // Assert
        Assert.NotNull(action.FilterGroup);
        Assert.Equal(Constants.FilterGroupName, action.FilterGroup.Name);
    }

    [Fact]
    public void FilterGroupAction_AddGroup_WithNoGroup_ShouldStoreNull()
    {
        // Act
        var action = new FilterGroupAction.AddGroup();

        // Assert
        Assert.Null(action.FilterGroup);
    }

    [Fact]
    public void FilterGroupAction_ImportGroups_ShouldStoreGroups()
    {
        // Arrange
        var groups = new List<FilterGroupModel>
        {
            new() { Name = Constants.FilterGroupName },
            new() { Name = Constants.FilterGroupNameNested }
        };

        // Act
        var action = new FilterGroupAction.ImportGroups(groups);

        // Assert
        Assert.Equal(2, action.Groups.Count());
    }

    [Fact]
    public void FilterGroupAction_LoadGroupsSuccess_ShouldStoreGroups()
    {
        // Arrange
        var groups = new List<FilterGroupModel> { new() { Name = Constants.FilterGroupName } };

        // Act
        var action = new FilterGroupAction.LoadGroupsSuccess(groups);

        // Assert
        Assert.Single(action.Groups);
    }

    [Fact]
    public void FilterGroupAction_RemoveFilter_ShouldStoreParentIdAndFilterId()
    {
        // Arrange
        var parentId = FilterGroupId.Create();
        var filterId = FilterId.Create();

        // Act
        var action = new FilterGroupAction.RemoveFilter(parentId, filterId);

        // Assert
        Assert.Equal(parentId, action.ParentId);
        Assert.Equal(filterId, action.Id);
    }

    [Fact]
    public void FilterGroupAction_RemoveGroup_ShouldStoreId()
    {
        // Arrange
        var groupId = FilterGroupId.Create();

        // Act
        var action = new FilterGroupAction.RemoveGroup(groupId);

        // Assert
        Assert.Equal(groupId, action.Id);
    }

    [Fact]
    public void FilterGroupAction_SetFilter_ShouldStoreParentIdAndFilter()
    {
        // Arrange
        var parentId = FilterGroupId.Create();
        var filter = new FilterModel();

        // Act
        var action = new FilterGroupAction.SetFilter(parentId, filter);

        // Assert
        Assert.Equal(parentId, action.ParentId);
        Assert.Equal(filter, action.Filter);
    }

    [Fact]
    public void FilterGroupAction_SetGroup_ShouldStoreGroup()
    {
        // Arrange
        var group = new FilterGroupModel { Name = Constants.FilterGroupName };

        // Act
        var action = new FilterGroupAction.SetGroup(group);

        // Assert
        Assert.Equal(group, action.FilterGroup);
    }

    [Fact]
    public void FilterGroupAction_ToggleFilter_ShouldStoreParentIdAndFilterId()
    {
        // Arrange
        var parentId = FilterGroupId.Create();
        var filterId = FilterId.Create();

        // Act
        var action = new FilterGroupAction.ToggleFilter(parentId, filterId);

        // Assert
        Assert.Equal(parentId, action.ParentId);
        Assert.Equal(filterId, action.Id);
    }

    [Fact]
    public void FilterGroupAction_ToggleFilterExcluded_ShouldStoreParentIdAndFilterId()
    {
        // Arrange
        var parentId = FilterGroupId.Create();
        var filterId = FilterId.Create();

        // Act
        var action = new FilterGroupAction.ToggleFilterExcluded(parentId, filterId);

        // Assert
        Assert.Equal(parentId, action.ParentId);
        Assert.Equal(filterId, action.Id);
    }

    [Fact]
    public void FilterGroupAction_ToggleGroup_ShouldStoreId()
    {
        // Arrange
        var groupId = FilterGroupId.Create();

        // Act
        var action = new FilterGroupAction.ToggleGroup(groupId);

        // Assert
        Assert.Equal(groupId, action.Id);
    }

    [Fact]
    public void FilterGroupAction_UpdateDisplayGroups_ShouldStoreGroups()
    {
        // Arrange
        var groups = new List<FilterGroupModel> { new() { Name = Constants.FilterGroupName } };

        // Act
        var action = new FilterGroupAction.UpdateDisplayGroups(groups);

        // Assert
        Assert.Single(action.Groups);
    }

    [Fact]
    public void FilterGroupId_Create_ShouldGenerateUniqueIds()
    {
        // Arrange & Act
        var id1 = FilterGroupId.Create();
        var id2 = FilterGroupId.Create();

        // Assert
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void FilterGroupModel_DisplayName_ShouldReturnLastSegment()
    {
        // Arrange
        var group = new FilterGroupModel { Name = Constants.FilterGroupName };

        // Act
        var displayName = group.DisplayName;

        // Assert
        Assert.Equal(Constants.FilterGroupDisplayName, displayName);
    }

    [Fact]
    public void FilterGroupModel_Id_ShouldBeUnique()
    {
        // Arrange & Act
        var group1 = new FilterGroupModel();
        var group2 = new FilterGroupModel();

        // Assert
        Assert.NotEqual(group1.Id, group2.Id);
    }

    [Fact]
    public void FilterGroupState_DefaultState_ShouldHaveCorrectDefaults()
    {
        // Arrange & Act
        var state = new FilterGroupState();

        // Assert
        Assert.Empty(state.Groups);
        Assert.Empty(state.DisplayGroups);
    }

    [Fact]
    public void IntegrationTest_AddGroupAndFilters()
    {
        // Arrange
        var state = new FilterGroupState();

        // Act - Add group
        var group = new FilterGroupModel { Name = Constants.FilterGroupName };
        state = FilterGroupReducers.ReducerAddGroup(state, new FilterGroupAction.AddGroup(group));

        // Assert
        Assert.Single(state.Groups);

        // Act - Add filter
        var groupId = state.Groups.First().Id;
        state = FilterGroupReducers.ReducerAddFilter(state, new FilterGroupAction.AddFilter(groupId));

        // Assert
        Assert.Single(state.Groups.First().Filters);
        Assert.True(state.Groups.First().Filters[0].IsEditing);
    }

    [Fact]
    public void IntegrationTest_CompleteGroupLifecycle()
    {
        // Arrange
        var state = new FilterGroupState();

        // Act - Add group
        var group = new FilterGroupModel { Name = Constants.FilterGroupName };
        state = FilterGroupReducers.ReducerAddGroup(state, new FilterGroupAction.AddGroup(group));
        var groupId = state.Groups.First().Id;

        // Act - Add filter
        state = FilterGroupReducers.ReducerAddFilter(state, new FilterGroupAction.AddFilter(groupId));
        var filterId = state.Groups.First().Filters[0].Id;

        // Act - Set filter
        var filter = state.Groups.First().Filters[0] with
        {
            Color = HighlightColor.Blue,
            Comparison = new FilterComparison { Value = Constants.FilterIdEquals100 }
        };

        state = FilterGroupReducers.ReducerSetFilter(state, new FilterGroupAction.SetFilter(groupId, filter));

        // Assert
        Assert.Equal(HighlightColor.Blue, state.Groups.First().Filters[0].Color);
        Assert.False(state.Groups.First().Filters[0].IsEditing);

        // Act - Remove filter (note: SetFilter creates a new filter with a different ID)
        var updatedFilterId = state.Groups.First().Filters[0].Id;
        state = FilterGroupReducers.ReducerRemoveFilter(state,
            new FilterGroupAction.RemoveFilter(groupId, updatedFilterId));

        // Assert
        Assert.Empty(state.Groups.First().Filters);

        // Act - Remove group
        state = FilterGroupReducers.ReducerRemoveGroup(state, new FilterGroupAction.RemoveGroup(groupId));

        // Assert
        Assert.Empty(state.Groups);
    }

    [Fact]
    public void IntegrationTest_DisplayGroupsGeneration()
    {
        // Arrange
        var state = new FilterGroupState();

        var groups = new List<FilterGroupModel>
        {
            new() { Name = Constants.FilterGroupName },
            new() { Name = Constants.FilterGroupNameNested },
            new() { Name = "TestSection\\AnotherGroup" }
        };

        // Act - Load groups
        state = FilterGroupReducers.ReducerLoadGroupsSuccess(
            state,
            new FilterGroupAction.LoadGroupsSuccess(groups));

        // Act - Update display groups
        state = FilterGroupReducers.ReducerUpdateDisplayGroups(
            state,
            new FilterGroupAction.UpdateDisplayGroups(state.Groups));

        // Assert
        Assert.NotEmpty(state.DisplayGroups);
        Assert.True(state.DisplayGroups.ContainsKey(Constants.FilterGroupSection));
        var sectionData = state.DisplayGroups[Constants.FilterGroupSection];
        Assert.Equal(2, sectionData.FilterGroups.Count);
        Assert.True(sectionData.ChildGroup.ContainsKey(Constants.FilterGroupSubSection));
    }

    [Fact]
    public void IntegrationTest_FilterManipulation()
    {
        // Arrange
        var filter = new FilterModel { IsEditing = true };

        var group = new FilterGroupModel
        {
            Name = Constants.FilterGroupName,
            Filters = [filter]
        };

        var state = new FilterGroupState { Groups = [group] };

        // Act - Toggle excluded
        state = FilterGroupReducers.ReducerToggleFilterExcluded(
            state,
            new FilterGroupAction.ToggleFilterExcluded(group.Id, filter.Id));

        // Assert
        Assert.True(state.Groups.First().Filters[0].IsExcluded);

        // Act - Set filter
        var updatedFilter = state.Groups.First().Filters[0] with
        {
            Color = HighlightColor.Red
        };

        state = FilterGroupReducers.ReducerSetFilter(
            state,
            new FilterGroupAction.SetFilter(group.Id, updatedFilter));

        // Assert
        Assert.Equal(HighlightColor.Red, state.Groups.First().Filters[0].Color);
        Assert.False(state.Groups.First().Filters[0].IsEditing);
    }

    [Fact]
    public void IntegrationTest_LoadAndModifyGroups()
    {
        // Arrange
        var state = new FilterGroupState();

        var groups = new List<FilterGroupModel>
        {
            new() { Name = Constants.FilterGroupName },
            new() { Name = Constants.FilterGroupNameNested }
        };

        // Act - Load groups
        state = FilterGroupReducers.ReducerLoadGroupsSuccess(
            state,
            new FilterGroupAction.LoadGroupsSuccess(groups));

        // Assert
        Assert.Equal(2, state.Groups.Count);

        // Act - Toggle group editing
        var firstGroupId = state.Groups.First().Id;

        state = FilterGroupReducers.ReducerToggleGroup(
            state,
            new FilterGroupAction.ToggleGroup(firstGroupId));

        // Assert
        Assert.True(state.Groups.First(g => g.Id == firstGroupId).IsEditing);
    }

    [Fact]
    public void ReducerAddFilter_ShouldAddNewFilterToGroup()
    {
        // Arrange
        var group = new FilterGroupModel { Name = Constants.FilterGroupName };
        var state = new FilterGroupState { Groups = [group] };
        var action = new FilterGroupAction.AddFilter(group.Id);

        // Act
        var newState = FilterGroupReducers.ReducerAddFilter(state, action);

        // Assert
        var updatedGroup = newState.Groups.First(g => g.Id == group.Id);
        Assert.Single(updatedGroup.Filters);
        Assert.True(updatedGroup.Filters[0].IsEditing);
    }

    [Fact]
    public void ReducerAddFilter_WhenGroupNotFound_ShouldReturnSameState()
    {
        // Arrange
        var state = new FilterGroupState();
        var action = new FilterGroupAction.AddFilter(FilterGroupId.Create());

        // Act
        var newState = FilterGroupReducers.ReducerAddFilter(state, action);

        // Assert
        Assert.Same(state, newState);
    }

    [Fact]
    public void ReducerAddGroup_WithGroup_ShouldAddSpecifiedGroup()
    {
        // Arrange
        var state = new FilterGroupState();
        var group = new FilterGroupModel { Name = Constants.FilterGroupName };
        var action = new FilterGroupAction.AddGroup(group);

        // Act
        var newState = FilterGroupReducers.ReducerAddGroup(state, action);

        // Assert
        Assert.Single(newState.Groups);
        Assert.Equal(Constants.FilterGroupName, newState.Groups.First().Name);
    }

    [Fact]
    public void ReducerAddGroup_WithNoGroup_ShouldAddNewGroup()
    {
        // Arrange
        var state = new FilterGroupState();
        var action = new FilterGroupAction.AddGroup();

        // Act
        var newState = FilterGroupReducers.ReducerAddGroup(state, action);

        // Assert
        Assert.Single(newState.Groups);
    }

    [Fact]
    public void ReducerImportGroups_ShouldAddMultipleGroups()
    {
        // Arrange
        var existingGroup = new FilterGroupModel { Name = Constants.FilterGroupName };
        var state = new FilterGroupState { Groups = [existingGroup] };

        var newGroups = new List<FilterGroupModel>
        {
            new() { Name = Constants.FilterGroupNameNested },
            new() { Name = "Another\\Group" }
        };

        var action = new FilterGroupAction.ImportGroups(newGroups);

        // Act
        var newState = FilterGroupReducers.ReducerImportGroups(state, action);

        // Assert
        Assert.Equal(3, newState.Groups.Count);
    }

    [Fact]
    public void ReducerLoadGroupsSuccess_ShouldReplaceAllGroups()
    {
        // Arrange
        var existingGroup = new FilterGroupModel { Name = "Old\\Group" };
        var state = new FilterGroupState { Groups = [existingGroup] };

        var newGroups = new List<FilterGroupModel>
        {
            new() { Name = Constants.FilterGroupName },
            new() { Name = Constants.FilterGroupNameNested }
        };

        var action = new FilterGroupAction.LoadGroupsSuccess(newGroups);

        // Act
        var newState = FilterGroupReducers.ReducerLoadGroupsSuccess(state, action);

        // Assert
        Assert.Equal(2, newState.Groups.Count);
        Assert.DoesNotContain(newState.Groups, g => g.Name == "Old\\Group");
    }

    [Fact]
    public void ReducerRemoveFilter_ShouldRemoveFilterFromGroup()
    {
        // Arrange
        var filter = new FilterModel();

        var group = new FilterGroupModel
        {
            Name = Constants.FilterGroupName,
            Filters = [filter]
        };

        var state = new FilterGroupState { Groups = [group] };
        var action = new FilterGroupAction.RemoveFilter(group.Id, filter.Id);

        // Act
        var newState = FilterGroupReducers.ReducerRemoveFilter(state, action);

        // Assert
        var updatedGroup = newState.Groups.First(g => g.Id == group.Id);
        Assert.Empty(updatedGroup.Filters);
    }

    [Fact]
    public void ReducerRemoveFilter_WhenFilterNotFound_ShouldReturnSameState()
    {
        // Arrange
        var group = new FilterGroupModel { Name = Constants.FilterGroupName };
        var state = new FilterGroupState { Groups = [group] };
        var action = new FilterGroupAction.RemoveFilter(group.Id, FilterId.Create());

        // Act
        var newState = FilterGroupReducers.ReducerRemoveFilter(state, action);

        // Assert
        Assert.Same(state, newState);
    }

    [Fact]
    public void ReducerRemoveFilter_WhenGroupNotFound_ShouldReturnSameState()
    {
        // Arrange
        var state = new FilterGroupState();
        var action = new FilterGroupAction.RemoveFilter(FilterGroupId.Create(), FilterId.Create());

        // Act
        var newState = FilterGroupReducers.ReducerRemoveFilter(state, action);

        // Assert
        Assert.Same(state, newState);
    }

    [Fact]
    public void ReducerRemoveGroup_ShouldRemoveGroup()
    {
        // Arrange
        var group1 = new FilterGroupModel { Name = Constants.FilterGroupName };
        var group2 = new FilterGroupModel { Name = Constants.FilterGroupNameNested };
        var state = new FilterGroupState { Groups = [group1, group2] };
        var action = new FilterGroupAction.RemoveGroup(group1.Id);

        // Act
        var newState = FilterGroupReducers.ReducerRemoveGroup(state, action);

        // Assert
        Assert.Single(newState.Groups);
        Assert.DoesNotContain(newState.Groups, g => g.Id == group1.Id);
    }

    [Fact]
    public void ReducerRemoveGroup_WhenGroupNotFound_ShouldReturnSameState()
    {
        // Arrange
        var state = new FilterGroupState();
        var action = new FilterGroupAction.RemoveGroup(FilterGroupId.Create());

        // Act
        var newState = FilterGroupReducers.ReducerRemoveGroup(state, action);

        // Assert
        Assert.Same(state, newState);
    }

    [Fact]
    public void ReducerSetFilter_ShouldUpdateFilter()
    {
        // Arrange
        var filter = new FilterModel { IsEditing = true };

        var group = new FilterGroupModel
        {
            Name = Constants.FilterGroupName,
            Filters = [filter]
        };

        var state = new FilterGroupState { Groups = [group] };

        var updatedFilter = filter with
        {
            Color = HighlightColor.Green,
            Comparison = new FilterComparison { Value = Constants.FilterIdEquals100 }
        };

        var action = new FilterGroupAction.SetFilter(group.Id, updatedFilter);

        // Act
        var newState = FilterGroupReducers.ReducerSetFilter(state, action);

        // Assert
        var updatedGroup = newState.Groups.First(g => g.Id == group.Id);
        Assert.Single(updatedGroup.Filters);
        var resultFilter = updatedGroup.Filters[0];
        Assert.Equal(HighlightColor.Green, resultFilter.Color);
        Assert.False(resultFilter.IsEditing);
    }

    [Fact]
    public void ReducerSetFilter_WhenFilterNotFound_ShouldAppendFilter()
    {
        // Arrange — pending-draft commit path: parent group exists but the filter Id is brand new
        // (FilterGroup.OnPendingDraftSaved fires SetFilter for a draft that was never in state).
        var group = new FilterGroupModel { Name = Constants.FilterGroupName };
        var state = new FilterGroupState { Groups = [group] };
        var newFilter = new FilterModel
        {
            Color = HighlightColor.Yellow,
            Comparison = new FilterComparison { Value = Constants.FilterIdEquals100 },
            IsEditing = true // upsert must clear this
        };
        var action = new FilterGroupAction.SetFilter(group.Id, newFilter);

        // Act
        var newState = FilterGroupReducers.ReducerSetFilter(state, action);

        // Assert
        var resultGroup = newState.Groups.First(group => group.Id == group.Id);

        Assert.Single(resultGroup.Filters);
        Assert.Equal(newFilter.Id, resultGroup.Filters[0].Id);
        Assert.Equal(HighlightColor.Yellow, resultGroup.Filters[0].Color);
        Assert.False(resultGroup.Filters[0].IsEditing);
    }

    [Fact]
    public void ReducerSetFilter_WhenGroupNotFound_ShouldReturnSameState()
    {
        // Arrange
        var state = new FilterGroupState();
        var action = new FilterGroupAction.SetFilter(FilterGroupId.Create(), new FilterModel());

        // Act
        var newState = FilterGroupReducers.ReducerSetFilter(state, action);

        // Assert
        Assert.Same(state, newState);
    }

    [Fact]
    public void ReducerSetGroup_ShouldUpdateGroup()
    {
        // Arrange
        var group = new FilterGroupModel { Name = "OldName", IsEditing = true };
        var state = new FilterGroupState { Groups = [group] };

        var updatedGroup = group with
        {
            Name = Constants.FilterGroupName,
            Filters = [new FilterModel()]
        };

        var action = new FilterGroupAction.SetGroup(updatedGroup);

        // Act
        var newState = FilterGroupReducers.ReducerSetGroup(state, action);

        // Assert
        var resultGroup = newState.Groups.First(g => g.Id == group.Id);
        Assert.Equal(Constants.FilterGroupName, resultGroup.Name);
        Assert.Single(resultGroup.Filters);
        Assert.False(resultGroup.IsEditing);
    }

    [Fact]
    public void ReducerSetGroup_WhenGroupNotFound_ShouldReturnSameState()
    {
        // Arrange
        var state = new FilterGroupState();
        var action = new FilterGroupAction.SetGroup(new FilterGroupModel());

        // Act
        var newState = FilterGroupReducers.ReducerSetGroup(state, action);

        // Assert
        Assert.Same(state, newState);
    }

    [Fact]
    public void ReducerToggleFilter_ShouldToggleIsEditing()
    {
        // Arrange
        var filter = new FilterModel { IsEditing = false };

        var group = new FilterGroupModel
        {
            Name = Constants.FilterGroupName,
            Filters = [filter]
        };

        var state = new FilterGroupState { Groups = [group] };
        var action = new FilterGroupAction.ToggleFilter(group.Id, filter.Id);

        // Act
        var newState = FilterGroupReducers.ReducerToggleFilter(state, action);

        // Assert
        var updatedGroup = newState.Groups.First(g => g.Id == group.Id);
        Assert.True(updatedGroup.Filters[0].IsEditing);
    }

    [Fact]
    public void ReducerToggleFilter_WhenFilterNotFound_ShouldReturnSameState()
    {
        // Arrange
        var group = new FilterGroupModel { Name = Constants.FilterGroupName };
        var state = new FilterGroupState { Groups = [group] };
        var action = new FilterGroupAction.ToggleFilter(group.Id, FilterId.Create());

        // Act
        var newState = FilterGroupReducers.ReducerToggleFilter(state, action);

        // Assert
        Assert.Same(state, newState);
    }

    [Fact]
    public void ReducerToggleFilter_WhenGroupNotFound_ShouldReturnSameState()
    {
        // Arrange
        var state = new FilterGroupState();
        var action = new FilterGroupAction.ToggleFilter(FilterGroupId.Create(), FilterId.Create());

        // Act
        var newState = FilterGroupReducers.ReducerToggleFilter(state, action);

        // Assert
        Assert.Same(state, newState);
    }

    [Fact]
    public void ReducerToggleFilterExcluded_ShouldToggleIsExcluded()
    {
        // Arrange
        var filter = new FilterModel { IsExcluded = false };

        var group = new FilterGroupModel
        {
            Name = Constants.FilterGroupName,
            Filters = [filter]
        };

        var state = new FilterGroupState { Groups = [group] };
        var action = new FilterGroupAction.ToggleFilterExcluded(group.Id, filter.Id);

        // Act
        var newState = FilterGroupReducers.ReducerToggleFilterExcluded(state, action);

        // Assert
        var updatedGroup = newState.Groups.First(g => g.Id == group.Id);
        Assert.True(updatedGroup.Filters[0].IsExcluded);
    }

    [Fact]
    public void ReducerToggleFilterExcluded_WhenFilterNotFound_ShouldReturnSameState()
    {
        // Arrange
        var group = new FilterGroupModel { Name = Constants.FilterGroupName };
        var state = new FilterGroupState { Groups = [group] };
        var action = new FilterGroupAction.ToggleFilterExcluded(group.Id, FilterId.Create());

        // Act
        var newState = FilterGroupReducers.ReducerToggleFilterExcluded(state, action);

        // Assert
        Assert.Same(state, newState);
    }

    [Fact]
    public void ReducerToggleFilterExcluded_WhenGroupNotFound_ShouldReturnSameState()
    {
        // Arrange
        var state = new FilterGroupState();
        var action = new FilterGroupAction.ToggleFilterExcluded(FilterGroupId.Create(), FilterId.Create());

        // Act
        var newState = FilterGroupReducers.ReducerToggleFilterExcluded(state, action);

        // Assert
        Assert.Same(state, newState);
    }

    [Fact]
    public void ReducerToggleGroup_ShouldToggleIsEditing()
    {
        // Arrange
        var group = new FilterGroupModel { Name = Constants.FilterGroupName, IsEditing = false };
        var state = new FilterGroupState { Groups = [group] };
        var action = new FilterGroupAction.ToggleGroup(group.Id);

        // Act
        var newState = FilterGroupReducers.ReducerToggleGroup(state, action);

        // Assert
        var updatedGroup = newState.Groups.First(g => g.Id == group.Id);
        Assert.True(updatedGroup.IsEditing);
    }

    [Fact]
    public void ReducerToggleGroup_WhenGroupNotFound_ShouldReturnSameState()
    {
        // Arrange
        var state = new FilterGroupState();
        var action = new FilterGroupAction.ToggleGroup(FilterGroupId.Create());

        // Act
        var newState = FilterGroupReducers.ReducerToggleGroup(state, action);

        // Assert
        Assert.Same(state, newState);
    }

    [Fact]
    public void ReducerUpdateDisplayGroups_ShouldCreateDisplayHierarchy()
    {
        // Arrange
        var state = new FilterGroupState();

        var groups = new List<FilterGroupModel>
        {
            new() { Name = Constants.FilterGroupName },
            new() { Name = Constants.FilterGroupNameNested }
        };

        var action = new FilterGroupAction.UpdateDisplayGroups(groups);

        // Act
        var newState = FilterGroupReducers.ReducerUpdateDisplayGroups(state, action);

        // Assert
        Assert.NotEmpty(newState.DisplayGroups);
        Assert.True(newState.DisplayGroups.ContainsKey(Constants.FilterGroupSection));
    }

    [Fact]
    public void ReducerUpdateDisplayGroups_WithEmptyGroups_ShouldHaveEmptyDisplay()
    {
        // Arrange
        var state = new FilterGroupState();
        var action = new FilterGroupAction.UpdateDisplayGroups([]);

        // Act
        var newState = FilterGroupReducers.ReducerUpdateDisplayGroups(state, action);

        // Assert
        Assert.Empty(newState.DisplayGroups);
    }
}
