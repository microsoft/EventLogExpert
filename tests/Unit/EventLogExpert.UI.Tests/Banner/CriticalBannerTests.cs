// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Localization;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Common.Restart;
using EventLogExpert.UI.Banner;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Banner;

public sealed class CriticalBannerTests : BunitContext
{
    private readonly IApplicationRestartService _applicationRestartService =
        Substitute.For<IApplicationRestartService>();
    private readonly IClipboardService _clipboardService = Substitute.For<IClipboardService>();
    private readonly ICriticalErrorService _criticalErrorService = Substitute.For<ICriticalErrorService>();
    private readonly ITraceLogger _traceLogger = Substitute.For<ITraceLogger>();

    public CriticalBannerTests()
    {
        Services.AddSingleton(_criticalErrorService);
        Services.AddSingleton(_applicationRestartService);
        Services.AddSingleton(_clipboardService);
        Services.AddSingleton(_traceLogger);
        Services.AddSingleton<IStringLocalizer<SharedResource>>(new MarkerLocalizer());

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task CriticalBanner_CopyDetailsClicked_CopiesExceptionAndShowsCopiedChip()
    {
        var critical = new InvalidOperationException("kaboom");
        _criticalErrorService.CurrentCritical.Returns(critical);

        var component = Render<CriticalBanner>(p => p.Add(c => c.Critical, critical));

        // Sync Click() returns at the handler's first real async point (the 2s Task.Delay) with
        // the chip rendered; ClickAsync would block for the full delay until the chip clears.
        component.FindAll("aside.banner-critical .banner-actions button")[2].Click();

        await _clipboardService.Received(1)
            .CopyTextAsync(Arg.Is<string>(s => s != null && s.Contains("InvalidOperationException") && s.Contains("kaboom")));

        Assert.Single(component.FindAll("aside.banner-critical .banner-feedback .banner-chip"));
    }

    [Fact]
    public async Task CriticalBanner_RecoveryThrows_ShowsRecoveryFailureSubtitle()
    {
        var critical = new InvalidOperationException("kaboom");
        _criticalErrorService.CurrentCritical.Returns(critical);
        _criticalErrorService.TryRecoverAsync().Returns(Task.FromException(new InvalidOperationException("recovery failed")));

        var component = Render<CriticalBanner>(p => p.Add(c => c.Critical, critical));
        await component.FindAll("aside.banner-critical .banner-actions button")[0].ClickAsync(new MouseEventArgs());

        var subtitle = component.Find("aside.banner-critical .banner-feedback .banner-subtitle");
        Assert.Contains("[[Banner_Critical_RecoveryFailed(recovery failed)]]", subtitle.TextContent);
    }

    [Fact]
    public async Task CriticalBanner_RelaunchClicked_InvokesTryRestartAsync()
    {
        var critical = new InvalidOperationException("kaboom");
        _criticalErrorService.CurrentCritical.Returns(critical);
        _applicationRestartService.TryRestartAsync().Returns(true);

        var component = Render<CriticalBanner>(p => p.Add(c => c.Critical, critical));
        await component.FindAll("aside.banner-critical .banner-actions button")[1].ClickAsync(new MouseEventArgs());

        await _applicationRestartService.Received(1).TryRestartAsync();
    }

    [Fact]
    public async Task CriticalBanner_RelaunchFails_ShowsRestartFailureSubtitle()
    {
        var critical = new InvalidOperationException("kaboom");
        _criticalErrorService.CurrentCritical.Returns(critical);
        _applicationRestartService.TryRestartAsync().Returns(false);

        var component = Render<CriticalBanner>(p => p.Add(c => c.Critical, critical));
        await component.FindAll("aside.banner-critical .banner-actions button")[1].ClickAsync(new MouseEventArgs());

        var subtitle = component.Find("aside.banner-critical .banner-feedback .banner-subtitle");
        Assert.Contains("[[Banner_Critical_RestartFailed]]", subtitle.TextContent);
    }

    [Fact]
    public async Task CriticalBanner_ReloadClicked_InvokesTryRecoverAsync()
    {
        var critical = new InvalidOperationException("kaboom");
        _criticalErrorService.CurrentCritical.Returns(critical);
        _criticalErrorService.TryRecoverAsync().Returns(Task.CompletedTask);

        var component = Render<CriticalBanner>(p => p.Add(c => c.Critical, critical));
        await component.FindAll("aside.banner-critical .banner-actions button")[0].ClickAsync(new MouseEventArgs());

        await _criticalErrorService.Received(1).TryRecoverAsync();
    }

    [Fact]
    public void CriticalBanner_RendersWithThreeButtons()
    {
        var critical = new InvalidOperationException("kaboom");
        _criticalErrorService.CurrentCritical.Returns(critical);

        var component = Render<CriticalBanner>(p => p.Add(c => c.Critical, critical));

        var banner = component.Find("aside.banner-critical");
        Assert.Contains("[[Banner_Critical_Unexpected(InvalidOperationException|kaboom)]]", banner.TextContent);

        var buttons = component.FindAll("aside.banner-critical .banner-actions button");
        Assert.Equal(3, buttons.Count);
        Assert.Contains("[[Banner_Critical_Reload]]", buttons[0].TextContent);
        Assert.Contains("[[Banner_Critical_Relaunch]]", buttons[1].TextContent);
        Assert.Contains("[[Banner_Critical_CopyDetails]]", buttons[2].TextContent);
    }
}
