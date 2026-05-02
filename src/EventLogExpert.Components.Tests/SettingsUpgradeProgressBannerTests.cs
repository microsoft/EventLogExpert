// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.Components.Tests;

public sealed class SettingsUpgradeProgressBannerTests : BunitContext
{
    private readonly IBannerService _bannerService = Substitute.For<IBannerService>();
    private readonly ITraceLogger _traceLogger = Substitute.For<ITraceLogger>();

    public SettingsUpgradeProgressBannerTests()
    {
        _bannerService.SettingsProgress.Returns((BannerProgressEntry?)null);
        _bannerService.BackgroundProgress.Returns((BannerProgressEntry?)null);

        Services.AddSingleton(_bannerService);
        Services.AddSingleton(_traceLogger);

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void SettingsUpgradeProgressBanner_BackgroundProgressOnly_RendersNothing()
    {
        // The inline banner observes SettingsProgress only. A background-scope batch must not surface here
        // (top-level BannerHost owns that slot).
        _bannerService.BackgroundProgress.Returns(BuildProgress(UpgradeProgressScope.Background));

        var component = Render<SettingsUpgradeProgressBanner>();

        Assert.Empty(component.FindAll("aside.settings-upgrade-banner"));
    }

    [Fact]
    public async Task SettingsUpgradeProgressBanner_CancelClicked_InvokesProgressCancelDelegate()
    {
        int cancelInvocationCount = 0;
        _bannerService.SettingsProgress.Returns(BuildProgress(cancel: () => cancelInvocationCount++));

        var component = Render<SettingsUpgradeProgressBanner>();
        await component.Find("aside.settings-upgrade-banner button.banner-action").ClickAsync(new());

        Assert.Equal(1, cancelInvocationCount);
    }

    [Fact]
    public async Task SettingsUpgradeProgressBanner_CancelThrows_LogsViaTraceLoggerAndDoesNotPropagate()
    {
        _bannerService.SettingsProgress.Returns(
            BuildProgress(cancel: () => throw new InvalidOperationException("cts disposed")));

        var component = Render<SettingsUpgradeProgressBanner>();
        await component.Find("aside.settings-upgrade-banner button.banner-action").ClickAsync(new());

        Assert.Single(component.FindAll("aside.settings-upgrade-banner"));

        _traceLogger.Received(1).Error(Arg.Is<ErrorLogHandler>(h =>
            h.ToString().Contains(nameof(SettingsUpgradeProgressBanner)) &&
            h.ToString().Contains("cts disposed")));
    }

    [Fact]
    public void SettingsUpgradeProgressBanner_DisposeIsIdempotent()
    {
        var component = Render<SettingsUpgradeProgressBanner>();
        var instance = component.Instance;

        instance.Dispose();
        instance.Dispose();

        // Two disposals must not unsubscribe twice (handler delta would go negative).
        // Verified by the fact that no exception bubbles out and the component stays alive.
    }

    [Fact]
    public async Task SettingsUpgradeProgressBanner_DisposeUnsubscribesFromStateChanged()
    {
        _bannerService.SettingsProgress.Returns((BannerProgressEntry?)null);

        var component = Render<SettingsUpgradeProgressBanner>();
        component.Instance.Dispose();

        // After Dispose, raising StateChanged with a now-non-null SettingsProgress must not re-render.
        _bannerService.SettingsProgress.Returns(BuildProgress());
        _bannerService.StateChanged += Raise.Event<Action>();

        await Task.Yield();

        Assert.Empty(component.FindAll("aside.settings-upgrade-banner"));
    }

    [Fact]
    public void SettingsUpgradeProgressBanner_QueuedBatchesAfterOne_UsesSingularBatchLabel()
    {
        _bannerService.SettingsProgress.Returns(BuildProgress(queuedBatchesAfter: 1));

        var component = Render<SettingsUpgradeProgressBanner>();

        var subtitle = component.Find("aside.settings-upgrade-banner .banner-subtitle");
        Assert.Contains("+1 batch queued", subtitle.TextContent);
        Assert.DoesNotContain("batches", subtitle.TextContent);
    }

    [Fact]
    public void SettingsUpgradeProgressBanner_QueuedBatchesAfterTwo_UsesPluralBatchesLabel()
    {
        _bannerService.SettingsProgress.Returns(BuildProgress(queuedBatchesAfter: 2));

        var component = Render<SettingsUpgradeProgressBanner>();

        var subtitle = component.Find("aside.settings-upgrade-banner .banner-subtitle");
        Assert.Contains("+2 batches queued", subtitle.TextContent);
    }

    [Fact]
    public void SettingsUpgradeProgressBanner_QueuedBatchesAfterZero_DoesNotRenderSubtitle()
    {
        _bannerService.SettingsProgress.Returns(BuildProgress(queuedBatchesAfter: 0));

        var component = Render<SettingsUpgradeProgressBanner>();

        Assert.Empty(component.FindAll("aside.settings-upgrade-banner .banner-subtitle"));
    }

    [Fact]
    public void SettingsUpgradeProgressBanner_SettingsProgressNull_RendersNothing()
    {
        _bannerService.SettingsProgress.Returns((BannerProgressEntry?)null);

        var component = Render<SettingsUpgradeProgressBanner>();

        Assert.Empty(component.FindAll("aside.settings-upgrade-banner"));
    }

    [Fact]
    public void SettingsUpgradeProgressBanner_SettingsProgressWithBatchSizeOne_UsesSingularDatabaseLabel()
    {
        // Pre-first-tick rendering: empty CurrentEntryName triggers the "Preparing..." string.
        _bannerService.SettingsProgress.Returns(
            BuildProgress(currentBatchSize: 1, currentEntryName: string.Empty));

        var component = Render<SettingsUpgradeProgressBanner>();

        var banner = component.Find("aside.settings-upgrade-banner");
        Assert.Contains("Preparing upgrade of 1 database", banner.TextContent);
        Assert.DoesNotContain("databases", banner.TextContent);
    }

    [Fact]
    public void SettingsUpgradeProgressBanner_SettingsProgressWithEmptyEntryName_RendersPreparingMessage()
    {
        _bannerService.SettingsProgress.Returns(
            BuildProgress(currentBatchSize: 3, currentEntryName: string.Empty));

        var component = Render<SettingsUpgradeProgressBanner>();

        var banner = component.Find("aside.settings-upgrade-banner");
        Assert.Contains("Preparing upgrade of 3 databases", banner.TextContent);
        Assert.DoesNotContain("Upgrading database 0", banner.TextContent);
    }

    [Fact]
    public void
        SettingsUpgradeProgressBanner_SettingsProgressWithEntryName_RendersUpgradeProgressBannerWithCancelButton()
    {
        _bannerService.SettingsProgress.Returns(BuildProgress(
            currentBatchPosition: 2,
            currentBatchSize: 5,
            currentEntryName: "MyDb.evtx",
            currentPhase: UpgradePhase.MigratingSchema));

        var component = Render<SettingsUpgradeProgressBanner>();

        var banner = component.Find("aside.settings-upgrade-banner");
        Assert.Contains("Upgrading database 2 of 5", banner.TextContent);
        Assert.Contains("MyDb.evtx", banner.TextContent);
        Assert.Contains("MigratingSchema", banner.TextContent);
        Assert.Equal("Cancel", component.Find("aside.settings-upgrade-banner button.banner-action").TextContent.Trim());
        Assert.Single(component.FindAll("aside.settings-upgrade-banner .banner-spinner"));
    }

    [Fact]
    public async Task SettingsUpgradeProgressBanner_StateChangedRaised_RerendersWithNewProgress()
    {
        _bannerService.SettingsProgress.Returns((BannerProgressEntry?)null);
        var component = Render<SettingsUpgradeProgressBanner>();
        Assert.Empty(component.FindAll("aside.settings-upgrade-banner"));

        _bannerService.SettingsProgress.Returns(BuildProgress(currentEntryName: "x.evtx"));
        _bannerService.StateChanged += Raise.Event<Action>();

        await component.WaitForAssertionAsync(() =>
            Assert.Single(component.FindAll("aside.settings-upgrade-banner")));
    }

    private static BannerProgressEntry BuildProgress(
        UpgradeProgressScope scope = UpgradeProgressScope.SettingsTriggered,
        int currentBatchPosition = 1,
        int currentBatchSize = 1,
        string currentEntryName = "x.evtx",
        UpgradePhase currentPhase = UpgradePhase.MigratingSchema,
        int queuedBatchesAfter = 0,
        Action? cancel = null) =>
        new(
            Guid.NewGuid(),
            scope,
            currentBatchPosition,
            currentBatchSize,
            currentEntryName,
            currentPhase,
            queuedBatchesAfter,
            cancel ?? (() => { }));
}
