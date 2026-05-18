// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Common;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Filtering.Runtime;
using EventLogExpert.Runtime.FilterGroup;
using EventLogExpert.Runtime.Tests.TestUtils;
using EventLogExpert.Runtime.Tests.TestUtils.Constants;
using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Filtering.TestUtils.Constants;

namespace EventLogExpert.Runtime.Tests.FilterGroup;

public sealed class FilterGroupStoreTests
{
    [Fact]
    public void FilterGroupAction_AddGroup_WithGroup_ShouldStoreGroup()
    {
        // Arrange
        var group = new SavedFilterGroup { Name = FilterTestConstants.FilterGroupName };

        // Act
        var action = new AddGroupAction(group);

        // Assert
        Assert.NotNull(action.FilterGroup);
        Assert.Equal(FilterTestConstants.FilterGroupName, action.FilterGroup.Name);
    }

    [Fact]
    public void FilterGroupAction_AddGroup_WithNoGroup_ShouldStoreNull()
    {
        // Act
        var action = new AddGroupAction();

        // Assert
        Assert.Null(action.FilterGroup);
    }

    [Fact]
    public void FilterGroupAction_ImportGroups_ShouldStoreGroups()
    {
        // Arrange
        var groups = new List<SavedFilterGroup>
        {
            new() { Name = FilterTestConstants.FilterGroupName },
            new() { Name = FilterTestConstants.FilterGroupNameNested }
        };

        // Act
        var action = new ImportGroupsAction([.. groups]);

        // Assert
        Assert.Equal(2, action.Groups.Count());
    }

    [Fact]
    public void FilterGroupAction_LoadGroupsSuccess_ShouldStoreGroups()
    {
        // Arrange
        var groups = new List<SavedFilterGroup> { new() { Name = FilterTestConstants.FilterGroupName } };

        // Act
        var action = new LoadGroupsSuccessAction([.. groups]);

        // Assert
        Assert.Single(action.Groups);
    }

    [Fact]
    public void FilterGroupAction_RemoveGroupFilter_ShouldStoreParentIdAndFilterId()
    {
        // Arrange
        var parentId = FilterGroupId.Create();
        var filterId = FilterId.Create();

        // Act
        var action = new RemoveGroupFilterAction(parentId, filterId);

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
        var action = new RemoveGroupAction(groupId);

        // Assert
        Assert.Equal(groupId, action.Id);
    }

    [Fact]
    public void FilterGroupAction_SetGroupFilter_ShouldStoreParentIdAndFilter()
    {
        // Arrange
        var parentId = FilterGroupId.Create();
        var filter = FilterFixtures.CreateTestFilter();

        // Act
        var action = new SetGroupFilterAction(parentId, filter);

        // Assert
        Assert.Equal(parentId, action.ParentId);
        Assert.Equal(filter, action.Filter);
    }

    [Fact]
    public void FilterGroupAction_SetGroup_ShouldStoreGroup()
    {
        // Arrange
        var group = new SavedFilterGroup { Name = FilterTestConstants.FilterGroupName };

        // Act
        var action = new SetGroupAction(group);

        // Assert
        Assert.Equal(group, action.FilterGroup);
    }

    [Fact]
    public void FilterGroupAction_ToggleGroupFilterExcluded_ShouldStoreParentIdAndFilterId()
    {
        // Arrange
        var parentId = FilterGroupId.Create();
        var filterId = FilterId.Create();

        // Act
        var action = new ToggleGroupFilterExcludedAction(parentId, filterId);

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
        var action = new ToggleGroupAction(groupId);

        // Assert
        Assert.Equal(groupId, action.Id);
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
        var group = new SavedFilterGroup { Name = FilterTestConstants.FilterGroupName };

        // Act
        var displayName = group.DisplayName;

        // Assert
        Assert.Equal(FilterTestConstants.FilterGroupDisplayName, displayName);
    }

    [Fact]
    public void FilterGroupModel_Id_ShouldBeUnique()
    {
        // Arrange & Act
        var group1 = new SavedFilterGroup();
        var group2 = new SavedFilterGroup();

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

        // Act
        var group = new SavedFilterGroup { Name = FilterTestConstants.FilterGroupName };
        state = Reducers.ReducerAddGroup(state, new AddGroupAction(group));

        // Assert
        Assert.Single(state.Groups);

        // Act
        var groupId = state.Groups.First().Id;
        var filter = FilterFixtures.CreateTestFilter();

        state = Reducers.ReducerSetGroupFilter(state, new SetGroupFilterAction(groupId, filter));

        // Assert
        Assert.Single(state.Groups.First().Filters);
    }

