// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Shared.Base;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.FilterGroup;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components.Filters;

public sealed partial class FilterGroupRow : EditableFilterRowBase
{
    [Parameter] public FilterGroupId ParentId { get; set; }

    protected override void DispatchRemoveFilter()
    {
        if (Value is not { } savedFilter) { return; }

        Dispatcher.Dispatch(new FilterGroupAction.RemoveFilter(ParentId, savedFilter.Id));
    }

    protected override void DispatchSetFilter(FilterModel filter) =>
        Dispatcher.Dispatch(new FilterGroupAction.SetFilter(ParentId, filter));

    protected override void DispatchToggleExclusion(FilterId id) =>
        Dispatcher.Dispatch(new FilterGroupAction.ToggleFilterExcluded(ParentId, id));
}
