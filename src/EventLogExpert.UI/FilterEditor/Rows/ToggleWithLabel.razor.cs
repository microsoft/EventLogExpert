// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Common;
using EventLogExpert.UI.Inputs;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.FilterEditor.Rows;

/// <summary>
///     Compact toggle paired with a visible text label (e.g., "Exclude"). Reuses <see cref="Toggle" /> internally so
///     keyboard interaction (Space to flip), focus-visible appearance, and forced-colors rendering match the rest of the
///     app. The visible label is wired into the inner switch via <c>aria-labelledby</c> chaining so SR users hear both the
///     upstream filter context and the visible label.
/// </summary>
public sealed partial class ToggleWithLabel : InputComponent<bool>
{
    private readonly string _textLabelId = ComponentId.NewUnique("toggle-with-label-text").Value;

    [Parameter] public bool Disabled { get; set; }

    [Parameter] public string Id { get; set; } = ComponentId.NewUnique().Value;

    /// <summary>
    ///     Visible text rendered next to the toggle (e.g. "Exclude"). When null/empty, behaves like a bare
    ///     <see cref="Toggle" />.
    /// </summary>
    [Parameter] public string? Label { get; set; }

    private string? EffectiveAriaLabelledBy => (string.IsNullOrEmpty(Label), string.IsNullOrEmpty(AriaLabelledBy)) switch
    {
        (true, _) => AriaLabelledBy,
        (false, true) => _textLabelId,
        (false, false) => $"{AriaLabelledBy} {_textLabelId}"
    };
}
