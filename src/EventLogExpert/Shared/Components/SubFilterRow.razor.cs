// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Helpers;
using EventLogExpert.Library.Models;
using EventLogExpert.Store.FilterPane;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components;

public partial class SubFilterRow
{
    [Parameter] public Guid ParentId { get; set; }

    [Parameter] public FilterType FilterType { get; set; }

    [Parameter] public SubFilterModel Value { get; set; } = null!;

    private void RemoveSubFilter() => Dispatcher.Dispatch(new FilterPaneAction.RemoveSubFilter(ParentId, Value));
}
