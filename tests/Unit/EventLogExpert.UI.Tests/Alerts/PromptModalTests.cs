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
    public void NoValidator_ShowsNoErrorAndEnablesOk()
    {
        var component = Render<PromptModal>(parameters => parameters
            .Add(p => p.Title, "Rename")
            .Add(p => p.Message, "New name:"));

        Assert.Empty(component.FindAll(".prompt-modal-validation-error"));
        Assert.False(component.FindAll(".footer-group button")[0].HasAttribute("disabled"));
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

    [Fact]
    public void Validation_AcceptingInvalidValue_DoesNotComplete()
    {
        Func<string, string?> validate = value =>
            string.IsNullOrWhiteSpace(value) ? "Name is required." : null;

        var component = Render<PromptModal>(parameters => parameters
            .Add(p => p.Title, "Rename")
            .Add(p => p.Message, "New name:")
            .Add(p => p.Validate, validate));

        component.FindAll(".footer-group button")[0].Click();

        _modalService.DidNotReceive().Complete(Arg.Any<ModalId>(), Arg.Any<string>());
    }

    [Fact]
    public void Validation_InvalidInput_SetsAriaInvalidAndDescribesError()
    {
        Func<string, string?> validate = value =>
            string.IsNullOrWhiteSpace(value) ? "Name is required." : null;

        var component = Render<PromptModal>(parameters => parameters
            .Add(p => p.Title, "Rename")
            .Add(p => p.Message, "New name:")
            .Add(p => p.Validate, validate));

        var input = component.Find("input[type='text']");
        var error = component.Find(".prompt-modal-validation-error");

        Assert.Equal("true", input.GetAttribute("aria-invalid"));
        Assert.Equal(error.Id, input.GetAttribute("aria-describedby"));
    }

    [Fact]
    public void Validation_InvalidValue_ShowsError_DisablesOk_KeepsCancelEnabled()
    {
        Func<string, string?> validate = value =>
            string.IsNullOrWhiteSpace(value) ? "Name is required." : null;

        var component = Render<PromptModal>(parameters => parameters
            .Add(p => p.Title, "Rename")
            .Add(p => p.Message, "New name:")
            .Add(p => p.Validate, validate));

        var buttons = component.FindAll(".footer-group button");

        Assert.Equal("Name is required.", component.Find(".prompt-modal-validation-error").TextContent.Trim());
        Assert.True(buttons[0].HasAttribute("disabled"));
        Assert.False(buttons[1].HasAttribute("disabled"));
    }

    [Fact]
    public void Validation_TypingValidValue_ClearsErrorAndCompletesOnAccept()
    {
        Func<string, string?> validate = value =>
            string.IsNullOrWhiteSpace(value) ? "Name is required." : null;

        var component = Render<PromptModal>(parameters => parameters
            .Add(p => p.Title, "Rename")
            .Add(p => p.Message, "New name:")
            .Add(p => p.Validate, validate));

        component.Find("input").Input("valid name");

        Assert.Empty(component.FindAll(".prompt-modal-validation-error"));

        component.FindAll(".footer-group button")[0].Click();

        _modalService.Received(1).Complete(Arg.Any<ModalId>(), "valid name");
    }
}
