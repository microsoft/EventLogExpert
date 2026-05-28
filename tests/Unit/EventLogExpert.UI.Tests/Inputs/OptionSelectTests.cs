// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.UI.Inputs;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Tests.Inputs;

public sealed class OptionSelectTests : BunitContext
{
    [Fact]
    public void Render_AriaDescribedBy_AppliedToRadiogroup()
    {
        var component = Render<OptionSelect>(parameters => parameters
            .Add(p => p.Value, false)
            .Add(p => p.AriaDescribedBy, "pending-status-id"));

        var group = component.Find(".option-select");
        Assert.Equal("pending-status-id", group.GetAttribute("aria-describedby"));
    }

    [Fact]
    public void Render_AriaLabelledBy_AppliedToRadiogroup()
    {
        var component = Render<OptionSelect>(parameters => parameters
            .Add(p => p.Value, false)
            .Add(p => p.AriaLabelledBy, "filename-button-id"));

        var group = component.Find(".option-select");
        Assert.Equal("filename-button-id", group.GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void Render_AriaLabelledByAndAriaLabel_SuppressesOuterAriaLabelAndRadioPrefix()
    {
        var component = Render<OptionSelect>(parameters => parameters
            .Add(p => p.Value, false)
            .Add(p => p.AriaLabel, "Should be suppressed")
            .Add(p => p.AriaLabelledBy, "filename-button-id")
            .Add(p => p.DisabledString, "AND")
            .Add(p => p.EnabledString, "OR"));

        var group = component.Find(".option-select");
        Assert.False(group.HasAttribute("aria-label"));
        Assert.Equal("filename-button-id", group.GetAttribute("aria-labelledby"));

        var radios = component.FindAll(".option-select input[type='radio']");
        Assert.Equal("AND", radios[0].GetAttribute("aria-label"));
        Assert.Equal("OR", radios[1].GetAttribute("aria-label"));
    }

    [Fact]
    public void Render_AriaLabelOnly_PrependsOuterLabelOnEachRadio()
    {
        var component = Render<OptionSelect>(parameters => parameters
            .Add(p => p.Value, false)
            .Add(p => p.AriaLabel, "Should Compare")
            .Add(p => p.DisabledString, "AND")
            .Add(p => p.EnabledString, "OR"));

        var radios = component.FindAll(".option-select input[type='radio']");
        Assert.Equal("Should Compare AND", radios[0].GetAttribute("aria-label"));
        Assert.Equal("Should Compare OR", radios[1].GetAttribute("aria-label"));
    }

    [Fact]
    public void Render_CustomLabels_AreApplied()
    {
        var component = Render<OptionSelect>(parameters => parameters
            .Add(p => p.Value, false)
            .Add(p => p.DisabledString, "AND")
            .Add(p => p.EnabledString, "OR"));

        var labels = component.FindAll(".option-select label");
        Assert.Equal("AND", labels[0].TextContent);
        Assert.Equal("OR", labels[1].TextContent);
    }

    [Fact]
    public void Render_DefaultId_IsUnique()
    {
        var first = Render<OptionSelect>(parameters => parameters.Add(p => p.Value, false));
        var second = Render<OptionSelect>(parameters => parameters.Add(p => p.Value, false));

        var firstName = first.Find(".option-select input[type='radio']").GetAttribute("name");
        var secondName = second.Find(".option-select input[type='radio']").GetAttribute("name");

        Assert.False(string.IsNullOrEmpty(firstName));
        Assert.False(string.IsNullOrEmpty(secondName));
        Assert.NotEqual(firstName, secondName);
    }

    [Fact]
    public void Render_DefaultLabels_AreDisabledAndEnabled()
    {
        var component = Render<OptionSelect>(parameters => parameters
            .Add(p => p.Value, false));

        var labels = component.FindAll(".option-select label");
        Assert.Equal("Disabled", labels[0].TextContent);
        Assert.Equal("Enabled", labels[1].TextContent);
    }

    [Fact]
    public void Render_DefaultState_HasRadiogroupRole()
    {
        var component = Render<OptionSelect>(parameters => parameters
            .Add(p => p.Value, false));

        var group = component.Find(".option-select");
        Assert.Equal("radiogroup", group.GetAttribute("role"));
    }

    [Fact]
    public void Render_Disabled_RendersDisabledOnBothInputs()
    {
        var component = Render<OptionSelect>(parameters => parameters
            .Add(p => p.Value, false)
            .Add(p => p.Disabled, true));

        var radios = component.FindAll(".option-select input[type='radio']");
        Assert.True(radios[0].HasAttribute("disabled"));
        Assert.True(radios[1].HasAttribute("disabled"));
    }

    [Fact]
    public void Render_RendersTwoRadioInputs()
    {
        var component = Render<OptionSelect>(parameters => parameters
            .Add(p => p.Value, false));

        var radios = component.FindAll(".option-select input[type='radio']");
        Assert.Equal(2, radios.Count);
    }

    [Fact]
    public void Render_UseStatusColorsFalse_HasFalseDataAttribute()
    {
        var component = Render<OptionSelect>(parameters => parameters
            .Add(p => p.Value, false)
            .Add(p => p.UseStatusColors, false));

        var labels = component.FindAll(".option-select label");
        Assert.Equal("false", labels[0].GetAttribute("data-use-status-colors"));
        Assert.Equal("false", labels[1].GetAttribute("data-use-status-colors"));
    }

    [Fact]
    public void Render_UseStatusColorsTrue_AppliesDataAttributeToLabels()
    {
        var component = Render<OptionSelect>(parameters => parameters
            .Add(p => p.Value, true)
            .Add(p => p.UseStatusColors, true));

        var labels = component.FindAll(".option-select label");
        Assert.Equal("true", labels[0].GetAttribute("data-use-status-colors"));
        Assert.Equal("true", labels[1].GetAttribute("data-use-status-colors"));
    }

    [Fact]
    public void Render_ValueFalse_ChecksFalseRadio()
    {
        var component = Render<OptionSelect>(parameters => parameters
            .Add(p => p.Value, false));

        var radios = component.FindAll(".option-select input[type='radio']");
        Assert.True(radios[0].HasAttribute("checked"));
        Assert.False(radios[1].HasAttribute("checked"));
    }

    [Fact]
    public void Render_ValueTrue_ChecksTrueRadio()
    {
        var component = Render<OptionSelect>(parameters => parameters
            .Add(p => p.Value, true));

        var radios = component.FindAll(".option-select input[type='radio']");
        Assert.False(radios[0].HasAttribute("checked"));
        Assert.True(radios[1].HasAttribute("checked"));
    }

    [Fact]
    public async Task TogglingTrueRadio_InvokesValueChanged()
    {
        bool? observedValue = null;
        var component = Render<OptionSelect>(parameters => parameters
            .Add(p => p.Value, false)
            .Add(p => p.ValueChanged, v =>
            {
                observedValue = v;
                return Task.CompletedTask;
            }));

        var radios = component.FindAll(".option-select input[type='radio']");
        await radios[1].ChangeAsync(new ChangeEventArgs { Value = "true" });

        Assert.True(observedValue);
    }
}
