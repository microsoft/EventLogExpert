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
    public void Input_WhenTypedTextDoesNotParse_ClearsValueInsteadOfKeepingStaleOne()
    {
        // Regression: an unparseable entry in an editable numeric combobox must clear the bound value rather than
        // silently retaining the last valid one, so a consumer cannot submit a stale value the input no longer shows.
        int? bound = 7;
        var component = Render<ValueSelect<int?>>(parameters => parameters
            .Add(p => p.IsInput, true)
            .Add(p => p.Value, bound)
            .Add(p => p.ValueChanged, value => bound = value));

        component.Find("input[role='combobox']").Input("not-a-number");

        Assert.Null(bound);
    }

    [Fact]
    public void Input_WhenTypedTextParses_RaisesValueChangedWithParsedValue()
    {
        int? bound = 7;
        var component = Render<ValueSelect<int?>>(parameters => parameters
            .Add(p => p.IsInput, true)
            .Add(p => p.Value, bound)
            .Add(p => p.ValueChanged, value => bound = value));

        component.Find("input[role='combobox']").Input("12");

        Assert.Equal(12, bound);
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
