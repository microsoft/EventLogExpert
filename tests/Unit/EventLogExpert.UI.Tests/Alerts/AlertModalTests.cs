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

    [Fact]
    public void Render_DialogUsesFixedWindowsStyleWidth()
    {
        var component = Render<AlertModal>(parameters => parameters
            .Add(p => p.Title, "Alert")
            .Add(p => p.Message, "Something happened."));

        var style = component.Find("dialog").GetAttribute("style");

        Assert.Contains("--modal-min-width: min(34rem, calc(100vw - 2rem));", style);
        Assert.Contains("--modal-max-width: min(34rem, calc(100vw - 2rem));", style);
    }
}
