// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Localization;
using EventLogExpert.UI.Alerts;
using EventLogExpert.UI.Modal;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Tests.Modal;

public sealed class ModalChromeLocalizerWiringTests : BunitContext
{
    public ModalChromeLocalizerWiringTests()
    {
        Services.AddBannerHostDependencies();
        Services.AddMenuMocks();
        Services.AddSingleton<IStringLocalizer<SharedResource>>(new MarkerLocalizer());

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Theory]
    [InlineData(FooterPreset.Dismiss)]
    [InlineData(FooterPreset.AcceptCancel)]
    public void ModalChrome_AcceptDefault_UsesAcceptKey(FooterPreset footer)
    {
        var component = Render<ModalChrome>(parameters => parameters
            .Add(p => p.Title, "Test")
            .Add(p => p.Footer, footer)
            .AddChildContent("<p>body</p>"));

        var labels = component.FindAll(".footer-group button").Select(button => button.TextContent.Trim()).ToList();
        Assert.Contains("[[Modal_Accept]]", labels);
    }

    [Fact]
    public void ModalChrome_CloseButton_UsesCloseKeyForDefaultAriaLabel()
    {
        var component = Render<ModalChrome>(parameters => parameters
            .Add(p => p.Title, "Test")
            .Add(p => p.ShowCloseButton, true)
            .AddChildContent("<p>body</p>"));

        Assert.Equal("[[Modal_Close]]", component.Find(".dialog-close").GetAttribute("aria-label"));
    }

    [Fact]
    public void ModalChrome_ImportExportCloseDefaults_UseModalKeys()
    {
        var component = Render<ModalChrome>(parameters => parameters
            .Add(p => p.Title, "Test")
            .Add(p => p.Footer, FooterPreset.ImportExportClose)
            .AddChildContent("<p>body</p>"));

        var labels = component.FindAll(".footer-group button").Select(button => button.TextContent.Trim()).ToList();
        Assert.Contains("[[Modal_Import]]", labels);
        Assert.Contains("[[Modal_Export]]", labels);
        Assert.Contains("[[Modal_Close]]", labels);
    }

    [Fact]
    public void ModalChrome_InlineAlertWithoutTitle_UsesAlertAriaFallbackKey()
    {
        var alert = new InlineAlertRequest(
            Title: "",
            Message: "Something happened",
            AcceptLabel: "OK",
            CancelLabel: "Cancel",
            IsPrompt: false,
            PromptInitialValue: null);

        var component = Render<ModalChrome>(parameters => parameters
            .Add(p => p.Title, "Test")
            .Add(p => p.InlineAlert, alert)
            .AddChildContent("<p>body</p>"));

        Assert.Equal("[[Modal_AlertAriaFallback]]", component.Find("section.inline-alert").GetAttribute("aria-label"));
    }

    [Fact]
    public void ModalChrome_NoTitleNoAriaLabel_UsesDialogAriaFallbackKey()
    {
        var component = Render<ModalChrome>(parameters => parameters
            .Add(p => p.Footer, FooterPreset.None)
            .AddChildContent("<p>body</p>"));

        Assert.Equal("[[Modal_DialogAriaFallback]]", component.Find("dialog").GetAttribute("aria-label"));
    }

    [Fact]
    public void ModalChrome_SaveCancelDefaults_UseSaveAndCancelKeys()
    {
        var component = Render<ModalChrome>(parameters => parameters
            .Add(p => p.Title, "Test")
            .Add(p => p.Footer, FooterPreset.SaveCancel)
            .AddChildContent("<p>body</p>"));

        var labels = component.FindAll(".footer-group button").Select(button => button.TextContent.Trim()).ToList();
        Assert.Contains("[[Modal_Save]]", labels);
        Assert.Contains("[[Modal_Cancel]]", labels);
    }
}
