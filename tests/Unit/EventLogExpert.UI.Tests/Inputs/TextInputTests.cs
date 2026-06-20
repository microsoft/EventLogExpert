// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.UI.Inputs;

namespace EventLogExpert.UI.Tests.Inputs;

public sealed class TextInputTests : BunitContext
{
    [Fact]
    public void Default_RaisesValueChangedOnChangeEvent()
    {
        string? captured = null;

        var component = Render<TextInput>(parameters => parameters
            .Add(p => p.Value, string.Empty)
            .Add(p => p.ValueChanged, value => { captured = value; }));

        component.Find("input").Change("committed");

        Assert.Equal("committed", captured);
    }

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
    public void Render_AriaInvalidFalse_OmitsAttribute()
    {
        var component = Render<TextInput>(parameters => parameters
            .Add(p => p.Value, string.Empty)
            .Add(p => p.AriaInvalid, false));

        var input = component.Find("input[type='text']");
        Assert.False(input.HasAttribute("aria-invalid"));
    }

    [Fact]
    public void Render_AriaInvalidTrue_AppliedToInput()
    {
        var component = Render<TextInput>(parameters => parameters
            .Add(p => p.Value, string.Empty)
            .Add(p => p.AriaInvalid, true));

        var input = component.Find("input[type='text']");
        Assert.Equal("true", input.GetAttribute("aria-invalid"));
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

    [Fact]
    public void Render_Id_AppliedToInput()
    {
        var component = Render<TextInput>(parameters => parameters
            .Add(p => p.Value, string.Empty)
            .Add(p => p.Id, "prompt-input-id"));

        var input = component.Find("input[type='text']");
        Assert.Equal("prompt-input-id", input.GetAttribute("id"));
    }

    [Fact]
    public void UpdateOnInput_RaisesValueChangedOnInputEvent()
    {
        string? captured = null;

        var component = Render<TextInput>(parameters => parameters
            .Add(p => p.Value, string.Empty)
            .Add(p => p.UpdateOnInput, true)
            .Add(p => p.ValueChanged, value => { captured = value; }));

        component.Find("input").Input("live");

        Assert.Equal("live", captured);
    }
}
