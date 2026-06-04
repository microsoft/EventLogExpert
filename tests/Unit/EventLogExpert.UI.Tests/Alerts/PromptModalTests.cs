// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.UI.Alerts;
using EventLogExpert.UI.Tests.TestUtils;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Alerts;

public sealed class PromptModalTests : BunitContext
{
    public PromptModalTests()
    {
        Services.AddBannerHostDependencies();
        Services.AddMenuMocks();

        var modalService = Substitute.For<IModalService>();
        modalService.ActiveModalId.Returns(new ModalId(1L));
        Services.AddSingleton(modalService);
        Services.AddSingleton(Substitute.For<IModalCoordinator>());

        Services.AddFluxor(options => options.ScanAssemblies(typeof(PromptModal).Assembly));

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Render_AlwaysRendersDividerBetweenMessageAndInput()
    {
        var component = Render<PromptModal>(parameters => parameters
            .Add(p => p.Title, "Prompt")
            .Add(p => p.Message, "Enter a value"));

        var divider = component.Find(".alert-modal-divider");
        Assert.Equal("HR", divider.NodeName);
    }
}
