// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Components.Filters.Base;
using EventLogExpert.UI.Filter;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Components.Filters;

public sealed partial class SubFilterRow : FilterRowBase<SubFilterDraft>
{
    [Parameter] public EventCallback<FilterId> OnRemove { get; set; }

    private async Task RemoveSubFilter() => await OnRemove.InvokeAsync(Value.Id);
}
