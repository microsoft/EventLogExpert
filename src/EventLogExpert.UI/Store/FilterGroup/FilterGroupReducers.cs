// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Fluxor;

namespace EventLogExpert.UI.Store.FilterGroup;

public sealed class FilterGroupReducers
{
    [ReducerMethod]
    public static FilterGroupState ReducerAddGroup(FilterGroupState state, FilterGroupAction.AddGroup action) =>
        state with { Groups = state.Groups.Add(action.FilterGroup ?? new FilterGroupModel()) };

    [ReducerMethod]
    public static FilterGroupState ReducerRemoveGroup(FilterGroupState state, FilterGroupAction.RemoveGroup action)
    {
        var group = state.Groups.FirstOrDefault(x => x.Id == action.Id);

        if (group is null) { return state; }

        return state with { Groups = state.Groups.Remove(group) };
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
                    IsEditing = action.FilterGroup.IsEditing
                })
        };
    }
}
