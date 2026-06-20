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
    private readonly IModalService _modalService = Substitute.For<IModalService>();

    public PromptModalTests()
    {
        Services.AddBannerHostDependencies();
        Services.AddMenuMocks();

        _modalService.ActiveModalId.Returns(new ModalId(1L));
        Services.AddSingleton(_modalService);
        Services.AddSingleton(Substitute.For<IModalCoordinator>());

        Services.AddFluxor(options => options.ScanAssemblies(typeof(PromptModal).Assembly));

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Accepting_CompletesWithValueTypedIntoInput()
    {
        var component = Render<PromptModal>(parameters => parameters
            .Add(p => p.Title, "Rename")
            .Add(p => p.Message, "New name:"));

        component.Find("input").Input("renamed value");
        component.Find(".footer-group .button-green").Click();

        _modalService.Received(1).Complete(Arg.Any<ModalId>(), "renamed value");
    }

    [Fact]
    public void Render_DialogUsesFixedWindowsStyleWidth()
    {
        var component = Render<PromptModal>(parameters => parameters
            .Add(p => p.Title, "Prompt")
            .Add(p => p.Message, "Enter a value"));

        var style = component.Find("dialog").GetAttribute("style");

        Assert.Contains("--modal-min-width: min(34rem, calc(100vw - 2rem));", style);
        Assert.Contains("--modal-max-width: min(34rem, calc(100vw - 2rem));", style);
    }

    [Fact]
    public void Render_InputUsesSharedDialogInputStyle()
    {
        var component = Render<PromptModal>(parameters => parameters
            .Add(p => p.Title, "Prompt")
            .Add(p => p.Message, "Enter a value"));

        var input = component.Find("input[type='text']");

        Assert.Contains("dialog-input", input.GetAttribute("class")!.Split(' '));
    }

    [Fact]
    public void Render_MessageRendersAsLabelForTheInput()
    {
        var component = Render<PromptModal>(parameters => parameters
            .Add(p => p.Title, "Prompt")
            .Add(p => p.Message, "Enter a value"));

        var label = component.Find("label");
        var input = component.Find("input");

        Assert.Equal("Enter a value", label.TextContent);
        Assert.Equal(input.GetAttribute("id"), label.GetAttribute("for"));
    }
}
