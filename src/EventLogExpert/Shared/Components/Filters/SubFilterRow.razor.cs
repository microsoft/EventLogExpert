// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Shared.Base;
using EventLogExpert.UI.Models;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components.Filters;

public sealed partial class SubFilterRow : FilterRowBase<SubFilterDraft>
{
    [Parameter] public EventCallback<FilterId> OnRemove { get; set; }

    private async Task RemoveSubFilter() => await OnRemove.InvokeAsync(Value.Id);
}