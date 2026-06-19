// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace EventLogExpert.UI.Inputs;

public abstract class ButtonBase : ComponentBase
{
    private ElementReference _element;

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter] public string CssClass { get; set; } = string.Empty;

    [Parameter] public bool Disabled { get; set; }

    public ElementReference Element => _element;

    [Parameter] public string? IconClass { get; set; }

    [Parameter] public bool IconOnly { get; set; }

    [Parameter] public EventCallback OnClick { get; set; }

    [Parameter] public string Type { get; set; } = "button";

    protected abstract string? VariantClass { get; }

    public ValueTask FocusAsync(bool preventScroll = false) => _element.FocusAsync(preventScroll);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "button");

        if (AdditionalAttributes is not null)
        {
            builder.AddMultipleAttributes(1, AdditionalAttributes);
        }

        builder.AddAttribute(2, "type", Type);
        builder.AddAttribute(3, "class", BuildCssClass());
        builder.AddAttribute(4, "disabled", Disabled);
        builder.AddAttribute(5, "onclick", OnClick);
        builder.AddElementReferenceCapture(6, capturedRef => _element = capturedRef);

        if (!string.IsNullOrWhiteSpace(IconClass))
        {
            builder.OpenElement(7, "i");
            builder.AddAttribute(8, "aria-hidden", "true");
            builder.AddAttribute(9, "class", IconClass);
            builder.CloseElement();

            if (ChildContent is not null)
            {
                builder.AddContent(10, " ");
            }
        }

        builder.AddContent(11, ChildContent);
        builder.CloseElement();
    }

    private string BuildCssClass()
    {
        var classes = new List<string>(4) { "button" };

        if (!string.IsNullOrWhiteSpace(VariantClass)) { classes.Add(VariantClass); }

        if (IconOnly) { classes.Add("icon-button"); }

        if (!string.IsNullOrWhiteSpace(CssClass)) { classes.Add(CssClass); }

        return string.Join(' ', classes);
    }
}
