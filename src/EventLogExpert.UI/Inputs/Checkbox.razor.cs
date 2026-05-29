// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Inputs;

public sealed partial class Checkbox : InputComponent<bool>
{
    [Parameter] public bool Disabled { get; set; }

    private async Task UpdateValue(ChangeEventArgs args)
    {
        if (args.Value is not bool value) { return; }
        if (Disabled) { return; }

        Value = value;
        await ValueChanged.InvokeAsync(Value);
    }
}
