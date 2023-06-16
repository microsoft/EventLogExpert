// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Store.FilterPane;
using EventLogExpert.UI.Models;
using Microsoft.AspNetCore.Components;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components;

public partial class SubFilterRow
{
    [Inject] private IDispatcher Dispatcher { get; set; } = null!;

    [Parameter] public Guid ParentId { get; set; }

    [Parameter] public SubFilterModel Value { get; set; } = null!;

    private void RemoveSubFilter() => Dispatcher.Dispatch(new FilterPaneAction.RemoveSubFilter(ParentId, Value.Id));
}
