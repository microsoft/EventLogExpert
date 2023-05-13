// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components;

public partial class TimeZoneSelect
{
    [Parameter] public int Value { get; set; }

    [Parameter] public EventCallback<int> ValueChanged { get; set; }

    private async Task SetValue(ChangeEventArgs args)
    {
        if (int.TryParse(args.Value?.ToString(), out int value))
        {
            Value = value;
            await ValueChanged.InvokeAsync(Value);
        }
    }
}
