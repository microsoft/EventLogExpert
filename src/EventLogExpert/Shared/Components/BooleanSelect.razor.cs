// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Shared.Base;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components;

public sealed partial class BooleanSelect : BaseComponent<bool>
{
    [Parameter] public string AriaLabel { get; set; } = string.Empty;

    [Parameter] public string DisabledString { get; set; } = "Disabled";

    [Parameter] public string EnabledString { get; set; } = "Enabled";

    [Parameter] public string Id { get; set; } = Guid.NewGuid().ToString();

    [Parameter] public bool IsSingleColor { get; set; }

    private async Task UpdateValue(ChangeEventArgs args)
    {
        if (!bool.TryParse(args.Value?.ToString(), out bool value)) { return; }

        Value = value;
        await ValueChanged.InvokeAsync(Value);
    }
}