    [Fact]
    public void IntegrationTest_CompleteGroupLifecycle()
    {
        // Arrange
        var state = new FilterGroupState();

        // Act
        var group = new SavedFilterGroup { Name = FilterTestConstants.FilterGroupName };
        state = Reducers.ReducerAddGroup(state, new AddGroupAction(group));
        var groupId = state.Groups.First().Id;

        // Act
        var initialFilter = FilterFixtures.CreateTestFilter(FilterTestConstants.FilterIdEquals100, HighlightColor.Blue);

        state = Reducers.ReducerSetGroupFilter(state, new SetGroupFilterAction(groupId, initialFilter));

        // Assert
        Assert.Equal(HighlightColor.Blue, state.Groups.First().Filters[0].Color);

        // Act
        var filterId = state.Groups.First().Filters[0].Id;
        state = Reducers.ReducerRemoveGroupFilter(state,
            new RemoveGroupFilterAction(groupId, filterId));

        // Assert
        Assert.Empty(state.Groups.First().Filters);

        // Act
        state = Reducers.ReducerRemoveGroup(state, new RemoveGroupAction(groupId));

        // Assert
        Assert.Empty(state.Groups);
    }

    [Fact]
    public void IntegrationTest_DisplayGroupsGeneration()
    {
        // Arrange
        var state = new FilterGroupState();

        var groups = new List<SavedFilterGroup>
        {
            new() { Name = FilterTestConstants.FilterGroupName },
            new() { Name = FilterTestConstants.FilterGroupNameNested },
            new() { Name = "TestSection\\AnotherGroup" }
        };

        // Act
        state = Reducers.ReducerLoadGroupsSuccess(
            state,
            new LoadGroupsSuccessAction([.. groups]));

        // Assert
        Assert.NotEmpty(state.DisplayGroups);
        Assert.True(state.DisplayGroups.ContainsKey(FilterTestConstants.FilterGroupSection));
        var sectionNode = state.DisplayGroups[FilterTestConstants.FilterGroupSection];
        Assert.Equal(2, sectionNode.Groups.Count);
        Assert.True(sectionNode.ChildNodes.ContainsKey(FilterTestConstants.FilterGroupSubSection));
    }

    [Fact]
    public void IntegrationTest_FilterManipulation()
    {
        // Arrange
        var filter = FilterFixtures.CreateTestFilter();

        var group = new SavedFilterGroup
        {
            Name = FilterTestConstants.FilterGroupName,
            Filters = [filter]
        };

        var state = new FilterGroupState { Groups = [group] };

        // Act
        state = Reducers.ReducerToggleGroupFilterExcluded(
            state,
            new ToggleGroupFilterExcludedAction(group.Id, filter.Id));

        // Assert
        Assert.True(state.Groups.First().Filters[0].IsExcluded);

        // Act
        var updatedFilter = state.Groups.First().Filters[0] with
        {
            Color = HighlightColor.Red
        };

        state = Reducers.ReducerSetGroupFilter(
            state,
            new SetGroupFilterAction(group.Id, updatedFilter));

        // Assert
        Assert.Equal(HighlightColor.Red, state.Groups.First().Filters[0].Color);
    }

    [Fact]
    public void IntegrationTest_LoadAndModifyGroups()
    {
        // Arrange
        var state = new FilterGroupState();

        var groups = new List<SavedFilterGroup>
        {
            new() { Name = FilterTestConstants.FilterGroupName },
            new() { Name = FilterTestConstants.FilterGroupNameNested }
        };

        // Act
        state = Reducers.ReducerLoadGroupsSuccess(
            state,
            new LoadGroupsSuccessAction([.. groups]));

        // Assert
        Assert.Equal(2, state.Groups.Count);

        // Act
        var firstGroupId = state.Groups.First().Id;

        state = Reducers.ReducerToggleGroup(
            state,
            new ToggleGroupAction(firstGroupId));

        // Assert
        Assert.True(state.Groups.First(g => g.Id == firstGroupId).IsEditing);
    }

    [Fact]
    public void ReducerAddGroup_ShouldRebuildDisplayGroupsAtomically()
    {
        // Arrange
        var state = new FilterGroupState();
        var group = new SavedFilterGroup { Name = FilterTestConstants.FilterGroupName };
        var action = new AddGroupAction(group);

        // Act
        var newState = Reducers.ReducerAddGroup(state, action);

        // Assert
        Assert.Single(newState.Groups);
        Assert.NotEmpty(newState.DisplayGroups);
        Assert.True(newState.DisplayGroups.ContainsKey(FilterTestConstants.FilterGroupSection));
    }

