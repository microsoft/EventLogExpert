// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Localization;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.UI.Banner;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
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
        Services.AddSingleton<IStringLocalizer<SharedResource>>(new MarkerLocalizer());
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
    public async Task AttentionBanner_OpenDatabasesClicked_DismissesAttention_BeforeAwaitingOpenDatabases()
    {
        _menuActionService.OpenDatabaseToolsAsync().Returns(Task.FromResult(true));

        var component = RenderAttentionBanner(1);
        await component.Find("aside.banner-attention button.banner-action").ClickAsync(new MouseEventArgs());

        Received.InOrder(
            () =>
            {
                _attentionBannerService.DismissAttention();
                _ = _menuActionService.OpenDatabaseToolsAsync();
            });
        _errorBannerService.DidNotReceive().ReportError(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task AttentionBanner_OpenDatabasesReturnsFalse_DismissesAttentionImmediately_AndReportsRecoverableError()
    {
        _menuActionService.OpenDatabaseToolsAsync().Returns(Task.FromResult(false));
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
            .ReportError("[[Banner_Attention_ErrorTitle]]", "[[Banner_Attention_OpenFailed]]");
        Assert.NotNull(captured);
        Assert.Equal(BannerView.Error, captured.View);
    }

    [Fact]
    public async Task AttentionBanner_OpenDatabasesThrowsJSDisconnected_DismissesAttention_NoErrorReport()
    {
        _menuActionService.OpenDatabaseToolsAsync()
            .Returns(Task.FromException<bool>(new JSDisconnectedException("circuit gone")));

        var component = RenderAttentionBanner(1);
        await component.Find("aside.banner-attention button.banner-action").ClickAsync(new MouseEventArgs());

        _attentionBannerService.Received(1).DismissAttention();
        _errorBannerService.DidNotReceive().ReportError(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task AttentionBanner_OpenDatabasesThrowsUnexpectedly_DismissesAttention_LogsAndReportsRecoverableError()
    {
        var openException = new InvalidOperationException("modal boom");
        _menuActionService.OpenDatabaseToolsAsync().Returns(Task.FromException<bool>(openException));
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
            .ReportError("[[Banner_Attention_ErrorTitle]]", "[[Banner_Attention_OpenFailedDetail(modal boom)]]");
        _traceLogger.Received(1).Error(Arg.Is<ErrorLogHandler>(h =>
            h.ToString().Contains(nameof(AttentionBanner)) && h.ToString().Contains("modal boom")));
        Assert.NotNull(captured);
        Assert.Equal(BannerView.Error, captured.View);
    }

    [Fact]
    public void AttentionBanner_RendersWithOpenDatabasesAndDismiss()
    {
        var component = RenderAttentionBanner(2);

        var banner = component.Find("aside.banner-attention");
        Assert.Contains("[[Banner_Attention_Many(2)]]", banner.TextContent);
        Assert.Equal("[[Banner_Attention_OpenDatabases]]", component.Find("aside.banner-attention button.banner-action").TextContent.Trim());
        Assert.Single(component.FindAll("aside.banner-attention button.banner-dismiss"));
    }

    [Fact]
    public void AttentionBanner_SingleEntry_UsesSingularDatabaseLabelAndVerb()
    {
        var component = RenderAttentionBanner(1);

        var banner = component.Find("aside.banner-attention");
        Assert.Contains("[[Banner_Attention_One(1)]]", banner.TextContent);
        Assert.DoesNotContain("[[Banner_Attention_Many", banner.TextContent);
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
