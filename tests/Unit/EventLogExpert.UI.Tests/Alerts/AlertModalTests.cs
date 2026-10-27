// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.UI.Alerts;
using EventLogExpert.UI.Modal;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Alerts;

public sealed class AlertModalTests : BunitContext
{
    public AlertModalTests()
    {
        Services.AddBannerHostDependencies();
        Services.AddMenuMocks();

        var modalService = Substitute.For<IModalService>();
        modalService.ActiveModalId.Returns(new ModalId(1L));
        Services.AddSingleton(modalService);
        Services.AddSingleton(Substitute.For<IModalCoordinator>());

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Theory]
    [InlineData(false, "Clear all")]
    [InlineData(true, "Cancel")]
    public void Confirmation_AutofocusUsesTheRequestedDefault(bool defaultToCancel, string expectedLabel)
    {
        var component = Render<AlertModal>(parameters => parameters
            .Add(modal => modal.Title, "Clear notifications?")
            .Add(modal => modal.AcceptLabel, "Clear all")
            .Add(modal => modal.CancelLabel, "Cancel")
            .Add(modal => modal.DefaultToCancel, defaultToCancel));

        var defaultButton = component.Find("button[autofocus]");
        Assert.Equal(expectedLabel, defaultButton.TextContent.Trim());
        defaultButton.Click();
        Services.GetRequiredService<IModalService>().Received(1).Complete(new ModalId(1L), !defaultToCancel);
    }

    [Fact]
    public void Render_DialogUsesFixedWindowsStyleWidth()
    {
        var component = Render<AlertModal>(parameters => parameters
            .Add(p => p.Title, "Alert")
            .Add(p => p.Message, "Something happened."));

        var style = component.Find("dialog").GetAttribute("style");

        Assert.Contains("--modal-min-width: min(34rem, calc(100vw - 2rem));", style);
        Assert.Contains("--modal-max-width: min(34rem, calc(100vw - 2rem));", style);
        Assert.Empty(component.FindAll(".notification-trigger"));
        Assert.Contains("Alert", component.Find(".dialog-title").TextContent);
    }
}
