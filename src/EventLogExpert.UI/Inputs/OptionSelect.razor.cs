// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Common;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Inputs;

public sealed partial class OptionSelect : InputComponent<bool>
{
    [Parameter] public bool Disabled { get; set; }

    [Parameter] public string DisabledString { get; set; } = "Disabled";

    [Parameter] public string EnabledString { get; set; } = "Enabled";

    [Parameter] public string Id { get; set; } = ComponentId.NewUnique().Value;

    [Parameter] public bool UseStatusColors { get; set; }

    private string OptionAriaLabel(string optionText) =>
        string.IsNullOrWhiteSpace(EffectiveAriaLabel) ? optionText : $"{EffectiveAriaLabel} {optionText}";

    private async Task UpdateValue(ChangeEventArgs args)
    {
        if (!bool.TryParse(args.Value?.ToString(), out bool value)) { return; }

        Value = value;
        await ValueChanged.InvokeAsync(Value);
    }
}
