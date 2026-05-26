// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.UI.Banner;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Banner;

public sealed class AttentionBannerTests : BunitContext
{
    private readonly IAttentionBannerService _attentionBannerService = Substitute.For<IAttentionBannerService>();
    private readonly IErrorBannerService _errorBannerService = Substitute.For<IErrorBannerService>();
    private readonly IMenuActionService _menuActionService = Substitute.For<IMenuActionService>();
    private readonly ITraceLogger _traceLogger = Substitute.For<ITraceLogger>();

    public AttentionBannerTests()
    {
        Services.AddSingleton(_attentionBannerService);
        Services.AddSingleton(_errorBannerService);
        Services.AddSingleton(_menuActionService);
        Services.AddSingleton(_traceLogger);

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task AttentionBanner_DismissClicked_CallsDismissAttention()
    {
        var component = RenderAttentionBanner(1);
        await component.Find("aside.banner-attention button.banner-dismiss").ClickAsync(new MouseEventArgs());

        _attentionBannerService.Received(1).DismissAttention();
    }

    [Fact]
    public async Task AttentionBanner_OpenSettingsClicked_DismissesAttention_BeforeAwaitingOpenSettings()
    {
        // Clicking the action button is itself the user-acknowledgement that they're acting on the items;
        // dismissing immediately means the banner doesn't linger while the modal opens (which can take a
        // perceptible beat). On failure the error banner replaces the attention banner as the visible signal.
        _menuActionService.OpenSettingsAsync().Returns(Task.FromResult(true));

        var component = RenderAttentionBanner(1);
        await component.Find("aside.banner-attention button.banner-action").ClickAsync(new MouseEventArgs());

        Received.InOrder(
            () =>
            {
                _attentionBannerService.DismissAttention();
                _ = _menuActionService.OpenSettingsAsync();
            });
        _errorBannerService.DidNotReceive().ReportError(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task AttentionBanner_OpenSettingsReturnsFalse_DismissesAttentionImmediately_AndReportsRecoverableError()
    {
        // Dismiss-immediately semantics: the attention banner is gone the instant the user clicked, regardless
        // of outcome. When OpenSettingsAsync returns false (caught internally), surface a recoverable Error so
        // the user knows the click was received but the modal failed to open.
        _menuActionService.OpenSettingsAsync().Returns(Task.FromResult(false));
        BannerCycleItem? captured = null;
        Action<BannerCycleItem> handler = c => captured = c;

        var component = Render<AttentionBanner>(parameters =>
        {
            parameters.Add(p => p.AttentionCount, 1);
            parameters.Add(
                p => p.OnFallbackErrorPosted,
                EventCallback.Factory.Create(this, handler));
        });
        await component.Find("aside.banner-attention button.banner-action").ClickAsync(new MouseEventArgs());

        _attentionBannerService.Received(1).DismissAttention();
        _errorBannerService.Received(1)
            .ReportError("Settings", Arg.Is<string>(s => s.Contains("Failed to open settings")));
        Assert.NotNull(captured);
        Assert.Equal(BannerView.Error, captured.View);
    }

    [Fact]
    public async Task AttentionBanner_OpenSettingsThrowsJSDisconnected_DismissesAttention_NoErrorReport()
    {
        // Per rule 3.9, JSDisconnectedException is expected during teardown and must be caught silently — it
        // does not warrant ReportError surface (the user closed the circuit themselves). The dismiss happened
        // before the await so the attention banner is already gone by the time the throw lands.
        _menuActionService.OpenSettingsAsync()
            .Returns(Task.FromException<bool>(new JSDisconnectedException("circuit gone")));

        var component = RenderAttentionBanner(1);
        await component.Find("aside.banner-attention button.banner-action").ClickAsync(new MouseEventArgs());

        _attentionBannerService.Received(1).DismissAttention();
        _errorBannerService.DidNotReceive().ReportError(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task AttentionBanner_OpenSettingsThrowsUnexpectedly_DismissesAttention_LogsAndReportsRecoverableError()
    {
        // Defensive path: contract says OpenSettingsAsync catches internally, but a synchronous throw before the
        // first await would still bubble. Must not propagate to ErrorBoundary (which would escalate the visible
        // banner from Attention to Critical). Surface as Error; attention was already dismissed on click.
        var openException = new InvalidOperationException("modal boom");
        _menuActionService.OpenSettingsAsync().Returns(Task.FromException<bool>(openException));
        BannerCycleItem? captured = null;
        Action<BannerCycleItem> handler = c => captured = c;

        var component = Render<AttentionBanner>(parameters =>
        {
            parameters.Add(p => p.AttentionCount, 1);
            parameters.Add(
                p => p.OnFallbackErrorPosted,
                EventCallback.Factory.Create(this, handler));
        });
        await component.Find("aside.banner-attention button.banner-action").ClickAsync(new MouseEventArgs());

        _attentionBannerService.Received(1).DismissAttention();
        _errorBannerService.Received(1)
            .ReportError("Settings", Arg.Is<string>(s => s.Contains("modal boom")));
        _traceLogger.Received(1).Error(Arg.Is<ErrorLogHandler>(h =>
            h.ToString().Contains(nameof(AttentionBanner)) && h.ToString().Contains("modal boom")));
        Assert.NotNull(captured);
        Assert.Equal(BannerView.Error, captured.View);
    }

    [Fact]
    public void AttentionBanner_RendersWithOpenSettingsAndDismiss()
    {
        var component = RenderAttentionBanner(2);

        var banner = component.Find("aside.banner-attention");
        Assert.Contains("2 databases need attention", banner.TextContent);
        Assert.Equal("Open Settings", component.Find("aside.banner-attention button.banner-action").TextContent.Trim());
        Assert.Single(component.FindAll("aside.banner-attention button.banner-dismiss"));
    }

    [Fact]
    public void AttentionBanner_SingleEntry_UsesSingularDatabaseLabel()
    {
        var component = RenderAttentionBanner(1);

        var banner = component.Find("aside.banner-attention");
        Assert.Contains("1 database need", banner.TextContent);
        Assert.DoesNotContain("databases need", banner.TextContent);
    }

    private IRenderedComponent<AttentionBanner> RenderAttentionBanner(
        int attentionCount,
        EventCallback<BannerCycleItem> onFallbackErrorPosted = default) =>
        Render<AttentionBanner>(parameters =>
        {
            parameters.Add(c => c.AttentionCount, attentionCount);
            parameters.Add(c => c.OnFallbackErrorPosted, onFallbackErrorPosted);
        });
}
