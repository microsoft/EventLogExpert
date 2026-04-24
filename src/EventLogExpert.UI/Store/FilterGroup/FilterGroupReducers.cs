// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.FilterGroup;

public sealed class FilterGroupReducers
{
    [ReducerMethod]
    public static FilterGroupState ReducerAddGroup(FilterGroupState state, FilterGroupAction.AddGroup action) =>
        WithGroups(state, state.Groups.Add(action.FilterGroup ?? new FilterGroupModel()));

    [ReducerMethod]
    public static FilterGroupState ReducerImportGroups(FilterGroupState state, FilterGroupAction.ImportGroups action) =>
        WithGroups(state, state.Groups.AddRange(action.Groups));

    [ReducerMethod]
    public static FilterGroupState ReducerLoadGroupsSuccess(
        FilterGroupState state,
        FilterGroupAction.LoadGroupsSuccess action) =>
        WithGroups(state, [.. action.Groups]);

    [ReducerMethod]
    public static FilterGroupState ReducerRemoveFilter(FilterGroupState state, FilterGroupAction.RemoveFilter action)
    {
        var parent = state.Groups.FirstOrDefault(x => x.Id == action.ParentId);

        if (parent is null) { return state; }

        var filter = parent.Filters.FirstOrDefault(x => x.Id == action.Id);

        if (filter is null) { return state; }

        var groupIndex = state.Groups.IndexOf(parent);

        return WithGroups(
            state,
            state.Groups.SetItem(
                groupIndex,
                parent with { Filters = [.. parent.Filters.Where(x => x.Id != action.Id)] }));
    }

    [ReducerMethod]
    public static FilterGroupState ReducerRemoveGroup(FilterGroupState state, FilterGroupAction.RemoveGroup action)
    {
        var group = state.Groups.FirstOrDefault(x => x.Id == action.Id);

        if (group is null) { return state; }

        return WithGroups(state, state.Groups.Remove(group));
    }

    [ReducerMethod]
    public static FilterGroupState ReducerSetFilter(FilterGroupState state, FilterGroupAction.SetFilter action)
    {
        var parent = state.Groups.FirstOrDefault(x => x.Id == action.ParentId);

        if (parent is null) { return state; }

        var groupIndex = state.Groups.IndexOf(parent);
        var existing = parent.Filters.FirstOrDefault(x => x.Id == action.Filter.Id);

        if (existing is null)
        {
            // Upsert append (pending-draft commit path).
            return WithGroups(
                state,
                state.Groups.SetItem(
                    groupIndex,
                    parent with { Filters = [.. parent.Filters, action.Filter] }));
        }

        // `with` preserves IsExcluded/IsEnabled/Data/SubFilters; only Color and Comparison are overridden.
        var updatedFilter = existing with
        {
            Color = action.Filter.Color,
            Comparison = action.Filter.Comparison with { }
        };

        return WithGroups(
            state,
            state.Groups.SetItem(
                groupIndex,
                parent with { Filters = ReplaceFilterById(parent.Filters, existing.Id, updatedFilter) }));
    }

    [ReducerMethod]
    public static FilterGroupState ReducerSetGroup(FilterGroupState state, FilterGroupAction.SetGroup action)
    {
        var group = state.Groups.FirstOrDefault(x => x.Id == action.FilterGroup.Id);

        if (group is null) { return state; }

        var index = state.Groups.IndexOf(group);

        return WithGroups(
            state,
            state.Groups.SetItem(
                index,
                group with
                {
                    Name = action.FilterGroup.Name,
                    Filters = action.FilterGroup.Filters,
                    IsEditing = false
                }));
    }

    [ReducerMethod]
    public static FilterGroupState ReducerToggleFilterExcluded(
        FilterGroupState state,
        FilterGroupAction.ToggleFilterExcluded action) =>
        UpdateFilterInGroup(state,
            action.ParentId,
            action.Id,
            filter => filter with { IsExcluded = !filter.IsExcluded });

    [ReducerMethod]
    public static FilterGroupState ReducerToggleGroup(FilterGroupState state, FilterGroupAction.ToggleGroup action)
    {
        var group = state.Groups.FirstOrDefault(x => x.Id == action.Id);

        if (group is null) { return state; }

        var index = state.Groups.IndexOf(group);

        return WithGroups(
            state,
            state.Groups.SetItem(index, group with { IsEditing = !group.IsEditing }));
    }

    private static IReadOnlyDictionary<string, FilterGroupData> BuildDisplayGroups(
        IEnumerable<FilterGroupModel> groups)
    {
        Dictionary<string, FilterGroupData> displayGroups = [];

        foreach (var group in groups)
        {
            var folders = group.Name.Split('\\');

            displayGroups.AddFilterGroup(folders, group);
        }

        return displayGroups.AsReadOnly();
    }

    private static IReadOnlyList<FilterModel> ReplaceFilterById(
        IReadOnlyList<FilterModel> filters,
        FilterId id,
        FilterModel replacement)
    {
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

        return WithGroups(
            state,
            state.Groups.SetItem(
                groupIndex,
                parent with { Filters = ReplaceFilterById(parent.Filters, filterId, transform(filter)) }));
    }

    private static FilterGroupState WithGroups(FilterGroupState state, ImmutableList<FilterGroupModel> newGroups) =>
        state with { Groups = newGroups, DisplayGroups = BuildDisplayGroups(newGroups) };
}
