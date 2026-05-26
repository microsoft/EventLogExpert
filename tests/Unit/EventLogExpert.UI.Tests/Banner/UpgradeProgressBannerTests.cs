// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Database.Upgrade;
using EventLogExpert.UI.Banner;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Banner;

public sealed class UpgradeProgressBannerTests : BunitContext
{
    private readonly ITraceLogger _traceLogger = Substitute.For<ITraceLogger>();

    public UpgradeProgressBannerTests()
    {
        Services.AddSingleton(_traceLogger);

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task UpgradeProgressBanner_CancelClicked_InvokesCancelDelegate()
    {
        int cancelInvocationCount = 0;
        var entry = new BannerProgressEntry(
            UpgradeBatchId.Create(),
            UpgradeProgressScope.Background,
            1,
            1,
            "x.evtx",
            UpgradePhase.MigratingSchema,
            0,
            () => cancelInvocationCount++);

        var component = Render<UpgradeProgressBanner>(p => p.Add(c => c.Progress, entry));
        await component.Find("aside.banner-upgrade-progress button.banner-action").ClickAsync(new MouseEventArgs());

        Assert.Equal(1, cancelInvocationCount);
    }

    [Fact]
    public async Task UpgradeProgressBanner_CancelThrows_LogsViaTraceLogger_DoesNotPropagate()
    {
        var entry = new BannerProgressEntry(
            UpgradeBatchId.Create(),
            UpgradeProgressScope.Background,
            1,
            1,
            "x.evtx",
            UpgradePhase.MigratingSchema,
            0,
            () => throw new InvalidOperationException("cts disposed"));

        var component = Render<UpgradeProgressBanner>(p => p.Add(c => c.Progress, entry));
        await component.Find("aside.banner-upgrade-progress button.banner-action").ClickAsync(new MouseEventArgs());

        Assert.Single(component.FindAll("aside.banner-upgrade-progress"));
        _traceLogger.Received(1).Error(Arg.Is<ErrorLogHandler>(h =>
            h.ToString().StartsWith($"{nameof(UpgradeProgressBanner)}.OnCancelUpgradeClickedAsync: threw:", StringComparison.Ordinal) &&
            h.ToString().Contains("cts disposed", StringComparison.Ordinal)));
    }

    [Fact]
    public void UpgradeProgressBanner_WithEmptyEntryName_RendersPreparingMessage()
    {
        // Pre-first-tick rendering: BannerService creates the entry with Position=0/EntryName="" before the
        // first per-entry progress event arrives. The "Preparing..." text avoids the misleading
        // "Upgrading database 0 of N: " string in that gap.
        var entry = new BannerProgressEntry(
            UpgradeBatchId.Create(),
            UpgradeProgressScope.Background,
            0,
            3,
            string.Empty,
            UpgradePhase.BackingUp,
            0,
            () => { });

        var component = Render<UpgradeProgressBanner>(p => p.Add(c => c.Progress, entry));

        var banner = component.Find("aside.banner-upgrade-progress");
        Assert.Contains("Preparing upgrade of 3 databases", banner.TextContent);
        Assert.DoesNotContain("Upgrading database 0", banner.TextContent);
    }

    [Fact]
    public void UpgradeProgressBanner_WithEntryName_RendersWithCancelButton()
    {
        var entry = new BannerProgressEntry(
            UpgradeBatchId.Create(),
            UpgradeProgressScope.Background,
            2,
            5,
            "MyDb.evtx",
            UpgradePhase.MigratingSchema,
            0,
            () => { });

        var component = Render<UpgradeProgressBanner>(p => p.Add(c => c.Progress, entry));

        var banner = component.Find("aside.banner-upgrade-progress");
        Assert.Contains("Upgrading database 2 of 5", banner.TextContent);
        Assert.Contains("MyDb.evtx", banner.TextContent);
        Assert.Contains("MigratingSchema", banner.TextContent);
        Assert.Equal("Cancel", component.Find("aside.banner-upgrade-progress button.banner-action").TextContent.Trim());
        Assert.Single(component.FindAll("aside.banner-upgrade-progress .banner-spinner"));
    }

    [Fact]
    public void UpgradeProgressBanner_WithQueuedBatches_RendersQueuedBatchesSubtitle()
    {
        var entry = new BannerProgressEntry(
            UpgradeBatchId.Create(),
            UpgradeProgressScope.Background,
            1,
            2,
            "x.evtx",
            UpgradePhase.Verifying,
            3,
            () => { });

        var component = Render<UpgradeProgressBanner>(p => p.Add(c => c.Progress, entry));

        var subtitle = component.Find("aside.banner-upgrade-progress .banner-subtitle");
        Assert.Contains("+3 batches queued", subtitle.TextContent);
    }
}
