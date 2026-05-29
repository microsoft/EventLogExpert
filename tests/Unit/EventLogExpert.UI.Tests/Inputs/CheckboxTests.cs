// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.UI.Inputs;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Tests.Inputs;

public sealed class CheckboxTests : BunitContext
{
    [Fact]
    public async Task Change_InvokesValueChangedWithNewValue()
    {
        bool? observedValue = null;
        var component = Render<Checkbox>(parameters => parameters
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

    [Fact]
    public async Task Change_WhenDisabled_DoesNotInvokeValueChanged()
    {
        bool invoked = false;
        var component = Render<Checkbox>(parameters => parameters
            .Add(p => p.Value, false)
            .Add(p => p.Disabled, true)
            .Add(p => p.ValueChanged, _ =>
            {
                invoked = true;
                return Task.CompletedTask;
            }));

        var input = component.Find("input[type='checkbox']");
        await input.ChangeAsync(new ChangeEventArgs { Value = true });

        Assert.False(invoked);
    }

    [Fact]
    public void Render_AriaDescribedBy_AppliedToInput()
    {
        var component = Render<Checkbox>(parameters => parameters
            .Add(p => p.Value, false)
            .Add(p => p.AriaDescribedBy, "help-text-id"));

        var input = component.Find("input[type='checkbox']");
        Assert.Equal("help-text-id", input.GetAttribute("aria-describedby"));
    }

    [Fact]
    public void Render_AriaLabel_AppliedToInput()
    {
        var component = Render<Checkbox>(parameters => parameters
            .Add(p => p.Value, false)
            .Add(p => p.AriaLabel, "Select database foo.db"));

        var input = component.Find("input[type='checkbox']");
        Assert.Equal("Select database foo.db", input.GetAttribute("aria-label"));
    }

    [Fact]
    public void Render_AriaLabelledBy_AppliedToInput()
    {
        var component = Render<Checkbox>(parameters => parameters
            .Add(p => p.Value, false)
            .Add(p => p.AriaLabelledBy, "external-label-id"));

        var input = component.Find("input[type='checkbox']");
        Assert.Equal("external-label-id", input.GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void Render_AriaLabelledByAndAriaLabel_SuppressesAriaLabelPerWaiAriaPrecedence()
    {
        var component = Render<Checkbox>(parameters => parameters
            .Add(p => p.Value, false)
            .Add(p => p.AriaLabel, "Should be suppressed")
            .Add(p => p.AriaLabelledBy, "external-label-id"));

        var input = component.Find("input[type='checkbox']");
        Assert.False(input.HasAttribute("aria-label"));
        Assert.Equal("external-label-id", input.GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void Render_CssClass_AppendedToLabel()
    {
        var component = Render<Checkbox>(parameters => parameters
            .Add(p => p.Value, false)
            .Add(p => p.CssClass, "extra-class"));

        var label = component.Find("label");
        Assert.Contains("checkbox", label.ClassList);
        Assert.Contains("extra-class", label.ClassList);
    }

    [Fact]
    public void Render_DisabledFalse_InputNotDisabled()
    {
        var component = Render<Checkbox>(parameters => parameters
            .Add(p => p.Value, false)
            .Add(p => p.Disabled, false));

        var input = component.Find("input[type='checkbox']");
        Assert.False(input.HasAttribute("disabled"));
    }

    [Fact]
    public void Render_DisabledTrue_InputDisabled()
    {
        var component = Render<Checkbox>(parameters => parameters
            .Add(p => p.Value, false)
            .Add(p => p.Disabled, true));

        var input = component.Find("input[type='checkbox']");
        Assert.True(input.HasAttribute("disabled"));
    }

    [Fact]
    public void Render_TitleOnLabel_AppliedFromAriaLabel()
    {
        var component = Render<Checkbox>(parameters => parameters
            .Add(p => p.Value, false)
            .Add(p => p.AriaLabel, "Tooltip text"));

        var label = component.Find("label.checkbox");
        Assert.Equal("Tooltip text", label.GetAttribute("title"));
    }

    [Fact]
    public void Render_ValueFalse_IconIsSquare()
    {
        var component = Render<Checkbox>(parameters => parameters
            .Add(p => p.Value, false));

        var icon = component.Find(".checkbox-icon i");
        Assert.Contains("bi-square", icon.ClassList);
        Assert.DoesNotContain("bi-check-square-fill", icon.ClassList);
    }

    [Fact]
    public void Render_ValueFalse_InputDoesNotHaveCheckedAttribute()
    {
        var component = Render<Checkbox>(parameters => parameters
            .Add(p => p.Value, false));

        var input = component.Find("input[type='checkbox']");
        Assert.False(input.HasAttribute("checked"));
    }

    [Fact]
    public void Render_ValueTrue_IconIsCheckSquareFill()
    {
        var component = Render<Checkbox>(parameters => parameters
            .Add(p => p.Value, true));

        var icon = component.Find(".checkbox-icon i");
        Assert.Contains("bi-check-square-fill", icon.ClassList);
    }

    [Fact]
    public void Render_ValueTrue_InputHasCheckedAttribute()
    {
        var component = Render<Checkbox>(parameters => parameters
            .Add(p => p.Value, true));

        var input = component.Find("input[type='checkbox']");
        Assert.True(input.HasAttribute("checked"));
    }
}
