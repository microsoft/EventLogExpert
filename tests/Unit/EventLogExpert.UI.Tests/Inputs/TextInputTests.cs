// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.UI.Inputs;

namespace EventLogExpert.UI.Tests.Inputs;

public sealed class TextInputTests : BunitContext
{
    [Fact]
    public void Render_AriaDescribedBy_AppliedToInput()
    {
        var component = Render<TextInput>(parameters => parameters
            .Add(p => p.Value, string.Empty)
            .Add(p => p.AriaDescribedBy, "help-text-id"));

        var input = component.Find("input[type='text']");
        Assert.Equal("help-text-id", input.GetAttribute("aria-describedby"));
    }

    [Fact]
    public void Render_AriaLabel_AppliedToInput()
    {
        var component = Render<TextInput>(parameters => parameters
            .Add(p => p.Value, string.Empty)
            .Add(p => p.AriaLabel, "Filter value"));

        var input = component.Find("input[type='text']");
        Assert.Equal("Filter value", input.GetAttribute("aria-label"));
    }

    [Fact]
    public void Render_AriaLabelledBy_AppliedToInput()
    {
        var component = Render<TextInput>(parameters => parameters
            .Add(p => p.Value, string.Empty)
            .Add(p => p.AriaLabelledBy, "external-label-id"));

        var input = component.Find("input[type='text']");
        Assert.Equal("external-label-id", input.GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void Render_AriaLabelledByAndAriaLabel_SuppressesAriaLabel()
    {
        var component = Render<TextInput>(parameters => parameters
            .Add(p => p.Value, string.Empty)
            .Add(p => p.AriaLabel, "Should be suppressed")
            .Add(p => p.AriaLabelledBy, "external-label-id"));

        var input = component.Find("input[type='text']");
        Assert.False(input.HasAttribute("aria-label"));
        Assert.Equal("external-label-id", input.GetAttribute("aria-labelledby"));
    }
}
