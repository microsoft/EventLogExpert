// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Persistence;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterGroup;

internal sealed class Reducers
{
    [ReducerMethod]
    public static FilterGroupState ReducerAddGroup(FilterGroupState state, AddGroupAction action) =>
        WithGroups(state, state.Groups.Add(action.FilterGroup ?? new SavedFilterGroup()));

    [ReducerMethod]
    public static FilterGroupState ReducerImportGroups(FilterGroupState state, ImportGroupsAction action) =>
        WithGroups(state, state.Groups.AddRange(action.Groups));

    [ReducerMethod]
    public static FilterGroupState ReducerLoadGroupsSuccess(
        FilterGroupState state,
        LoadGroupsSuccessAction action) =>
        WithGroups(state, [.. action.Groups]);

    [ReducerMethod]
    public static FilterGroupState ReducerRemoveGroup(FilterGroupState state, RemoveGroupAction action)
    {
        var group = state.Groups.FirstOrDefault(x => x.Id == action.Id);

        return group is null ? state : WithGroups(state, state.Groups.Remove(group));
    }

    [ReducerMethod]
    public static FilterGroupState ReducerRemoveGroupFilter(FilterGroupState state, RemoveGroupFilterAction action)
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
    public static FilterGroupState ReducerSetGroup(FilterGroupState state, SetGroupAction action)
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
    public static FilterGroupState ReducerSetGroupFilter(FilterGroupState state, SetGroupFilterAction action)
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

        // Replace the filter in-place. `with` preserves IsExcluded/IsEnabled. Color, ComparisonText,
        // Compiled, BasicFilter, and Mode are overridden from the incoming action.
        var updatedFilter = existing with
        {
            Color = action.Filter.Color,
            ComparisonText = action.Filter.ComparisonText,
            Compiled = action.Filter.Compiled,
            BasicFilter = action.Filter.BasicFilter,
            Mode = action.Filter.Mode
        };

        return WithGroups(
            state,
            state.Groups.SetItem(
                groupIndex,
                parent with { Filters = ReplaceFilterById(parent.Filters, existing.Id, updatedFilter) }));
    }

    [ReducerMethod]
    public static FilterGroupState ReducerToggleGroup(FilterGroupState state, ToggleGroupAction action)
    {
        var group = state.Groups.FirstOrDefault(x => x.Id == action.Id);

        if (group is null) { return state; }

        var index = state.Groups.IndexOf(group);

        return WithGroups(
            state,
            state.Groups.SetItem(index, group with { IsEditing = !group.IsEditing }));
    }

    [ReducerMethod]
    public static FilterGroupState ReducerToggleGroupFilterExcluded(
        FilterGroupState state,
        ToggleGroupFilterExcludedAction action) =>
        UpdateFilterInGroup(state,
            action.ParentId,
            action.Id,
            filter => filter with { IsExcluded = !filter.IsExcluded });

    private static IReadOnlyDictionary<string, FilterGroupNode> BuildDisplayGroups(
        IEnumerable<SavedFilterGroup> groups)
    {
        Dictionary<string, FilterGroupNode> displayGroups = [];

        foreach (var group in groups)
        {
            var folders = group.Name.Split('\\');

            displayGroups.AddFilterGroup(folders, group);
        }

        return displayGroups.AsReadOnly();
    }

    private static IReadOnlyList<SavedFilter> ReplaceFilterById(
        IReadOnlyList<SavedFilter> filters,
        FilterId id,
        SavedFilter replacement)
    {
        var result = new SavedFilter[filters.Count];

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
        Func<SavedFilter, SavedFilter> transform)
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

    private static FilterGroupState WithGroups(FilterGroupState state, ImmutableList<SavedFilterGroup> newGroups) =>
        state with { Groups = newGroups, DisplayGroups = BuildDisplayGroups(newGroups) };
}