    [Fact]
    public void ReducerAddGroup_WithGroup_ShouldAddSpecifiedGroup()
    {
        // Arrange
        var state = new FilterGroupState();
        var group = new SavedFilterGroup { Name = FilterTestConstants.FilterGroupName };
        var action = new AddGroupAction(group);

        // Act
        var newState = Reducers.ReducerAddGroup(state, action);

        // Assert
        Assert.Single(newState.Groups);
        Assert.Equal(FilterTestConstants.FilterGroupName, newState.Groups.First().Name);
    }

    [Fact]
    public void ReducerAddGroup_WithNoGroup_ShouldAddNewGroup()
    {
        // Arrange
        var state = new FilterGroupState();
        var action = new AddGroupAction();

        // Act
        var newState = Reducers.ReducerAddGroup(state, action);

        // Assert
        Assert.Single(newState.Groups);
    }

    [Fact]
    public void ReducerImportGroups_ShouldAddMultipleGroups()
    {
        // Arrange
        var existingGroup = new SavedFilterGroup { Name = FilterTestConstants.FilterGroupName };
        var state = new FilterGroupState { Groups = [existingGroup] };

        var newGroups = new List<SavedFilterGroup>
        {
            new() { Name = FilterTestConstants.FilterGroupNameNested },
            new() { Name = "Another\\Group" }
        };

        var action = new ImportGroupsAction([.. newGroups]);

        // Act
        var newState = Reducers.ReducerImportGroups(state, action);

        // Assert
        Assert.Equal(3, newState.Groups.Count);
    }

    [Fact]
    public void ReducerLoadGroupsSuccess_ShouldCreateDisplayHierarchy()
    {
        // Arrange
        var state = new FilterGroupState();

        var groups = new List<SavedFilterGroup>
        {
            new() { Name = FilterTestConstants.FilterGroupName },
            new() { Name = FilterTestConstants.FilterGroupNameNested }
        };

        var action = new LoadGroupsSuccessAction([.. groups]);

        // Act
        var newState = Reducers.ReducerLoadGroupsSuccess(state, action);

        // Assert
        Assert.NotEmpty(newState.DisplayGroups);
        Assert.True(newState.DisplayGroups.ContainsKey(FilterTestConstants.FilterGroupSection));
    }

    [Fact]
    public void ReducerLoadGroupsSuccess_ShouldReplaceAllGroups()
    {
        // Arrange
        var existingGroup = new SavedFilterGroup { Name = "Old\\Group" };
        var state = new FilterGroupState { Groups = [existingGroup] };

        var newGroups = new List<SavedFilterGroup>
        {
            new() { Name = FilterTestConstants.FilterGroupName },
            new() { Name = FilterTestConstants.FilterGroupNameNested }
        };

        var action = new LoadGroupsSuccessAction([.. newGroups]);

        // Act
        var newState = Reducers.ReducerLoadGroupsSuccess(state, action);

        // Assert
        Assert.Equal(2, newState.Groups.Count);
        Assert.DoesNotContain(newState.Groups, g => g.Name == "Old\\Group");
    }

    [Fact]
    public void ReducerLoadGroupsSuccess_WithEmptyGroups_ShouldHaveEmptyDisplay()
    {
        // Arrange
        var state = new FilterGroupState();
        var action = new LoadGroupsSuccessAction([]);

        // Act
        var newState = Reducers.ReducerLoadGroupsSuccess(state, action);

        // Assert
        Assert.Empty(newState.DisplayGroups);
    }

    [Fact]
    public void ReducerRemoveGroupFilter_ShouldRemoveFilterFromGroup()
    {
        // Arrange
        var filter = FilterFixtures.CreateTestFilter();

        var group = new SavedFilterGroup
        {
            Name = FilterTestConstants.FilterGroupName,
            Filters = [filter]
        };

        var state = new FilterGroupState { Groups = [group] };
        var action = new RemoveGroupFilterAction(group.Id, filter.Id);

        // Act
        var newState = Reducers.ReducerRemoveGroupFilter(state, action);

        // Assert
        var updatedGroup = newState.Groups.First(g => g.Id == group.Id);
        Assert.Empty(updatedGroup.Filters);
    }

    [Fact]
    public void ReducerRemoveGroupFilter_WhenFilterNotFound_ShouldReturnSameState()
    {
        // Arrange
        var group = new SavedFilterGroup { Name = FilterTestConstants.FilterGroupName };
        var state = new FilterGroupState { Groups = [group] };
        var action = new RemoveGroupFilterAction(group.Id, FilterId.Create());

        // Act
        var newState = Reducers.ReducerRemoveGroupFilter(state, action);

        // Assert
        Assert.Same(state, newState);
    }

