// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Components.Filters.Base;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.FilterPane;

namespace EventLogExpert.Components.Filters;

public sealed partial class AdvancedFilterRow : EditableFilterRowBase
{
    protected override void DispatchRemoveFilter()
    {
        if (Value is not { } savedFilter) { return; }

        Dispatcher.Dispatch(new RemoveFilterAction(savedFilter.Id));
    }

    protected override void DispatchSetFilter(FilterModel filter) =>
        Dispatcher.Dispatch(new SetFilterAction(filter));

    protected override void DispatchToggleEnabled(FilterId id) =>
        Dispatcher.Dispatch(new ToggleFilterEnabledAction(id));

    protected override void DispatchToggleExclusion(FilterId id) =>
        Dispatcher.Dispatch(new ToggleFilterExcludedAction(id));
}
