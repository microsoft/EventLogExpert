// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

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

    [Parameter] public EventCallback<MouseEventArgs> OnClick { get; set; }

    [Parameter] public bool StopPropagation { get; set; }

    [Parameter] public string? Type { get; set; } = "button";

    public ValueTask FocusAsync(bool preventScroll = false) => _element.FocusAsync(preventScroll);

    protected abstract string BuildCssClass();

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "button");

        if (AdditionalAttributes is not null)
        {
            builder.AddMultipleAttributes(1, AdditionalAttributes);
        }

        builder.AddAttribute(2, "type", string.IsNullOrWhiteSpace(Type) ? "button" : Type);

        var cssClass = BuildCssClass();

        if (!string.IsNullOrWhiteSpace(cssClass))
        {
            builder.AddAttribute(3, "class", cssClass);
        }

        builder.AddAttribute(4, "disabled", Disabled);
        builder.AddAttribute(5, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, HandleClickAsync));

        if (StopPropagation)
        {
            builder.AddEventStopPropagationAttribute(6, "onclick", true);
        }

        builder.AddElementReferenceCapture(7, capturedRef => _element = capturedRef);

        if (!string.IsNullOrWhiteSpace(IconClass))
        {
            builder.OpenElement(8, "i");
            builder.AddAttribute(9, "aria-hidden", "true");
            builder.AddAttribute(10, "class", IconClass);
            builder.CloseElement();

            if (ChildContent is not null)
            {
                builder.AddContent(11, " ");
            }
        }

        builder.AddContent(12, ChildContent);
        builder.CloseElement();
    }

    private Task HandleClickAsync(MouseEventArgs args) => Disabled ? Task.CompletedTask : OnClick.InvokeAsync(args);
}
