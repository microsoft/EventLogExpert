// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Store.FilterGroup;

public sealed record FilterGroupAction
{
    public sealed record AddGroup(FilterGroupModel? FilterGroup = null);

    public sealed record RemoveGroup(Guid Id);

    public sealed record SetGroup(FilterGroupModel FilterGroup);

    public sealed record OpenMenu;
}
