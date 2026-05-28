// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.UI.Inputs;

namespace EventLogExpert.UI.Tests.Inputs;

public sealed class ValueSelectTests : BunitContext
{
    public ValueSelectTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Render_AriaDescribedBy_AppliedToCombobox()
    {
        var component = Render<ValueSelect<string>>(parameters => parameters
            .Add(p => p.AriaDescribedBy, "help-text-id"));

        var combobox = component.Find("input[role='combobox']");
        Assert.Equal("help-text-id", combobox.GetAttribute("aria-describedby"));
    }

    [Fact]
    public void Render_AriaLabel_AppliedToCombobox()
    {
        var component = Render<ValueSelect<string>>(parameters => parameters
            .Add(p => p.AriaLabel, "Highlight Color"));

        var combobox = component.Find("input[role='combobox']");
        Assert.Equal("Highlight Color", combobox.GetAttribute("aria-label"));
    }

    [Fact]
    public void Render_AriaLabelledBy_AppliedToCombobox()
    {
        var component = Render<ValueSelect<string>>(parameters => parameters
            .Add(p => p.AriaLabelledBy, "external-label-id"));

        var combobox = component.Find("input[role='combobox']");
        Assert.Equal("external-label-id", combobox.GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void Render_AriaLabelledByAndAriaLabel_SuppressesAriaLabelPerWaiAriaPrecedence()
    {
        var component = Render<ValueSelect<string>>(parameters => parameters
            .Add(p => p.AriaLabel, "Should be suppressed")
            .Add(p => p.AriaLabelledBy, "external-label-id"));

        var combobox = component.Find("input[role='combobox']");
        Assert.False(combobox.HasAttribute("aria-label"));
        Assert.Equal("external-label-id", combobox.GetAttribute("aria-labelledby"));
    }
}
