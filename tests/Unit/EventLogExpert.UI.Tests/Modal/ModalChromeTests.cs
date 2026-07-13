// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.UI.Banner;
using EventLogExpert.UI.Modal;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace EventLogExpert.UI.Tests.Modal;

public sealed class ModalChromeTests : BunitContext
{
    public ModalChromeTests()
    {
        Services.AddBannerHostDependencies();
        Services.AddMenuMocks();

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void ClickSecondaryButton_ResolvesWithSecondaryChosen()
    {
        InlineAlertResult? captured = null;
        var alert = new InlineAlertRequest(
            Title: "Filters with an empty value",
            Message: "Choose",
            AcceptLabel: "Normalize",
            CancelLabel: "Cancel",
            IsPrompt: false,
            PromptInitialValue: null)
        {
            SecondaryActionLabel = "Import as-is",
        };

        var component = Render<ModalChrome>(parameters => parameters
            .Add(p => p.Title, "Test")
            .Add(p => p.InlineAlert, alert)
            .Add(
                p => p.OnInlineAlertResolved,
                EventCallback.Factory.Create<InlineAlertResult>(this, result => captured = result))
            .AddChildContent("<p>body</p>"));

        component.FindAll(".inline-alert-buttons button")
            .Single(button => button.TextContent.Contains("Import as-is"))
            .Click();

        Assert.NotNull(captured);
        Assert.False(captured.Accepted);
        Assert.True(captured.SecondaryChosen);
    }

    [Fact]
    public void ClickSecondaryButton_WhenPrompt_CarriesTypedPromptValue()
    {
        // Regression: the secondary action must preserve the typed prompt value in a prompt scenario,
        // consistent with the Accept path. Previously it hardcoded PromptValue = null, silently losing input.
        InlineAlertResult? captured = null;
        var alert = new InlineAlertRequest(
            Title: "Rename",
            Message: "New name:",
            AcceptLabel: "Rename",
            CancelLabel: "Cancel",
            IsPrompt: true,
            PromptInitialValue: "current")
        {
            SecondaryActionLabel = "Reset to default",
        };

        var component = Render<ModalChrome>(parameters => parameters
            .Add(p => p.Title, "Test")
            .Add(p => p.InlineAlert, alert)
            .Add(
                p => p.OnInlineAlertResolved,
                EventCallback.Factory.Create<InlineAlertResult>(this, result => captured = result))
            .AddChildContent("<p>body</p>"));

        component.Find("input.dialog-input").Input("renamed");

        component.FindAll(".inline-alert-buttons button")
            .Single(button => button.TextContent.Contains("Reset to default"))
            .Click();

        Assert.NotNull(captured);
        Assert.False(captured.Accepted);
        Assert.True(captured.SecondaryChosen);
        Assert.Equal("renamed", captured.PromptValue);
    }

    [Fact]
    public async Task DisposeAsync_SetsModalContentDisplayedFalse()
    {
        var cycleState = Services.GetRequiredService<IBannerCycleStateService>();
        var component = Render<ModalChrome>(parameters => parameters
            .Add(p => p.Title, "Test")
            .AddChildContent("<p>body</p>"));

        await component.WaitForAssertionAsync(() => Assert.True(cycleState.ModalContentDisplayed));

        await component.Instance.DisposeAsync();

        Assert.False(cycleState.ModalContentDisplayed);
    }

    [Fact]
    public void Render_BannerHostWrapper_DoesNotHaveInertAttributeWhenNoInlineAlert()
    {
        var component = Render<ModalChrome>(parameters => parameters
            .Add(p => p.Title, "Test")
            .AddChildContent("<p>body</p>"));

        var dialogGroup = component.Find(".dialog-group");
        var wrapper = dialogGroup.Children[0];
        Assert.Contains("banner-region", wrapper.GetAttribute("class") ?? string.Empty);
        Assert.False(wrapper.HasAttribute("inert"));
    }

    [Fact]
    public void Render_BannerHostWrapper_HasInertAttributeWhenInlineAlertShown()
    {
        var alert = new InlineAlertRequest(
            Title: "Confirm",
            Message: "Are you sure?",
            AcceptLabel: "Yes",
            CancelLabel: "No",
            IsPrompt: false,
            PromptInitialValue: null);

        var component = Render<ModalChrome>(parameters => parameters
            .Add(p => p.Title, "Test")
            .Add(p => p.InlineAlert, alert)
            .AddChildContent("<p>body</p>"));

        var dialogGroup = component.Find(".dialog-group");
        var wrapper = dialogGroup.Children[0];
        Assert.Contains("banner-region", wrapper.GetAttribute("class") ?? string.Empty);
        Assert.True(wrapper.HasAttribute("inert"));
    }

    [Fact]
    public void Render_BannerHostWrapper_PresentAtTopOfDialogGroup()
    {
        var component = Render<ModalChrome>(parameters => parameters
            .Add(p => p.Title, "Test")
            .AddChildContent("<p>body</p>"));

        var dialogGroup = component.Find(".dialog-group");
        Assert.NotNull(dialogGroup);

        var firstChild = dialogGroup.Children[0];
        Assert.Contains("banner-region", firstChild.GetAttribute("class") ?? string.Empty);
    }

    [Fact]
    public void Render_BannerHostWrapper_RendersNothingWhenNoBannersAndNoModalActive()
    {
        var component = Render<ModalChrome>(parameters => parameters
            .Add(p => p.Title, "Test")
            .AddChildContent("<p>body</p>"));

        var dialogGroup = component.Find(".dialog-group");
        var wrapper = dialogGroup.Children[0];
        Assert.Contains("banner-region", wrapper.GetAttribute("class") ?? string.Empty);
        Assert.Empty(wrapper.Children);
    }

    [Fact]
    public async Task Render_FirstRender_SetsModalContentDisplayedTrue_AfterShowModal()
    {
        var cycleState = Services.GetRequiredService<IBannerCycleStateService>();
        Assert.False(cycleState.ModalContentDisplayed);

        var component = Render<ModalChrome>(parameters => parameters
            .Add(p => p.Title, "Test")
            .AddChildContent("<p>body</p>"));

        await component.WaitForAssertionAsync(() => Assert.True(cycleState.ModalContentDisplayed));
    }

    [Fact]
    public void Render_InlineAlertWithoutSecondaryActionLabel_RendersNoSecondaryButton()
    {
        var alert = new InlineAlertRequest(
            Title: "Confirm",
            Message: "Are you sure?",
            AcceptLabel: "Yes",
            CancelLabel: "No",
            IsPrompt: false,
            PromptInitialValue: null);

        var component = Render<ModalChrome>(parameters => parameters
            .Add(p => p.Title, "Test")
            .Add(p => p.InlineAlert, alert)
            .AddChildContent("<p>body</p>"));

        Assert.Equal(2, component.FindAll(".inline-alert-buttons button").Count);
    }

    [Fact]
    public void Render_InlineAlertWithSecondaryActionLabel_RendersSecondaryButton()
    {
        var alert = new InlineAlertRequest(
            Title: "Filters with an empty value",
            Message: "Choose",
            AcceptLabel: "Normalize",
            CancelLabel: "Cancel",
            IsPrompt: false,
            PromptInitialValue: null)
        {
            SecondaryActionLabel = "Import as-is",
        };

        var component = Render<ModalChrome>(parameters => parameters
            .Add(p => p.Title, "Test")
            .Add(p => p.InlineAlert, alert)
            .AddChildContent("<p>body</p>"));

        Assert.Equal(3, component.FindAll(".inline-alert-buttons button").Count);
    }

    [Fact]
    public void Render_WhenFooterPresetNone_RendersNoBuiltInButtons()
    {
        var component = Render<ModalChrome>(parameters => parameters
            .Add(p => p.Title, "Test")
            .Add(p => p.Footer, FooterPreset.None)
            .AddChildContent("<p>body</p>"));

        var footerGroup = component.Find(".footer-group");
        Assert.Empty(footerGroup.QuerySelectorAll("button"));
    }

    [Fact]
    public void Render_WhenFooterPresetNoneWithExtraFooterContent_RendersOnlyExtraContent()
    {
        var component = Render<ModalChrome>(parameters => parameters
            .Add(p => p.Title, "Test")
            .Add(p => p.Footer, FooterPreset.None)
            .Add(p => p.ExtraFooterContent, builder =>
            {
                builder.OpenElement(0, "button");
                builder.AddAttribute(1, "type", "button");
                builder.AddContent(2, "One");
                builder.CloseElement();
                builder.OpenElement(3, "button");
                builder.AddAttribute(4, "type", "button");
                builder.AddContent(5, "Two");
                builder.CloseElement();
                builder.OpenElement(6, "button");
                builder.AddAttribute(7, "type", "button");
                builder.AddContent(8, "Three");
                builder.CloseElement();
            })
            .AddChildContent("<p>body</p>"));

        var footerGroup = component.Find(".footer-group");
        var buttons = footerGroup.QuerySelectorAll("button");
        Assert.Equal(3, buttons.Length);
    }
}
