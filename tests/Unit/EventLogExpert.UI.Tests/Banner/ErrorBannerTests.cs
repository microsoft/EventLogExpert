// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.UI.Banner;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Banner;

public sealed class ErrorBannerTests : BunitContext
{
    private readonly IErrorBannerService _errorBannerService = Substitute.For<IErrorBannerService>();
    private readonly ITraceLogger _traceLogger = Substitute.For<ITraceLogger>();

    public ErrorBannerTests()
    {
        Services.AddSingleton(_errorBannerService);
        Services.AddSingleton(_traceLogger);

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task ErrorBanner_ActionClicked_InvokesSuppliedCallback()
    {
        int actionInvocationCount = 0;
        var entry = new ErrorBannerEntry(BannerId.Create(),
            "Database",
            "Recovery required",
            "Resolve",
            () => { actionInvocationCount++; return Task.CompletedTask; },
            DateTime.UtcNow);

        var component = Render<ErrorBanner>(p => p.Add(c => c.Entry, entry));
        await component.Find("aside.banner-error button.banner-action").ClickAsync(new MouseEventArgs());

        Assert.Equal(1, actionInvocationCount);
    }

    [Fact]
    public async Task ErrorBanner_ActionThrows_LogsViaTraceLogger_DoesNotPropagate()
    {
        // Arrange — action exceptions must be swallowed so they do not bubble up to ErrorBoundary.
        var actionException = new InvalidOperationException("action boom");
        var entry = new ErrorBannerEntry(BannerId.Create(),
            "Database",
            "Recovery required",
            "Resolve",
            () => throw actionException,
            DateTime.UtcNow);

        var component = Render<ErrorBanner>(p => p.Add(c => c.Entry, entry));

        // Act
        await component.Find("aside.banner-error button.banner-action").ClickAsync(new MouseEventArgs());

        // Assert — banner stays visible and the exception was logged.
        Assert.Single(component.FindAll("aside.banner-error"));
        _traceLogger.Received(1).Error(Arg.Is<ErrorLogHandler>(h =>
            h.ToString().Contains(nameof(ErrorBanner)) && h.ToString().Contains("action boom")));
    }

    [Fact]
    public async Task ErrorBanner_DismissClicked_CallsDismissErrorWithEntryId()
    {
        var entry = new ErrorBannerEntry(BannerId.Create(), "Database", "Schema invalid", null, null, DateTime.UtcNow);

        var component = Render<ErrorBanner>(p => p.Add(c => c.Entry, entry));
        await component.Find("aside.banner-error button.banner-dismiss").ClickAsync(new MouseEventArgs());

        _errorBannerService.Received(1).DismissError(entry.Id);
    }

    [Fact]
    public void ErrorBanner_WithAction_RendersActionButtonWithLabel()
    {
        var entry = new ErrorBannerEntry(BannerId.Create(),
            "Database",
            "Recovery required",
            "Resolve",
            () => Task.CompletedTask,
            DateTime.UtcNow);

        var component = Render<ErrorBanner>(p => p.Add(c => c.Entry, entry));

        var actionButton = component.Find("aside.banner-error button.banner-action");
        Assert.Equal("Resolve", actionButton.TextContent.Trim());
    }

    [Fact]
    public void ErrorBanner_WithoutAction_DoesNotRenderActionButton()
    {
        var entry = new ErrorBannerEntry(BannerId.Create(), "Database", "Schema invalid", null, null, DateTime.UtcNow);

        var component = Render<ErrorBanner>(p => p.Add(c => c.Entry, entry));

        Assert.Empty(component.FindAll("aside.banner-error button.banner-action"));
    }
}