    [Fact]
    public void ReducerRemoveGroupFilter_WhenGroupNotFound_ShouldReturnSameState()
    {
        // Arrange
        var state = new FilterGroupState();
        var action = new RemoveGroupFilterAction(FilterGroupId.Create(), FilterId.Create());

        // Act
        var newState = Reducers.ReducerRemoveGroupFilter(state, action);

        // Assert
        Assert.Same(state, newState);
    }

    [Fact]
    public void ReducerRemoveGroup_ShouldRebuildDisplayGroupsAtomically()
    {
        // Arrange
        var group = new SavedFilterGroup { Name = FilterTestConstants.FilterGroupName };
        var state = Reducers.ReducerAddGroup(
            new FilterGroupState(),
            new AddGroupAction(group));

        // Act
        var newState = Reducers.ReducerRemoveGroup(state, new RemoveGroupAction(group.Id));

        // Assert
        Assert.Empty(newState.Groups);
        Assert.Empty(newState.DisplayGroups);
    }

    [Fact]
    public void ReducerRemoveGroup_ShouldRemoveGroup()
    {
        // Arrange
        var group1 = new SavedFilterGroup { Name = FilterTestConstants.FilterGroupName };
        var group2 = new SavedFilterGroup { Name = FilterTestConstants.FilterGroupNameNested };
        var state = new FilterGroupState { Groups = [group1, group2] };
        var action = new RemoveGroupAction(group1.Id);

        // Act
        var newState = Reducers.ReducerRemoveGroup(state, action);

        // Assert
        Assert.Single(newState.Groups);
        Assert.DoesNotContain(newState.Groups, g => g.Id == group1.Id);
    }

    [Fact]
    public void ReducerRemoveGroup_WhenGroupNotFound_ShouldReturnSameState()
    {
        // Arrange
        var state = new FilterGroupState();
        var action = new RemoveGroupAction(FilterGroupId.Create());

        // Act
        var newState = Reducers.ReducerRemoveGroup(state, action);

        // Assert
        Assert.Same(state, newState);
    }

    [Fact]
    public void ReducerSetGroupFilter_ShouldUpdateFilter()
    {
        // Arrange
        var filter = FilterFixtures.CreateTestFilter();

        var group = new SavedFilterGroup
        {
            Name = FilterTestConstants.FilterGroupName,
            Filters = [filter]
        };

        var state = new FilterGroupState { Groups = [group] };

        var updatedFilter = FilterFixtures.CreateTestFilter(
            FilterTestConstants.FilterIdEquals100,
            HighlightColor.Green,
            id: filter.Id);

        var action = new SetGroupFilterAction(group.Id, updatedFilter);

        // Act
        var newState = Reducers.ReducerSetGroupFilter(state, action);

        // Assert
        var updatedGroup = newState.Groups.First(g => g.Id == group.Id);
        Assert.Single(updatedGroup.Filters);
        var resultFilter = updatedGroup.Filters[0];
        Assert.Equal(HighlightColor.Green, resultFilter.Color);
    }

    [Fact]
    public void ReducerSetGroupFilter_WhenFilterNotFound_ShouldAppendFilter()
    {
        // Arrange
        var group = new SavedFilterGroup { Name = FilterTestConstants.FilterGroupName };
        var state = new FilterGroupState { Groups = [group] };
        var newFilter = FilterFixtures.CreateTestFilter(FilterTestConstants.FilterIdEquals100, HighlightColor.Yellow);
        var action = new SetGroupFilterAction(group.Id, newFilter);

        // Act
        var newState = Reducers.ReducerSetGroupFilter(state, action);

        // Assert
        var resultGroup = newState.Groups.First(g => g.Id == group.Id);

        Assert.Single(resultGroup.Filters);
        Assert.Equal(newFilter.Id, resultGroup.Filters[0].Id);
        Assert.Equal(HighlightColor.Yellow, resultGroup.Filters[0].Color);
    }

    [Fact]
    public void ReducerSetGroupFilter_WhenGroupNotFound_ShouldReturnSameState()
    {
        // Arrange
        var state = new FilterGroupState();
        var action = new SetGroupFilterAction(FilterGroupId.Create(), FilterFixtures.CreateTestFilter());

        // Act
        var newState = Reducers.ReducerSetGroupFilter(state, action);

        // Assert
        Assert.Same(state, newState);
    }

    [Fact]
    public void ReducerSetGroupFilter_WhenModeChanges_ShouldPersistNewMode()
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

        var initial = FilterFixtures.CreateTestFilter(basicFilter: basicFilter,
            mode: FilterMode.Basic);

