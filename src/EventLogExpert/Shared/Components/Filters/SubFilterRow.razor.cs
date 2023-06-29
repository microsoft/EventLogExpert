// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.FilterPane;
using Microsoft.AspNetCore.Components;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components.Filters;

public partial class SubFilterRow
{
    private List<string> _filterItems = new();

    [Parameter] public Guid ParentId { get; set; }

    [Parameter] public SubFilterModel Value { get; set; } = null!;

    [Inject] private IDispatcher Dispatcher { get; set; } = null!;

    private List<string> FilteredItems =>
        _filterItems.Where(x => x.ToLower().Contains(Value.FilterValue.ToLower())).ToList();

    private void RemoveSubFilter() => Dispatcher.Dispatch(new FilterPaneAction.RemoveSubFilter(ParentId, Value.Id));
}
