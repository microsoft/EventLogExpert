// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Fluxor;

namespace EventLogExpert.UI.Store.FilterGroup;

public sealed class FilterGroupReducers
{
    [ReducerMethod]
    public static FilterGroupState ReducerAddGroup(FilterGroupState state, FilterGroupAction.AddGroup action) => state with
    {
        Groups = state.Groups.Add(action.FilterGroup ?? new FilterGroupModel())
    };
}
