// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.UI.Inputs;
using Microsoft.AspNetCore.Components.Web;

namespace EventLogExpert.UI.Tests.Inputs;

public sealed class ButtonTests : BunitContext
{
    [Fact]
    public async Task Click_InvokesOnClick()
    {
        bool invoked = false;
        var component = Render<Button>(parameters => parameters
            .Add(p => p.OnClick, () => invoked = true));

        await component.Find("button").ClickAsync(new MouseEventArgs());

        Assert.True(invoked);
    }

    [Fact]
    public async Task Click_InvokesOnClickWithMouseEventArgs()
    {
        MouseEventArgs? received = null;
        var component = Render<Button>(parameters => parameters
            .Add(p => p.OnClick, e => received = e));

        await component.Find("button").ClickAsync(new MouseEventArgs { Button = 1, CtrlKey = true });

        Assert.NotNull(received);
        Assert.Equal(1, received!.Button);
        Assert.True(received.CtrlKey);
    }

    [Fact]
    public async Task Click_WhenDisabled_DoesNotInvokeOnClick()
    {
        bool invoked = false;
        var component = Render<Button>(parameters => parameters
            .Add(p => p.Disabled, true)
            .Add(p => p.OnClick, () => invoked = true));

        await component.Find("button").ClickAsync(new MouseEventArgs());

        Assert.False(invoked);
    }

    [Fact]
    public async Task KeyDown_InvokesOnKeyDown()
    {
        bool invoked = false;
        var component = Render<Button>(parameters => parameters
            .Add(p => p.OnKeyDown, () => invoked = true));

        await component.Find("button").KeyDownAsync(new KeyboardEventArgs());

        Assert.True(invoked);
    }

    [Fact]
    public async Task KeyDown_WhenDisabled_DoesNotInvokeOnKeyDown()
    {
        bool invoked = false;
        var component = Render<Button>(parameters => parameters
            .Add(p => p.Disabled, true)
            .Add(p => p.OnKeyDown, () => invoked = true));

        await component.Find("button").KeyDownAsync(new KeyboardEventArgs());

        Assert.False(invoked);
    }

    [Fact]
    public async Task MouseEnter_InvokesOnMouseEnter()
    {
        bool invoked = false;
        var component = Render<Button>(parameters => parameters
            .Add(p => p.OnMouseEnter, () => invoked = true));

        await component.Find("button").MouseEnterAsync(new MouseEventArgs());

        Assert.True(invoked);
    }

