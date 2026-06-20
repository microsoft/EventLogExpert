// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Inputs;

public abstract class StyledButtonBase : ButtonBase
{
    [Parameter] public bool IconOnly { get; set; }

    protected abstract string? VariantClass { get; }

    protected override string BuildCssClass()
    {
        var classes = new List<string>(4) { "button" };

        if (!string.IsNullOrWhiteSpace(VariantClass)) { classes.Add(VariantClass); }

        if (IconOnly) { classes.Add("icon-button"); }

        if (!string.IsNullOrWhiteSpace(CssClass)) { classes.Add(CssClass); }

        return string.Join(' ', classes);
    }
}
