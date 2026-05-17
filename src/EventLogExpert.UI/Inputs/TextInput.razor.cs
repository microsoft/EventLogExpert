// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Base;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Inputs;

public partial class TextInput : InputComponent<string>
{
    private async Task UpdateValue(ChangeEventArgs args)
    {
        Value = args.Value?.ToString() ?? string.Empty;
        await ValueChanged.InvokeAsync(Value);
    }
}