    [Fact]
    public void Render_AdditionalAttributes_SplattedToButton()
    {
        var component = Render<Button>(parameters => parameters
            .AddUnmatched("id", "apply-button")
            .AddUnmatched("aria-label", "Apply filter")
            .AddUnmatched("aria-pressed", "true")
            .AddUnmatched("data-state", "active"));

        var button = component.Find("button");
        Assert.Equal("apply-button", button.GetAttribute("id"));
        Assert.Equal("Apply filter", button.GetAttribute("aria-label"));
        Assert.Equal("true", button.GetAttribute("aria-pressed"));
        Assert.Equal("active", button.GetAttribute("data-state"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Render_BlankType_DefaultsToButton(string? type)
    {
        var component = Render<Button>(parameters => parameters
            .Add(p => p.Type, type));

        var button = component.Find("button");
        Assert.Equal("button", button.GetAttribute("type"));
    }

    [Fact]
    public void Render_ChildContent_RenderedInsideButton()
    {
        var component = Render<Button>(parameters => parameters
            .AddChildContent("Apply"));

        var button = component.Find("button");
        Assert.Contains("Apply", button.TextContent);
    }

    [Fact]
    public void Render_CssClass_AppendedAfterBaseAndVariant()
    {
        var component = Render<PrimaryButton>(parameters => parameters
            .Add(p => p.CssClass, "banner-cycle-prev"));

        var button = component.Find("button");
        Assert.Contains("button", button.ClassList);
        Assert.Contains("button-green", button.ClassList);
        Assert.Contains("banner-cycle-prev", button.ClassList);
    }

    [Fact]
    public void Render_DangerButton_HasButtonRedClass()
    {
        var component = Render<DangerButton>();

        var button = component.Find("button");
        Assert.Contains("button", button.ClassList);
        Assert.Contains("button-red", button.ClassList);
    }

    [Fact]
    public void Render_Default_EmitsNativeButtonWithBaseClass()
    {
        var component = Render<Button>();

        var button = component.Find("button");
        Assert.Equal("button", button.GetAttribute("class"));
    }

    [Fact]
    public void Render_Default_TypeIsButton()
    {
        var component = Render<Button>();

        var button = component.Find("button");
        Assert.Equal("button", button.GetAttribute("type"));
    }

    [Fact]
    public void Render_DisabledFalse_ButtonNotDisabled()
    {
        var component = Render<Button>(parameters => parameters
            .Add(p => p.Disabled, false));

        var button = component.Find("button");
        Assert.False(button.HasAttribute("disabled"));
    }

    [Fact]
    public void Render_DisabledTrue_ButtonDisabled()
    {
        var component = Render<Button>(parameters => parameters
            .Add(p => p.Disabled, true));

        var button = component.Find("button");
        Assert.True(button.HasAttribute("disabled"));
    }

    [Fact]
    public void Render_IconAndChildContent_BothRenderedInOrder()
    {
        var component = Render<PrimaryButton>(parameters => parameters
            .Add(p => p.IconClass, "bi bi-check-circle")
            .AddChildContent("Apply"));

        var button = component.Find("button");
        Assert.NotNull(button.QuerySelector("i.bi-check-circle"));
        Assert.Contains("Apply", button.TextContent);
    }

    [Fact]
    public void Render_IconAndChildContent_SeparatedBySpace()
    {
        var component = Render<PrimaryButton>(parameters => parameters
            .Add(p => p.IconClass, "bi bi-check-circle")
            .AddChildContent("Apply"));

        var button = component.Find("button");
        Assert.Equal("I", button.FirstChild?.NodeName);
        Assert.Equal(" Apply", button.TextContent);
    }

    [Fact]
    public void Render_IconClass_RendersIconWithAriaHidden()
    {
        var component = Render<Button>(parameters => parameters
            .Add(p => p.IconClass, "bi bi-check-circle"));

        var icon = component.Find("button > i");
        Assert.Equal("bi bi-check-circle", icon.GetAttribute("class"));
        Assert.Equal("true", icon.GetAttribute("aria-hidden"));
    }

    [Fact]
    public void Render_IconOnly_AddsIconButtonClass()
    {
        var component = Render<Button>(parameters => parameters
            .Add(p => p.IconOnly, true));

        var button = component.Find("button");
        Assert.Contains("icon-button", button.ClassList);
    }

    [Fact]
    public void Render_IconOnly_NoTrailingSpaceAfterIcon()
    {
        var component = Render<Button>(parameters => parameters
            .Add(p => p.IconClass, "bi bi-x")
            .Add(p => p.IconOnly, true));

        var button = component.Find("button");
        Assert.Empty(button.TextContent);
    }

    [Fact]
    public void Render_NoIconClass_OmitsIconElement()
    {
        var component = Render<Button>(parameters => parameters
            .AddChildContent("Apply"));

        Assert.Empty(component.FindAll("button > i"));
    }

    [Fact]
    public void Render_PrimaryButton_HasButtonGreenClass()
    {
        var component = Render<PrimaryButton>();

        var button = component.Find("button");
        Assert.Contains("button", button.ClassList);
        Assert.Contains("button-green", button.ClassList);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Render_SplattedAutofocusBool_MatchesNativeBehavior(bool autofocus)
    {
        var component = Render<Button>(parameters => parameters
            .AddUnmatched("autofocus", autofocus));

        var button = component.Find("button");
        Assert.Equal(autofocus, button.HasAttribute("autofocus"));
    }

    [Fact]
    public void Render_SplattedClass_DoesNotOverrideComponentClass()
    {
        var component = Render<Button>(parameters => parameters
            .Add(p => p.CssClass, "custom")
            .AddUnmatched("class", "injected"));

        var button = component.Find("button");
        Assert.Contains("button", button.ClassList);
        Assert.Contains("custom", button.ClassList);
        Assert.DoesNotContain("injected", button.ClassList);
    }

    [Fact]
    public void Render_TypeOverride_AppliesType()
    {
        var component = Render<Button>(parameters => parameters
            .Add(p => p.Type, "submit"));

        var button = component.Find("button");
        Assert.Equal("submit", button.GetAttribute("type"));
    }

    [Fact]
    public void Render_WarningButton_HasButtonYellowClass()
    {
        var component = Render<WarningButton>();

        var button = component.Find("button");
        Assert.Contains("button", button.ClassList);
        Assert.Contains("button-yellow", button.ClassList);
    }
}
