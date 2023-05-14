// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components;

public partial class TimeZoneSelect
{
    [Parameter] public string Value { get; set; } = null!;

    [Parameter] public EventCallback<string> ValueChanged { get; set; }

    private async Task SetValue(ChangeEventArgs args)
    {
        if (string.IsNullOrEmpty(args.Value?.ToString())) { return; }

        Value = args.Value.ToString()!;
        await ValueChanged.InvokeAsync(Value);
    }
}
