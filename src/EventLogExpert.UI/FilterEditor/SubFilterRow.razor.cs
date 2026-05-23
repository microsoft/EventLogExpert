// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Drafts;
using EventLogExpert.Filtering.Persistence;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.FilterEditor;

public sealed partial class SubFilterRow : FilterRowBase<SubFilterDraft>
{
    [Parameter] public EventCallback<FilterId> OnRemove { get; set; }

    private async Task RemoveSubFilter() => await OnRemove.InvokeAsync(Value.Id);
}
