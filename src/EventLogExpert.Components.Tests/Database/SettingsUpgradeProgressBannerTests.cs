// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Components.Database;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.Components.Tests.Database;

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
        // Arrange
        _bannerService.BackgroundProgress.Returns(BuildProgress(UpgradeProgressScope.Background));

        // Act
        var component = Render<SettingsUpgradeProgressBanner>();

        // Assert
        Assert.Empty(component.FindAll("aside.settings-upgrade-banner"));
    }

    [Fact]
    public async Task SettingsUpgradeProgressBanner_CancelClicked_InvokesProgressCancelDelegate()
    {
        // Arrange
        int cancelInvocationCount = 0;
        _bannerService.SettingsProgress.Returns(BuildProgress(cancel: () => cancelInvocationCount++));

        // Act
        var component = Render<SettingsUpgradeProgressBanner>();
        await component.Find("aside.settings-upgrade-banner button.banner-action").ClickAsync(new());

        // Assert
        Assert.Equal(1, cancelInvocationCount);
    }

    [Fact]
    public async Task SettingsUpgradeProgressBanner_CancelThrows_LogsViaTraceLoggerAndDoesNotPropagate()
    {
        // Arrange
        _bannerService.SettingsProgress.Returns(
            BuildProgress(cancel: () => throw new InvalidOperationException("cts disposed")));

        // Act
        var component = Render<SettingsUpgradeProgressBanner>();
        await component.Find("aside.settings-upgrade-banner button.banner-action").ClickAsync(new());

        // Assert
        Assert.Single(component.FindAll("aside.settings-upgrade-banner"));

        _traceLogger.Received(1).Error(Arg.Is<ErrorLogHandler>(h =>
            h.ToString().Contains(nameof(SettingsUpgradeProgressBanner)) &&
            h.ToString().Contains("cts disposed")));
    }

    [Fact]
    public void SettingsUpgradeProgressBanner_DisposeIsIdempotent()
    {
        // Arrange
        var component = Render<SettingsUpgradeProgressBanner>();
        var instance = component.Instance;

        // Act + Assert
        instance.Dispose();
        instance.Dispose();

    }

    [Fact]
    public async Task SettingsUpgradeProgressBanner_DisposeUnsubscribesFromStateChanged()
    {
        // Arrange
        _bannerService.SettingsProgress.Returns((BannerProgressEntry?)null);

        var component = Render<SettingsUpgradeProgressBanner>();
        component.Instance.Dispose();

        // Act
        _bannerService.SettingsProgress.Returns(BuildProgress());
        _bannerService.StateChanged += Raise.Event<Action>();

        await Task.Yield();

        // Assert
        Assert.Empty(component.FindAll("aside.settings-upgrade-banner"));
    }

    [Fact]
    public void SettingsUpgradeProgressBanner_QueuedBatchesAfterOne_UsesSingularBatchLabel()
    {
        // Arrange
        _bannerService.SettingsProgress.Returns(BuildProgress(queuedBatchesAfter: 1));

        // Act
        var component = Render<SettingsUpgradeProgressBanner>();

        // Assert
        var subtitle = component.Find("aside.settings-upgrade-banner .banner-subtitle");
        Assert.Contains("+1 batch queued", subtitle.TextContent);
        Assert.DoesNotContain("batches", subtitle.TextContent);
    }

    [Fact]
    public void SettingsUpgradeProgressBanner_QueuedBatchesAfterTwo_UsesPluralBatchesLabel()
    {
        // Arrange
        _bannerService.SettingsProgress.Returns(BuildProgress(queuedBatchesAfter: 2));

        // Act
        var component = Render<SettingsUpgradeProgressBanner>();

        // Assert
        var subtitle = component.Find("aside.settings-upgrade-banner .banner-subtitle");
        Assert.Contains("+2 batches queued", subtitle.TextContent);
    }

    [Fact]
    public void SettingsUpgradeProgressBanner_QueuedBatchesAfterZero_DoesNotRenderSubtitle()
    {
        // Arrange
        _bannerService.SettingsProgress.Returns(BuildProgress(queuedBatchesAfter: 0));

        // Act
        var component = Render<SettingsUpgradeProgressBanner>();

        // Assert
        Assert.Empty(component.FindAll("aside.settings-upgrade-banner .banner-subtitle"));
    }

    [Fact]
    public void SettingsUpgradeProgressBanner_SettingsProgressNull_RendersNothing()
    {
        // Arrange
        _bannerService.SettingsProgress.Returns((BannerProgressEntry?)null);

        // Act
        var component = Render<SettingsUpgradeProgressBanner>();

        // Assert
        Assert.Empty(component.FindAll("aside.settings-upgrade-banner"));
    }

    [Fact]
    public void SettingsUpgradeProgressBanner_SettingsProgressWithBatchSizeOne_UsesSingularDatabaseLabel()
    {
        // Arrange
        _bannerService.SettingsProgress.Returns(
            BuildProgress(currentBatchSize: 1, currentEntryName: string.Empty));

        // Act
        var component = Render<SettingsUpgradeProgressBanner>();

        // Assert
        var banner = component.Find("aside.settings-upgrade-banner");
        Assert.Contains("Preparing upgrade of 1 database", banner.TextContent);
        Assert.DoesNotContain("databases", banner.TextContent);
    }

    [Fact]
    public void SettingsUpgradeProgressBanner_SettingsProgressWithEmptyEntryName_RendersPreparingMessage()
    {
        // Arrange
        _bannerService.SettingsProgress.Returns(
            BuildProgress(currentBatchSize: 3, currentEntryName: string.Empty));

        // Act
        var component = Render<SettingsUpgradeProgressBanner>();

        // Assert
        var banner = component.Find("aside.settings-upgrade-banner");
        Assert.Contains("Preparing upgrade of 3 databases", banner.TextContent);
        Assert.DoesNotContain("Upgrading database 0", banner.TextContent);
    }

    [Fact]
    public void
        SettingsUpgradeProgressBanner_SettingsProgressWithEntryName_RendersUpgradeProgressBannerWithCancelButton()
    {
        // Arrange
        _bannerService.SettingsProgress.Returns(BuildProgress(
            currentBatchPosition: 2,
            currentBatchSize: 5,
            currentEntryName: "MyDb.evtx",
            currentPhase: UpgradePhase.MigratingSchema));

        // Act
        var component = Render<SettingsUpgradeProgressBanner>();

        // Assert
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
        // Arrange
        _bannerService.SettingsProgress.Returns((BannerProgressEntry?)null);
        var component = Render<SettingsUpgradeProgressBanner>();
        Assert.Empty(component.FindAll("aside.settings-upgrade-banner"));

        // Act
        _bannerService.SettingsProgress.Returns(BuildProgress(currentEntryName: "x.evtx"));
        _bannerService.StateChanged += Raise.Event<Action>();

        // Assert
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