        var group = new SavedFilterGroup
        {
            Name = FilterTestConstants.FilterGroupName,
            Filters = [initial]
        };

        var state = new FilterGroupState { Groups = [group] };

        var updatedAsCached = FilterFixtures.CreateTestFilter(id: initial.Id,
            mode: FilterMode.Cached);

        // Act
        var newState = Reducers.ReducerSetGroupFilter(
            state,
            new SetGroupFilterAction(group.Id, updatedAsCached));

        // Assert
        var updatedGroup = newState.Groups.First(g => g.Id == group.Id);
        var resultFilter = updatedGroup.Filters.Single();

        Assert.Equal(FilterMode.Cached, resultFilter.Mode);
        Assert.Null(resultFilter.BasicFilter);
    }

    [Fact]
    public void ReducerSetGroup_ShouldUpdateGroup()
    {
        // Arrange
        var group = new SavedFilterGroup { Name = "OldName", IsEditing = true };
        var state = new FilterGroupState { Groups = [group] };

        var updatedGroup = group with
        {
            Name = FilterTestConstants.FilterGroupName,
            Filters = [FilterFixtures.CreateTestFilter()]
        };

        var action = new SetGroupAction(updatedGroup);

        // Act
        var newState = Reducers.ReducerSetGroup(state, action);

        // Assert
        var resultGroup = newState.Groups.First(g => g.Id == group.Id);
        Assert.Equal(FilterTestConstants.FilterGroupName, resultGroup.Name);
        Assert.Single(resultGroup.Filters);
        Assert.False(resultGroup.IsEditing);
    }

    [Fact]
    public void ReducerSetGroup_WhenGroupNotFound_ShouldReturnSameState()
    {
        // Arrange
        var state = new FilterGroupState();
        var action = new SetGroupAction(new SavedFilterGroup());

        // Act
        var newState = Reducers.ReducerSetGroup(state, action);

        // Assert
        Assert.Same(state, newState);
    }

    [Fact]
    public void ReducerToggleGroupFilterExcluded_ShouldToggleIsExcluded()
    {
        // Arrange
        var filter = FilterFixtures.CreateTestFilter(isExcluded: false);

        var group = new SavedFilterGroup
        {
            Name = FilterTestConstants.FilterGroupName,
            Filters = [filter]
        };

        var state = new FilterGroupState { Groups = [group] };
        var action = new ToggleGroupFilterExcludedAction(group.Id, filter.Id);

        // Act
        var newState = Reducers.ReducerToggleGroupFilterExcluded(state, action);

        // Assert
        var updatedGroup = newState.Groups.First(g => g.Id == group.Id);
        Assert.True(updatedGroup.Filters[0].IsExcluded);
    }

    [Fact]
    public void ReducerToggleGroupFilterExcluded_WhenFilterNotFound_ShouldReturnSameState()
    {
        // Arrange
        var group = new SavedFilterGroup { Name = FilterTestConstants.FilterGroupName };
        var state = new FilterGroupState { Groups = [group] };
        var action = new ToggleGroupFilterExcludedAction(group.Id, FilterId.Create());

        // Act
        var newState = Reducers.ReducerToggleGroupFilterExcluded(state, action);

        // Assert
        Assert.Same(state, newState);
    }

    [Fact]
    public void ReducerToggleGroupFilterExcluded_WhenGroupNotFound_ShouldReturnSameState()
    {
        // Arrange
        var state = new FilterGroupState();
        var action = new ToggleGroupFilterExcludedAction(FilterGroupId.Create(), FilterId.Create());

        // Act
        var newState = Reducers.ReducerToggleGroupFilterExcluded(state, action);

        // Assert
        Assert.Same(state, newState);
    }

    [Fact]
    public void ReducerToggleGroup_ShouldToggleIsEditing()
    {
        // Arrange
        var group = new SavedFilterGroup { Name = FilterTestConstants.FilterGroupName, IsEditing = false };
        var state = new FilterGroupState { Groups = [group] };
        var action = new ToggleGroupAction(group.Id);

        // Act
        var newState = Reducers.ReducerToggleGroup(state, action);

        // Assert
        var updatedGroup = newState.Groups.First(g => g.Id == group.Id);
        Assert.True(updatedGroup.IsEditing);
    }

    [Fact]
    public void ReducerToggleGroup_WhenGroupNotFound_ShouldReturnSameState()
    {
        // Arrange
        var state = new FilterGroupState();
        var action = new ToggleGroupAction(FilterGroupId.Create());

        // Act
        var newState = Reducers.ReducerToggleGroup(state, action);

        // Assert
        Assert.Same(state, newState);
    }
}
