// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Shared.Base;
using EventLogExpert.UI.Models;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components.Filters;

public sealed partial class SubFilterRow : BaseFilterRow
{
    [Parameter] public FilterEditorModel Value { get; set; } = null!;

    [Parameter] public EventCallback<FilterId> OnRemove { get; set; }

    protected override FilterData CurrentData => Value.Data;

    private async Task RemoveSubFilter() => await OnRemove.InvokeAsync(Value.Id);
}
