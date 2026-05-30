// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.UI.Banner;
using EventLogExpert.UI.Modal;
using EventLogExpert.UI.Tests.TestUtils;
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
}
