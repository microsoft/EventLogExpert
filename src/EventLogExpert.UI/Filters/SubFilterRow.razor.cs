// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Filters.Base;
using EventLogExpert.Filtering.Drafts;
using EventLogExpert.Filtering.Persistence;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Filters;

public sealed partial class SubFilterRow : FilterRowBase<SubFilterDraft>
{
    [Parameter] public EventCallback<FilterId> OnRemove { get; set; }

    private async Task RemoveSubFilter() => await OnRemove.InvokeAsync(Value.Id);
}
