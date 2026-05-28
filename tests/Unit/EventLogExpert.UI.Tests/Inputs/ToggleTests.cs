// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.UI.Inputs;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Tests.Inputs;

public sealed class ToggleTests : BunitContext
{
    [Fact]
    public void Render_AriaDescribedBy_AppliedToInput()
    {
        var component = Render<Toggle>(parameters => parameters
            .Add(p => p.Value, false)
            .Add(p => p.AriaDescribedBy, "help-text-id"));

        var input = component.Find("input[type='checkbox']");
        Assert.Equal("help-text-id", input.GetAttribute("aria-describedby"));
    }

    [Fact]
    public void Render_AriaLabel_AppliedToInput()
    {
        var component = Render<Toggle>(parameters => parameters
            .Add(p => p.Value, false)
            .Add(p => p.AriaLabel, "Auto-expand details on selection"));

        var input = component.Find("input[type='checkbox']");
        Assert.Equal("Auto-expand details on selection", input.GetAttribute("aria-label"));
    }

    [Fact]
    public void Render_AriaLabelledBy_AppliedToInput()
    {
        var component = Render<Toggle>(parameters => parameters
            .Add(p => p.Value, false)
            .Add(p => p.AriaLabelledBy, "external-label-id"));

        var input = component.Find("input[type='checkbox']");
        Assert.Equal("external-label-id", input.GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void Render_AriaLabelledByAndAriaLabel_SuppressesAriaLabelPerWaiAriaPrecedence()
    {
        var component = Render<Toggle>(parameters => parameters
            .Add(p => p.Value, false)
            .Add(p => p.AriaLabel, "Should be suppressed")
            .Add(p => p.AriaLabelledBy, "external-label-id"));

        var input = component.Find("input[type='checkbox']");
        Assert.False(input.HasAttribute("aria-label"));
        Assert.Equal("external-label-id", input.GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void Render_DefaultId_IsUnique()
    {
        var first = Render<Toggle>(parameters => parameters.Add(p => p.Value, false));
        var second = Render<Toggle>(parameters => parameters.Add(p => p.Value, false));

        var firstId = first.Find("input[type='checkbox']").GetAttribute("id");
        var secondId = second.Find("input[type='checkbox']").GetAttribute("id");

        Assert.False(string.IsNullOrEmpty(firstId));
        Assert.False(string.IsNullOrEmpty(secondId));
        Assert.NotEqual(firstId, secondId);
    }

    [Fact]
    public void Render_DefaultState_HasRoleSwitch()
    {
        var component = Render<Toggle>(parameters => parameters
            .Add(p => p.Value, false));

        var input = component.Find("input[type='checkbox']");
        Assert.Equal("switch", input.GetAttribute("role"));
    }

    [Fact]
    public void Render_Disabled_RendersDisabledAttribute()
    {
        var component = Render<Toggle>(parameters => parameters
            .Add(p => p.Value, false)
            .Add(p => p.Disabled, true));

        var input = component.Find("input[type='checkbox']");
        Assert.True(input.HasAttribute("disabled"));
    }

    [Fact]
    public void Render_HasToggleTrackAndKnob()
    {
        var component = Render<Toggle>(parameters => parameters
            .Add(p => p.Value, false));

        Assert.Single(component.FindAll(".toggle-track"));
        Assert.Single(component.FindAll(".toggle-knob"));
    }

    [Fact]
    public void Render_ValueFalse_RendersUncheckedInput()
    {
        var component = Render<Toggle>(parameters => parameters
            .Add(p => p.Value, false));

        var input = component.Find("input[type='checkbox']");
        Assert.False(input.HasAttribute("checked"));
    }

    [Fact]
    public void Render_ValueTrue_RendersCheckedInput()
    {
        var component = Render<Toggle>(parameters => parameters
            .Add(p => p.Value, true));

        var input = component.Find("input[type='checkbox']");
        Assert.True(input.HasAttribute("checked"));
    }

    [Fact]
    public async Task Toggle_Change_InvokesValueChanged()
    {
        bool? observedValue = null;
        var component = Render<Toggle>(parameters => parameters
            .Add(p => p.Value, false)
            .Add(p => p.ValueChanged, v =>
            {
                observedValue = v;
                return Task.CompletedTask;
            }));

        var input = component.Find("input[type='checkbox']");
        await input.ChangeAsync(new ChangeEventArgs { Value = true });

        Assert.True(observedValue);
    }
}
