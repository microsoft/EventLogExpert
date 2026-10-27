// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using EventLogExpert.UI.Alerts;
using EventLogExpert.UI.Modal;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Modal;

public sealed class ModalChromeValidateAsyncTests : BunitContext
{
    public ModalChromeValidateAsyncTests()
    {
        Services.AddBannerHostDependencies();
        Services.AddMenuMocks();

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InlineAlertFocusDisposalFailure_LogsOnlyWhileTheModalIsOpen(bool closed)
    {
        var component = Render<ModalChrome>(parameters => parameters.Add(modal => modal.ShowNotifications, false));
        ITraceLogger traceLogger = Services.GetRequiredService<ITraceLogger>();
        ObjectDisposedException failure = new("inline-alert-module");
        JSInterop.SetupVoid("captureInlineAlertFocus", _ => true).SetException(failure);
        InlineAlertRequest alert = new("Confirm", "Continue?", "Yes", "No", false, null);
        if (closed) { await component.Instance.CloseAsync(); }

        await component.InvokeAsync(() => component.Render(parameters => parameters.Add(modal => modal.InlineAlert, alert)));

        if (closed)
        {
            traceLogger.DidNotReceive().Warning(Arg.Any<WarningLogHandler>());
        }
        else
        {
            traceLogger.Received(1).Warning(Arg.Is<WarningLogHandler>(handler =>
                handler.ToString().Contains("captureInlineAlertFocus") && handler.ToString().Contains(failure.ToString())));
        }
        Assert.False(Renderer.UnhandledException.IsCompleted);
        Assert.Single(component.FindAll(".inline-alert"));
    }

    [Theory]
    [InlineData("captureInlineAlertFocus")]
    [InlineData("rememberInlineAlert")]
    [InlineData("restoreInlineAlertFocus")]
    public async Task InlineAlertFocusInteropFailure_LogsWarningAndDoesNotTearDownTheRenderer(string identifier)
    {
        var component = Render<ModalChrome>(parameters => parameters.Add(modal => modal.ShowNotifications, false));
        ITraceLogger traceLogger = Services.GetRequiredService<ITraceLogger>();
        JSException failure = new("Dialog focus is unavailable.");
        JSInterop.SetupVoid(identifier, _ => true).SetException(failure);
        InlineAlertRequest alert = new("Confirm", "Are you sure?", "Yes", "No", false, null);

        await component.InvokeAsync(() => component.Render(parameters => parameters.Add(modal => modal.InlineAlert, alert)));

        Assert.False(Renderer.UnhandledException.IsCompleted);
        Assert.Single(component.FindAll(".inline-alert"));
        await component.InvokeAsync(() => component.Render(parameters => parameters.Add(modal => modal.InlineAlert, null)));
        traceLogger.Received(1).Warning(Arg.Is<WarningLogHandler>(handler =>
            handler.ToString().Contains(identifier) && handler.ToString().Contains(failure.ToString())));
        Assert.False(Renderer.UnhandledException.IsCompleted);
    }

    [Theory]
    [InlineData("captureInlineAlertFocus")]
    [InlineData("rememberInlineAlert")]
    [InlineData("restoreInlineAlertFocus")]
    public async Task InlineAlertJavaScriptFailure_AfterClose_DoesNotLogWarning(string identifier)
    {
        var component = Render<ModalChrome>(parameters => parameters.Add(modal => modal.ShowNotifications, false));
        ITraceLogger traceLogger = Services.GetRequiredService<ITraceLogger>();
        JSException failure = new("Dialog focus is unavailable.");
        JSInterop.SetupVoid(identifier, _ => true).SetException(failure);
        InlineAlertRequest alert = new("Confirm", "Continue?", "Yes", "No", false, null);
        await component.Instance.CloseAsync();

        await component.InvokeAsync(() => component.Render(parameters => parameters.Add(modal => modal.InlineAlert, alert)));
        await component.InvokeAsync(() => component.Render(parameters => parameters.Add(modal => modal.InlineAlert, null)));

        Assert.Single(JSInterop.Invocations[identifier]);
        traceLogger.DidNotReceive().Warning(Arg.Any<WarningLogHandler>());
        Assert.False(Renderer.UnhandledException.IsCompleted);
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
    public void Prompt_InputLabelledByMessage()
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

        var input = component.Find("input.dialog-input");
        var message = component.Find(".inline-alert-message");

        Assert.Equal(message.Id, input.GetAttribute("aria-labelledby"));
        Assert.False(input.HasAttribute("aria-label"));
    }

    [Fact]
    public void Prompt_ResetsBeforeAsynchronousFocusCaptureAllowsTheFirstRender()
    {
        var component = Render<ModalChrome>(parameters => parameters.Add(modal => modal.ShowNotifications, false));
        InlineAlertRequest previous = new("Rename", "New name:", "Rename", "Cancel", true, "previous");
        component.Render(parameters => parameters.Add(modal => modal.InlineAlert, previous));
        component.Find("input.dialog-input").Input("stale value");
        component.Render(parameters => parameters.Add(modal => modal.InlineAlert, null));
        var pendingCapture = JSInterop.SetupVoid("captureInlineAlertFocus", _ => true);
        InlineAlertRequest next = new("Rename", "New name:", "Rename", "Cancel", true, "valid")
        {
            Validate = value => value == "valid" ? null : "Name is invalid."
        };

        try
        {
            component.Render(parameters => parameters.Add(modal => modal.InlineAlert, next));

            Assert.Single(pendingCapture.Invocations);
            var input = component.Find("input.dialog-input");
            Assert.Equal("valid", input.GetAttribute("value"));
            Assert.False(input.HasAttribute("aria-invalid"));
            Assert.Empty(component.FindAll(".inline-alert-validation-error"));
            Assert.False(component.FindAll(".inline-alert-buttons button")[0].HasAttribute("disabled"));
        }
        finally
        {
            pendingCapture.SetVoidResult();
        }
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

        var input = component.Find("input.dialog-input");
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

        var input = component.Find("input.dialog-input");
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
