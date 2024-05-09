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

        return state with
        {
            Groups = state.Groups
                .Remove(group)
                .Add(group with { Filters = group.Filters.Concat([new FilterModel { IsEditing = true }]) })
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

        return state with
        {
            Groups = state.Groups
                .Remove(parent)
                .Add(parent with { Filters = [.. parent.Filters.Where(x => x.Id != action.Id)] })
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

        return state with
        {
            Groups = state.Groups
                .Remove(parent)
                .Add(parent with
                {
                    Filters =
                    [
                        .. parent.Filters.Where(x => x.Id != action.Filter.Id),
                        new FilterModel
                        {
                            Color = action.Filter.Color,
                            Comparison = action.Filter.Comparison with { },
                            IsEditing = false
                        }
                    ]
                })
        };
    }

    [ReducerMethod]
    public static FilterGroupState ReducerSetGroup(FilterGroupState state, FilterGroupAction.SetGroup action)
    {
        var group = state.Groups.FirstOrDefault(x => x.Id == action.FilterGroup.Id);

        if (group is null) { return state; }

        return state with
        {
            Groups = state.Groups
                .Remove(group)
                .Add(group with
                {
                    Name = action.FilterGroup.Name,
                    Filters = action.FilterGroup.Filters,
                    IsEditing = false
                })
        };
    }

    [ReducerMethod]
    public static FilterGroupState ReducerToggleFilter(FilterGroupState state, FilterGroupAction.ToggleFilter action)
    {
        var parent = state.Groups.FirstOrDefault(x => x.Id == action.ParentId);

        if (parent is null) { return state; }

        var filter = parent.Filters.FirstOrDefault(x => x.Id == action.Id);

        if (filter is null) { return state; }

        return state with
        {
            Groups = state.Groups
                .Remove(parent)
                .Add(parent with
                {
                    Filters =
                    [
                        .. parent.Filters.Where(x => x.Id != action.Id),
                        filter with { IsEditing = !filter.IsEditing }
                    ]
                })
        };
    }

    [ReducerMethod]
    public static FilterGroupState ReducerToggleFilterExcluded(
        FilterGroupState state,
        FilterGroupAction.ToggleFilterExcluded action)
    {
        var parent = state.Groups.FirstOrDefault(x => x.Id == action.ParentId);

        if (parent is null) { return state; }

        var filter = parent.Filters.FirstOrDefault(x => x.Id == action.Id);

        if (filter is null) { return state; }

        return state with
        {
            Groups = state.Groups
                .Remove(parent)
                .Add(parent with
                {
                    Filters =
                    [
                        .. parent.Filters.Where(x => x.Id != action.Id), filter with { IsExcluded = !filter.IsExcluded }
                    ]
                })
        };
    }

    [ReducerMethod]
    public static FilterGroupState ReducerToggleGroup(FilterGroupState state, FilterGroupAction.ToggleGroup action)
    {
        var group = state.Groups.FirstOrDefault(x => x.Id == action.Id);

        if (group is null) { return state; }

        return state with
        {
            Groups = state.Groups
                .Remove(group)
                .Add(group with { IsEditing = !group.IsEditing })
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
}
