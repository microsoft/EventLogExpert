// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.Components.Tests;

public sealed class BannerHostTests : BunitContext
{
    private readonly IApplicationRestartService _applicationRestartService =
        Substitute.For<IApplicationRestartService>();
    private readonly IBannerService _bannerService = Substitute.For<IBannerService>();
    private readonly IClipboardService _clipboardService = Substitute.For<IClipboardService>();
    private readonly ITraceLogger _traceLogger = Substitute.For<ITraceLogger>();

    public BannerHostTests()
    {
        _bannerService.UnhandledError.Returns((Exception?)null);
        _bannerService.CriticalAlerts.Returns([]);
        _bannerService.InfoBanners.Returns([]);

        Services.AddSingleton(_bannerService);
        Services.AddSingleton(_applicationRestartService);
        Services.AddSingleton(_clipboardService);
        Services.AddSingleton(_traceLogger);

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task BannerHost_CopyDetailsClicked_CopiesExceptionAndShowsCopiedChip()
    {
        var error = new InvalidOperationException("kaboom");
        _bannerService.UnhandledError.Returns(error);

        var component = Render<BannerHost>();

        // Sync Click() returns at the handler's first real async point (the 2s Task.Delay) with
        // the chip rendered; ClickAsync would block for the full delay until the chip clears.
        component.Find("aside.banner-error .banner-actions button:nth-child(3)").Click();

        await _clipboardService.Received(1)
            .CopyTextAsync(Arg.Is<string>(s => s.Contains("InvalidOperationException") && s.Contains("kaboom")));

        Assert.Single(component.FindAll("aside.banner-error .banner-feedback .banner-chip"));
    }

    [Fact]
    public async Task BannerHost_DismissCriticalClicked_CallsDismissCriticalWithEntryId()
    {
        var alert = new CriticalAlertEntry(Guid.NewGuid(), "Database", "Schema invalid", DateTime.UtcNow);
        _bannerService.CriticalAlerts.Returns([alert]);

        var component = Render<BannerHost>();
        await component.Find("aside.banner-critical button.banner-dismiss").ClickAsync(new());

        _bannerService.Received(1).DismissCritical(alert.Id);
    }

    [Fact]
    public async Task BannerHost_DismissInfoClicked_CallsDismissInfoBannerWithEntryId()
    {
        var info = new BannerInfoEntry(Guid.NewGuid(), "Notice", "Heads up", BannerSeverity.Info, DateTime.UtcNow);
        _bannerService.InfoBanners.Returns([info]);

        var component = Render<BannerHost>();
        await component.Find("aside.banner-info button.banner-dismiss").ClickAsync(new());

        _bannerService.Received(1).DismissInfoBanner(info.Id);
    }

    [Fact]
    public void BannerHost_ErrorAndCriticalAndInfoAllPresent_RendersOnlyError()
    {
        _bannerService.UnhandledError.Returns(new InvalidOperationException("kaboom"));

        _bannerService.CriticalAlerts.Returns(
            [new CriticalAlertEntry(Guid.NewGuid(), "Critical", "C", DateTime.UtcNow)]);

        _bannerService.InfoBanners.Returns([
            new BannerInfoEntry(Guid.NewGuid(), "Info", "I", BannerSeverity.Info, DateTime.UtcNow)
        ]);

        var component = Render<BannerHost>();

        Assert.Single(component.FindAll("aside.banner-error"));
        Assert.Empty(component.FindAll("aside.banner-critical"));
        Assert.Empty(component.FindAll("aside.banner-info"));
    }

    [Fact]
    public void BannerHost_InfoSeverity_RendersInfoStyledBanner()
    {
        var info = new BannerInfoEntry(Guid.NewGuid(), "Notice", "Heads up", BannerSeverity.Info, DateTime.UtcNow);
        _bannerService.InfoBanners.Returns([info]);

        var component = Render<BannerHost>();

        Assert.Single(component.FindAll("aside.banner.banner-info"));
        Assert.Empty(component.FindAll("aside.banner.banner-warning"));
        Assert.Contains("Notice: Heads up", component.Find("aside.banner-info").TextContent);
    }

    [Fact]
    public void BannerHost_MultipleCriticalAlerts_RendersFirstWithPagination()
    {
        var first = new CriticalAlertEntry(Guid.NewGuid(), "First", "First message", DateTime.UtcNow);
        var second = new CriticalAlertEntry(Guid.NewGuid(), "Second", "Second message", DateTime.UtcNow);
        _bannerService.CriticalAlerts.Returns([first, second]);

        var component = Render<BannerHost>();

        var banner = component.Find("aside.banner-critical");
        Assert.Contains("First: First message", banner.TextContent);
        Assert.DoesNotContain("Second", banner.TextContent);

        var pagination = component.Find("aside.banner-critical .banner-pagination");
        Assert.Equal("1 of 2", pagination.TextContent.Trim());
    }

    [Fact]
    public void BannerHost_NoState_RendersNothing()
    {
        var component = Render<BannerHost>();

        Assert.Equal(string.Empty, component.Markup.Trim());
    }

    [Fact]
    public async Task BannerHost_RecoveryThrows_ShowsRecoveryFailureSubtitle()
    {
        _bannerService.UnhandledError.Returns(new InvalidOperationException("kaboom"));
        _bannerService.TryRecoverAsync().Returns(Task.FromException(new InvalidOperationException("recovery failed")));

        var component = Render<BannerHost>();
        await component.Find("aside.banner-error .banner-actions button:nth-child(1)").ClickAsync(new());

        var subtitle = component.Find("aside.banner-error .banner-feedback .banner-subtitle");
        Assert.Contains("Recovery failed", subtitle.TextContent);
        Assert.Contains("recovery failed", subtitle.TextContent);
    }

    [Fact]
    public async Task BannerHost_RelaunchClicked_InvokesTryRestartAsync()
    {
        _bannerService.UnhandledError.Returns(new InvalidOperationException("kaboom"));
        _applicationRestartService.TryRestartAsync().Returns(true);

        var component = Render<BannerHost>();
        await component.Find("aside.banner-error .banner-actions button:nth-child(2)").ClickAsync(new());

        await _applicationRestartService.Received(1).TryRestartAsync();
    }

    [Fact]
    public async Task BannerHost_RelaunchFails_ShowsRestartFailureSubtitle()
    {
        _bannerService.UnhandledError.Returns(new InvalidOperationException("kaboom"));
        _applicationRestartService.TryRestartAsync().Returns(false);

        var component = Render<BannerHost>();
        await component.Find("aside.banner-error .banner-actions button:nth-child(2)").ClickAsync(new());

        var subtitle = component.Find("aside.banner-error .banner-feedback .banner-subtitle");
        Assert.Contains("Restart failed", subtitle.TextContent);
    }

    [Fact]
    public async Task BannerHost_ReloadClicked_InvokesTryRecoverAsync()
    {
        _bannerService.UnhandledError.Returns(new InvalidOperationException("kaboom"));
        _bannerService.TryRecoverAsync().Returns(Task.CompletedTask);

        var component = Render<BannerHost>();
        await component.Find("aside.banner-error .banner-actions button:nth-child(1)").ClickAsync(new());

        await _bannerService.Received(1).TryRecoverAsync();
    }

    [Fact]
    public void BannerHost_SingleCriticalAlert_RendersWithoutPagination()
    {
        var alert = new CriticalAlertEntry(Guid.NewGuid(), "Database", "Schema invalid", DateTime.UtcNow);
        _bannerService.CriticalAlerts.Returns([alert]);

        var component = Render<BannerHost>();

        var banner = component.Find("aside.banner-critical");
        Assert.Contains("Database: Schema invalid", banner.TextContent);
        Assert.Empty(component.FindAll("aside.banner-critical .banner-pagination"));
        Assert.Single(component.FindAll("aside.banner-critical button.banner-dismiss"));
    }

    [Fact]
    public void BannerHost_UnhandledError_RendersErrorBannerWithThreeButtons()
    {
        var error = new InvalidOperationException("kaboom");
        _bannerService.UnhandledError.Returns(error);

        var component = Render<BannerHost>();

        var banner = component.Find("aside.banner-error");
        Assert.Contains("InvalidOperationException", banner.TextContent);
        Assert.Contains("kaboom", banner.TextContent);

        var buttons = component.FindAll("aside.banner-error .banner-actions button");
        Assert.Equal(3, buttons.Count);
        Assert.Contains("Reload", buttons[0].TextContent);
        Assert.Contains("Relaunch", buttons[1].TextContent);
        Assert.Contains("Copy details", buttons[2].TextContent);
    }

    [Fact]
    public void BannerHost_WarningSeverity_RendersWarningStyledBanner()
    {
        var info = new BannerInfoEntry(Guid.NewGuid(),
            "Slow",
            "Performance dip",
            BannerSeverity.Warning,
            DateTime.UtcNow);

        _bannerService.InfoBanners.Returns([info]);

        var component = Render<BannerHost>();

        Assert.Single(component.FindAll("aside.banner.banner-warning"));
        Assert.Empty(component.FindAll("aside.banner.banner-info"));
    }
}
