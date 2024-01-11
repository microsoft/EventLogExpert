// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Store.FilterGroup;

public sealed record FilterGroupAction
{
    public sealed record AddFilter(Guid ParentId);

    public sealed record AddGroup(FilterGroupModel? FilterGroup = null);

    public sealed record ImportGroups(IEnumerable<FilterGroupModel> Groups);

    public sealed record LoadGroups;

    public sealed record LoadGroupsSuccess(IEnumerable<FilterGroupModel> Groups);

    public sealed record OpenMenu;

    public sealed record RemoveFilter(Guid ParentId, Guid Id);

    public sealed record RemoveGroup(Guid Id);

    public sealed record SetFilter(Guid ParentId, FilterModel Filter);

    public sealed record SetGroup(FilterGroupModel FilterGroup);

    public sealed record ToggleFilter(Guid ParentId, Guid Id);

    public sealed record ToggleGroup(Guid Id);
}
