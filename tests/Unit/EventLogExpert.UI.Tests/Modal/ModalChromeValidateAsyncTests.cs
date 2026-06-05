// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.UI.Modal;
using EventLogExpert.UI.Tests.TestUtils;

namespace EventLogExpert.UI.Tests.Modal;

public sealed class ModalChromeValidateAsyncTests : BunitContext
{
    public ModalChromeValidateAsyncTests()
    {
        Services.AddBannerHostDependencies();
        Services.AddMenuMocks();

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void NonPrompt_WithValidate_ValidatorNotInvoked_NoErrorRendered()
    {
        bool invoked = false;
        var alert = new InlineAlertRequest(
            Title: "Confirm",
            Message: "Are you sure?",
            AcceptLabel: "Yes",
            CancelLabel: "No",
            IsPrompt: false,
            PromptInitialValue: null)
        {
            Validate = _ =>
            {
                invoked = true;
                return "should never render";
            },
        };

        var component = Render<ModalChrome>(p => p
            .Add(x => x.InlineAlert, alert)
            .AddChildContent("<p>body</p>"));

        Assert.False(invoked);
        Assert.Empty(component.FindAll(".inline-alert-validation-error"));
    }

    [Fact]
    public void Prompt_ValidationErrorId_StableAcrossRenders()
    {
        var alert = new InlineAlertRequest(
            Title: "Rename",
            Message: "New name:",
            AcceptLabel: "OK",
            CancelLabel: "Cancel",
            IsPrompt: true,
            PromptInitialValue: "bad")
        {
            Validate = _ => "Name is invalid.",
        };

        var component = Render<ModalChrome>(p => p
            .Add(x => x.InlineAlert, alert)
            .AddChildContent("<p>body</p>"));

        var firstId = component.Find(".inline-alert-validation-error").Id;

        component.Render();

        var secondId = component.Find(".inline-alert-validation-error").Id;

        Assert.Equal(firstId, secondId);
    }

    [Fact]
    public void Prompt_ValidatorReturnsError_InitialValue_AcceptButtonDisabled()
    {
        var alert = new InlineAlertRequest(
            Title: "Rename",
            Message: "New name:",
            AcceptLabel: "OK",
            CancelLabel: "Cancel",
            IsPrompt: true,
            PromptInitialValue: "bad")
        {
            Validate = _ => "Name is invalid.",
        };

        var component = Render<ModalChrome>(p => p
            .Add(x => x.InlineAlert, alert)
            .AddChildContent("<p>body</p>"));

        var accept = component.FindAll(".inline-alert-buttons button")[0];
        Assert.True(accept.HasAttribute("disabled"));
    }

    [Fact]
    public void Prompt_ValidatorReturnsError_InputHasAriaInvalidTrueAndAriaDescribedBy()
    {
        var alert = new InlineAlertRequest(
            Title: "Rename",
            Message: "New name:",
            AcceptLabel: "OK",
            CancelLabel: "Cancel",
            IsPrompt: true,
            PromptInitialValue: "bad")
        {
            Validate = _ => "Name is invalid.",
        };

        var component = Render<ModalChrome>(p => p
            .Add(x => x.InlineAlert, alert)
            .AddChildContent("<p>body</p>"));

        var input = component.Find("input.inline-alert-input");
        Assert.Equal("true", input.GetAttribute("aria-invalid"));
        var describedBy = input.GetAttribute("aria-describedby");
        Assert.False(string.IsNullOrEmpty(describedBy));

        var error = component.Find(".inline-alert-validation-error");
        Assert.Equal(describedBy, error.Id);
    }

    [Fact]
    public void Prompt_ValidatorReturnsError_RendersErrorParagraph()
    {
        var alert = new InlineAlertRequest(
            Title: "Rename",
            Message: "New name:",
            AcceptLabel: "OK",
            CancelLabel: "Cancel",
            IsPrompt: true,
            PromptInitialValue: "bad")
        {
            Validate = _ => "Name is invalid.",
        };

        var component = Render<ModalChrome>(p => p
            .Add(x => x.InlineAlert, alert)
            .AddChildContent("<p>body</p>"));

        var error = component.Find(".inline-alert-validation-error");
        Assert.Equal("Name is invalid.", error.TextContent);
        Assert.Equal("polite", error.GetAttribute("aria-live"));
    }

    [Fact]
    public void Prompt_ValidatorReturnsNull_InitialValue_AcceptButtonEnabled()
    {
        var alert = new InlineAlertRequest(
            Title: "Rename",
            Message: "New name:",
            AcceptLabel: "OK",
            CancelLabel: "Cancel",
            IsPrompt: true,
            PromptInitialValue: "valid")
        {
            Validate = _ => null,
        };

        var component = Render<ModalChrome>(p => p
            .Add(x => x.InlineAlert, alert)
            .AddChildContent("<p>body</p>"));

        var accept = component.FindAll(".inline-alert-buttons button")[0];
        Assert.False(accept.HasAttribute("disabled"));
        Assert.Empty(component.FindAll(".inline-alert-validation-error"));
    }

    [Fact]
    public void Prompt_ValidatorReturnsNull_InputHasNoAriaInvalid()
    {
        var alert = new InlineAlertRequest(
            Title: "Rename",
            Message: "New name:",
            AcceptLabel: "OK",
            CancelLabel: "Cancel",
            IsPrompt: true,
            PromptInitialValue: "valid")
        {
            Validate = _ => null,
        };

        var component = Render<ModalChrome>(p => p
            .Add(x => x.InlineAlert, alert)
            .AddChildContent("<p>body</p>"));

        var input = component.Find("input.inline-alert-input");
        Assert.False(input.HasAttribute("aria-invalid"));
        Assert.False(input.HasAttribute("aria-describedby"));
    }

    [Fact]
    public void Prompt_WithoutValidate_AcceptButtonEnabled()
    {
        var alert = new InlineAlertRequest(
            Title: "Rename",
            Message: "New name:",
            AcceptLabel: "OK",
            CancelLabel: "Cancel",
            IsPrompt: true,
            PromptInitialValue: "current");

        var component = Render<ModalChrome>(p => p
            .Add(x => x.InlineAlert, alert)
            .AddChildContent("<p>body</p>"));

        var accept = component.FindAll(".inline-alert-buttons button")[0];
        Assert.False(accept.HasAttribute("disabled"));
    }
}
