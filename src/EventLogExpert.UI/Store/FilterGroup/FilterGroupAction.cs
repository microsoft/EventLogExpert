// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Store.FilterGroup;

public sealed record FilterGroupAction
{
    public sealed record AddFilter(FilterGroupId ParentId);

    public sealed record AddGroup(FilterGroupModel? FilterGroup = null);

    public sealed record ImportGroups(IEnumerable<FilterGroupModel> Groups);

    public sealed record LoadGroups;

    public sealed record LoadGroupsSuccess(IEnumerable<FilterGroupModel> Groups);

    public sealed record OpenMenu;

    public sealed record RemoveFilter(FilterGroupId ParentId, FilterId Id);

    public sealed record RemoveGroup(FilterGroupId Id);

    public sealed record SetFilter(FilterGroupId ParentId, FilterModel Filter);

    public sealed record SetGroup(FilterGroupModel FilterGroup);

    public sealed record ToggleFilter(FilterGroupId ParentId, FilterId Id);

    public sealed record ToggleFilterExcluded(FilterGroupId ParentId, FilterId Id);

    public sealed record ToggleGroup(FilterGroupId Id);

    public sealed record UpdateDisplayGroups(IEnumerable<FilterGroupModel> Groups);
}
