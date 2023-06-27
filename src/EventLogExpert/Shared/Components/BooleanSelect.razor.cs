// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Shared.Base;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components;

public partial class BooleanSelect : BaseComponent<bool>
{
    [Parameter] public string Id { get; set; } = Guid.NewGuid().ToString();

    private async Task UpdateValue(ChangeEventArgs args)
    {
        if (!bool.TryParse(args.Value?.ToString(), out bool value)) { return; }

        Value = value;
        await ValueChanged.InvokeAsync(Value);
    }
}
