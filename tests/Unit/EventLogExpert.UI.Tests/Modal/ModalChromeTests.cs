// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.UI.Alerts;
using EventLogExpert.UI.Banner;
using EventLogExpert.UI.Inputs;
using EventLogExpert.UI.Modal;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

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
    public void DismissFooter_AlwaysAutofocusesItsOnlyButton()
    {
        var component = Render<ModalChrome>(parameters => parameters
            .Add(modal => modal.Footer, FooterPreset.Dismiss)
            .Add(modal => modal.DefaultToCancel, true)
            .Add(modal => modal.AcceptLabel, string.Empty));

        Assert.True(component.Find(".footer-group button").HasAttribute("autofocus"));
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
    public async Task EmptyTitlelessDialog_CollapsesHeaderChromeButKeepsItsNotificationCenterMounted()
    {
        var component = Render<ModalChrome>(parameters => parameters
            .Add(modal => modal.ShowCloseButton, false));
        NotificationCenter center = component.FindComponent<NotificationCenter>().Instance;
        Assert.Empty(component.FindAll(".dialog-header"));
        Assert.Contains("notification-trigger-hidden", component.Find(".notification-trigger").ClassList);
        var errors = Services.GetRequiredService<IErrorBannerService>();
        errors.ErrorBanners.Returns([
            new ErrorBannerEntry(BannerId.Create(),
                new Preformatted("Failure", "Detail"), null, new DateTime(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc))
        ]);

        await component.InvokeAsync(() => errors.StateChanged += Raise.Event<Action>());

        component.WaitForAssertion(() => Assert.Single(component.FindAll(".dialog-header")));
        Assert.DoesNotContain("notification-trigger-hidden", component.Find(".notification-trigger").ClassList);
        Assert.Same(center, component.FindComponent<NotificationCenter>().Instance);
        errors.ErrorBanners.Returns([]);

        await component.InvokeAsync(() => errors.StateChanged += Raise.Event<Action>());

        component.WaitForAssertion(() => Assert.Empty(component.FindAll(".dialog-header")));
        Assert.Same(center, component.FindComponent<NotificationCenter>().Instance);
    }

    [Fact]
    public void InlineAlert_DisablesNotificationAccessUntilResolved()
    {
        var component = Render<ModalChrome>();
        InlineAlertRequest alert = new("Confirm", "Continue?", "Continue", "Cancel", false, null);

        component.Render(parameters => parameters.Add(modal => modal.InlineAlert, alert));
        Assert.True(component.Find("header").HasAttribute("inert"));
        Assert.True(component.Find(".notification-trigger").HasAttribute("disabled"));
        Assert.Contains(JSInterop.Invocations, invocation =>
            invocation.Identifier == "setUnavailable" && invocation.Arguments.Last() is true);

        component.Render(parameters => parameters.Add(modal => modal.InlineAlert, null));
        Assert.False(component.Find(".notification-trigger").HasAttribute("disabled"));
    }

    [Theory]
    [InlineData(false, "Clear all", true)]
    [InlineData(true, "Cancel", false)]
    public async Task InlineConfirmation_FocusesTheRequestedDefaultAndResolvesItsResult(
        bool defaultToCancel, string expectedLabel, bool expectedAccepted)
    {
        InlineAlertResult? result = null;
        var component = Render<ModalChrome>(parameters => parameters
            .Add(modal => modal.ShowNotifications, false)
            .Add(modal => modal.OnInlineAlertResolved, resolved => result = resolved));
        InlineAlertRequest request = new("Clear notifications?", "Clear 2 notifications?", "Clear all", "Cancel", false, null)
        {
            DefaultToCancel = defaultToCancel
        };
        component.Render(parameters => parameters.Add(modal => modal.InlineAlert, request));
        var focusedCall = JSInterop.Invocations.Last(invocation => invocation.Identifier.Contains("domWrapper.focus", StringComparison.Ordinal));
        var defaultButton = component.Find(".inline-alert button[autofocus]");
        Assert.Equal(expectedLabel, defaultButton.TextContent.Trim());
        if (defaultToCancel)
        {
            await component.FindComponents<Button>().Single(button => button.Find("button").TextContent.Trim() == expectedLabel).Instance.FocusAsync(true);
        }
        else
        {
            await component.FindComponents<PrimaryButton>().Single().Instance.FocusAsync(true);
        }
        var expectedCall = JSInterop.Invocations.Last(invocation => invocation.Identifier.Contains("domWrapper.focus", StringComparison.Ordinal));
        Assert.Equal(expectedCall.Arguments[0], focusedCall.Arguments[0]);
        defaultButton.Click();
        Assert.NotNull(result);
        Assert.Equal(expectedAccepted, result.Accepted);
    }

    [Fact]
    public void ModalChrome_DefaultFooter_DoesNotLeakResourceKeys()
    {
        var component = Render<ModalChrome>(parameters => parameters
            .Add(p => p.Title, "Test")
            .AddChildContent("<p>body</p>"));

        Assert.DoesNotContain("Modal_", component.Markup);
    }

    [Fact]
    public void NotificationsHidden_CollapsesCompactNotificationSlotEvenWhenNoticesArePending()
    {
        Services.GetRequiredService<IErrorBannerService>().ErrorBanners.Returns([
            new ErrorBannerEntry(BannerId.Create(),
                new Preformatted("Failure", "Detail"), null, new DateTime(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc))
        ]);

        var component = Render<ModalChrome>(parameters => parameters.Add(modal => modal.ShowNotifications, false));

        Assert.Equal("false", component.Find(".notification-center-compact").GetAttribute("data-trigger-shown"));
        Assert.Empty(component.FindAll("[data-notification-trigger]"));
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
    public void TitledDialog_KeepsTitleAndCloseWhileTheNotificationTriggerIsHidden()
    {
        var component = Render<ModalChrome>(parameters => parameters
            .Add(modal => modal.Title, "Filter library")
            .Add(modal => modal.ShowCloseButton, true));

        Assert.Equal("Filter library", component.Find(".dialog-title").TextContent);
        Assert.Equal("-1", component.Find(".dialog-title").GetAttribute("tabindex"));
        Assert.Single(component.FindAll(".dialog-header .notification-trigger"));
        Assert.Single(component.FindAll(".dialog-header .dialog-close"));
    }

    [Fact]
    public void TitlelessDialog_KeepsHeaderChromeForItsCloseButton()
    {
        var component = Render<ModalChrome>(parameters => parameters
            .Add(modal => modal.ShowCloseButton, true)
            .AddChildContent("<p>Editor</p>"));

        Assert.Single(component.FindAll(".dialog-header .notification-trigger"));
        Assert.Single(component.FindAll(".dialog-header .dialog-close"));
        Assert.Empty(component.FindAll(".dialog-title"));
    }
}
