// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Shared.Base;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.FilterPane;

namespace EventLogExpert.Shared.Components.Filters;

public sealed partial class AdvancedFilterRow : EditableFilterRowBase
{
    protected override void DispatchRemoveFilter()
    {
        if (Value is not { } savedFilter) { return; }

        Dispatcher.Dispatch(new FilterPaneAction.RemoveFilter(savedFilter.Id));
    }

    protected override void DispatchSetFilter(FilterModel filter) =>
        Dispatcher.Dispatch(new FilterPaneAction.SetFilter(filter));

    protected override void DispatchToggleEnabled(FilterId id) =>
        Dispatcher.Dispatch(new FilterPaneAction.ToggleFilterEnabled(id));

    protected override void DispatchToggleExclusion(FilterId id) =>
        Dispatcher.Dispatch(new FilterPaneAction.ToggleFilterExcluded(id));
}
