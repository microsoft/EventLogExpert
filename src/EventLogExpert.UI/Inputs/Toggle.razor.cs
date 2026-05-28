// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Inputs;

public sealed partial class Toggle : InputComponent<bool>
{
    [Parameter] public bool Disabled { get; set; }

    [Parameter] public string Id { get; set; } = Guid.NewGuid().ToString();

    private async Task UpdateValue(ChangeEventArgs args)
    {
        if (args.Value is not bool value) { return; }

        Value = value;
        await ValueChanged.InvokeAsync(Value);
    }
}
