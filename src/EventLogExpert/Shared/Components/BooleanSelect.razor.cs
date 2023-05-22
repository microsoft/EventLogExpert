// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components;

public partial class BooleanSelect
{
    [Parameter] public string Id { get; set; } = Guid.NewGuid().ToString();

    [Parameter] public bool Value { get; set; }

    [Parameter] public EventCallback<bool> ValueChanged { get; set; }

    private async Task SetValue(ChangeEventArgs args)
    {
        if (!bool.TryParse(args.Value?.ToString(), out bool value)) { return; }

        Value = value;
        await ValueChanged.InvokeAsync(Value);
    }
}
