// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.FilterPane;
using Microsoft.AspNetCore.Components;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components.Filters;

public partial class FilterCacheRow
{
    private bool _isEditing;
    private FilterCacheModel _filter = null!;

    [Parameter] public FilterCacheModel Value { get; set; } = null!;

    [Inject] private IDispatcher Dispatcher { get; set; } = null!;

    private void EditFilter()
    {
        _isEditing = true;
        _filter = Value;
    }

    private void RemoveFilter() => Dispatcher.Dispatch(new FilterPaneAction.RemoveCachedFilter(Value));

    private void SaveFilter()
    {
        _isEditing = false;

        if (ReferenceEquals(Value, _filter)) { return; }

        Dispatcher.Dispatch(new FilterPaneAction.RemoveCachedFilter(_filter));
        Dispatcher.Dispatch(new FilterPaneAction.AddCachedFilter(Value));
    }

    private void ToggleFilter() => Dispatcher.Dispatch(new FilterPaneAction.ToggleCachedFilter(Value));
}
