// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Fluxor;

namespace EventLogExpert.UI.Store.FilterGroup;

public sealed class FilterGroupReducers
{
    [ReducerMethod]
    public static FilterGroupState ReducerAddFilter(FilterGroupState state, FilterGroupAction.AddFilter action)
    {
        var group = state.Groups.FirstOrDefault(x => x.Id == action.ParentId);

        if (group is null) { return state; }

        var index = state.Groups.IndexOf(group);

        return state with
        {
            Groups = state.Groups.SetItem(
                index,
                group with { Filters = [.. group.Filters, new FilterModel { IsEditing = true }] })
        };
    }

    [ReducerMethod]
    public static FilterGroupState ReducerAddGroup(FilterGroupState state, FilterGroupAction.AddGroup action) =>
        state with { Groups = state.Groups.Add(action.FilterGroup ?? new FilterGroupModel()) };

    [ReducerMethod]
    public static FilterGroupState ReducerImportGroups(FilterGroupState state, FilterGroupAction.ImportGroups action) =>
        state with { Groups = state.Groups.AddRange(action.Groups) };

    [ReducerMethod]
    public static FilterGroupState ReducerLoadGroupsSuccess(
        FilterGroupState state,
        FilterGroupAction.LoadGroupsSuccess action) => state with { Groups = [.. action.Groups] };

    [ReducerMethod]
    public static FilterGroupState ReducerRemoveFilter(FilterGroupState state, FilterGroupAction.RemoveFilter action)
    {
        var parent = state.Groups.FirstOrDefault(x => x.Id == action.ParentId);

        if (parent is null) { return state; }

        var filter = parent.Filters.FirstOrDefault(x => x.Id == action.Id);

        if (filter is null) { return state; }

        var groupIndex = state.Groups.IndexOf(parent);

        return state with
        {
            Groups = state.Groups.SetItem(
                groupIndex,
                parent with { Filters = [.. parent.Filters.Where(x => x.Id != action.Id)] })
        };
    }

    [ReducerMethod]
    public static FilterGroupState ReducerRemoveGroup(FilterGroupState state, FilterGroupAction.RemoveGroup action)
    {
        var group = state.Groups.FirstOrDefault(x => x.Id == action.Id);

        if (group is null) { return state; }

        return state with { Groups = state.Groups.Remove(group) };
    }

    [ReducerMethod]
    public static FilterGroupState ReducerSetFilter(FilterGroupState state, FilterGroupAction.SetFilter action)
    {
        var parent = state.Groups.FirstOrDefault(x => x.Id == action.ParentId);

        if (parent is null) { return state; }

        var filter = parent.Filters.FirstOrDefault(x => x.Id == action.Filter.Id);

        if (filter is null) { return state; }

        var groupIndex = state.Groups.IndexOf(parent);

        // Preserve all fields not explicitly overridden by the action (IsExcluded, IsEnabled,
        // Data, SubFilters, etc.). Previously a partial new FilterModel was constructed which
        // silently dropped those fields.
        var updatedFilter = filter with
        {
            Color = action.Filter.Color,
            Comparison = action.Filter.Comparison with { },
            IsEditing = false
        };

        return state with
        {
            Groups = state.Groups.SetItem(
                groupIndex,
                parent with { Filters = ReplaceFilterById(parent.Filters, filter.Id, updatedFilter) })
        };
    }

    [ReducerMethod]
    public static FilterGroupState ReducerSetGroup(FilterGroupState state, FilterGroupAction.SetGroup action)
    {
        var group = state.Groups.FirstOrDefault(x => x.Id == action.FilterGroup.Id);

        if (group is null) { return state; }

        var index = state.Groups.IndexOf(group);

        return state with
        {
            Groups = state.Groups.SetItem(
                index,
                group with
                {
                    Name = action.FilterGroup.Name,
                    Filters = action.FilterGroup.Filters,
                    IsEditing = false
                })
        };
    }

    [ReducerMethod]
    public static FilterGroupState ReducerToggleFilter(FilterGroupState state, FilterGroupAction.ToggleFilter action) =>
        UpdateFilterInGroup(state, action.ParentId, action.Id, filter => filter with { IsEditing = !filter.IsEditing });

    [ReducerMethod]
    public static FilterGroupState ReducerToggleFilterExcluded(
        FilterGroupState state,
        FilterGroupAction.ToggleFilterExcluded action) =>
        UpdateFilterInGroup(state, action.ParentId, action.Id, filter => filter with { IsExcluded = !filter.IsExcluded });

    [ReducerMethod]
    public static FilterGroupState ReducerToggleGroup(FilterGroupState state, FilterGroupAction.ToggleGroup action)
    {
        var group = state.Groups.FirstOrDefault(x => x.Id == action.Id);

        if (group is null) { return state; }

        var index = state.Groups.IndexOf(group);

        return state with
        {
            Groups = state.Groups.SetItem(index, group with { IsEditing = !group.IsEditing })
        };
    }

    [ReducerMethod]
    public static FilterGroupState ReducerUpdateDisplayGroups(FilterGroupState state,
        FilterGroupAction.UpdateDisplayGroups action)
    {
        Dictionary<string, FilterGroupData> displayGroups = [];

        foreach (var group in action.Groups)
        {
            var folders = group.Name.Split('\\');

            displayGroups.AddFilterGroup(folders, group);
        }

        return state with { DisplayGroups = displayGroups.AsReadOnly() };
    }

    private static IReadOnlyList<FilterModel> ReplaceFilterById(
        IReadOnlyList<FilterModel> filters,
        FilterId id,
        FilterModel replacement)
    {
        // Order-preserving replacement. FilterGroupModel.Filters is IReadOnlyList (not ImmutableList)
        // so we materialize to an array; allocation is O(n) which is fine for the small group sizes used.
        var result = new FilterModel[filters.Count];

        for (var index = 0; index < filters.Count; index++)
        {
            result[index] = filters[index].Id == id ? replacement : filters[index];
        }

        return result;
    }

    private static FilterGroupState UpdateFilterInGroup(
        FilterGroupState state,
        FilterGroupId parentId,
        FilterId filterId,
        Func<FilterModel, FilterModel> transform)
    {
        var parent = state.Groups.FirstOrDefault(x => x.Id == parentId);

        if (parent is null) { return state; }

        var filter = parent.Filters.FirstOrDefault(x => x.Id == filterId);

        if (filter is null) { return state; }

        var groupIndex = state.Groups.IndexOf(parent);

        return state with
        {
            Groups = state.Groups.SetItem(
                groupIndex,
                parent with { Filters = ReplaceFilterById(parent.Filters, filterId, transform(filter)) })
        };
    }
}
