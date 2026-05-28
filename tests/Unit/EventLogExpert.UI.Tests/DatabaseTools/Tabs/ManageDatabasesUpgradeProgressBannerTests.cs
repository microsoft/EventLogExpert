// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Database.Upgrade;
using EventLogExpert.UI.DatabaseTools.Tabs;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.DatabaseTools.Tabs;

public sealed class ManageDatabasesUpgradeProgressBannerTests : BunitContext
{
    private readonly IProgressBannerService _progressBannerService = Substitute.For<IProgressBannerService>();
    private readonly ITraceLogger _traceLogger = Substitute.For<ITraceLogger>();

    public ManageDatabasesUpgradeProgressBannerTests()
    {
        _progressBannerService.ManageDatabasesProgress.Returns((BannerProgressEntry?)null);
        _progressBannerService.BackgroundProgress.Returns((BannerProgressEntry?)null);

        Services.AddSingleton(_progressBannerService);
        Services.AddSingleton(_traceLogger);

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void ManageDatabasesUpgradeProgressBanner_BackgroundProgressOnly_RendersNothing()
    {
        // Arrange
        _progressBannerService.BackgroundProgress.Returns(BuildProgress(UpgradeProgressScope.Background));

        // Act
        var component = Render<ManageDatabasesUpgradeProgressBanner>();

        // Assert
        Assert.Empty(component.FindAll("aside.manage-databases-upgrade-banner"));
    }

    [Fact]
    public async Task ManageDatabasesUpgradeProgressBanner_CancelClicked_InvokesProgressCancelDelegate()
    {
        // Arrange
        int cancelInvocationCount = 0;
        _progressBannerService.ManageDatabasesProgress.Returns(BuildProgress(cancel: () => cancelInvocationCount++));

        // Act
        var component = Render<ManageDatabasesUpgradeProgressBanner>();
        await component.Find("aside.manage-databases-upgrade-banner button.banner-action").ClickAsync(new MouseEventArgs());

        // Assert
        Assert.Equal(1, cancelInvocationCount);
    }

    [Fact]
    public async Task ManageDatabasesUpgradeProgressBanner_CancelThrows_LogsViaTraceLoggerAndDoesNotPropagate()
    {
        // Arrange
        _progressBannerService.ManageDatabasesProgress.Returns(
            BuildProgress(cancel: () => throw new InvalidOperationException("cts disposed")));

        // Act
        var component = Render<ManageDatabasesUpgradeProgressBanner>();
        await component.Find("aside.manage-databases-upgrade-banner button.banner-action").ClickAsync(new MouseEventArgs());

        // Assert
        Assert.Single(component.FindAll("aside.manage-databases-upgrade-banner"));

        _traceLogger.Received(1).Error(Arg.Is<ErrorLogHandler>(h =>
            h.ToString().Contains(nameof(ManageDatabasesUpgradeProgressBanner)) &&
            h.ToString().Contains("cts disposed")));
    }

    [Fact]
    public void ManageDatabasesUpgradeProgressBanner_DisposeIsIdempotent()
    {
        // Arrange
        var component = Render<ManageDatabasesUpgradeProgressBanner>();
        var instance = component.Instance;
        instance.Dispose();

        // Act + Assert - second Dispose must not throw
        var exception = Record.Exception((Action)(() => instance.Dispose()));
        Assert.Null(exception);
    }

    [Fact]
    public async Task ManageDatabasesUpgradeProgressBanner_DisposeUnsubscribesFromStateChanged()
    {
        // Arrange
        _progressBannerService.ManageDatabasesProgress.Returns((BannerProgressEntry?)null);

        var component = Render<ManageDatabasesUpgradeProgressBanner>();
        component.Instance.Dispose();

        // Act
        _progressBannerService.ManageDatabasesProgress.Returns(BuildProgress());
        _progressBannerService.StateChanged += Raise.Event<Action>();

        await Task.Yield();

        // Assert
        Assert.Empty(component.FindAll("aside.manage-databases-upgrade-banner"));
    }

    [Fact]
    public void ManageDatabasesUpgradeProgressBanner_ManageDatabasesProgressNull_RendersNothing()
    {
        // Arrange
        _progressBannerService.ManageDatabasesProgress.Returns((BannerProgressEntry?)null);

        // Act
        var component = Render<ManageDatabasesUpgradeProgressBanner>();

        // Assert
        Assert.Empty(component.FindAll("aside.manage-databases-upgrade-banner"));
    }

    [Fact]
    public void ManageDatabasesUpgradeProgressBanner_ManageDatabasesProgressWithBatchSizeOne_UsesSingularDatabaseLabel()
    {
        // Arrange
        _progressBannerService.ManageDatabasesProgress.Returns(
            BuildProgress(currentBatchSize: 1, currentEntryName: string.Empty));

        // Act
        var component = Render<ManageDatabasesUpgradeProgressBanner>();

        // Assert
        var banner = component.Find("aside.manage-databases-upgrade-banner");
        Assert.Contains("Preparing upgrade of 1 database", banner.TextContent);
        Assert.DoesNotContain("databases", banner.TextContent);
    }

    [Fact]
    public void ManageDatabasesUpgradeProgressBanner_ManageDatabasesProgressWithEmptyEntryName_RendersPreparingMessage()
    {
        // Arrange
        _progressBannerService.ManageDatabasesProgress.Returns(
            BuildProgress(currentBatchSize: 3, currentEntryName: string.Empty));

        // Act
        var component = Render<ManageDatabasesUpgradeProgressBanner>();

        // Assert
        var banner = component.Find("aside.manage-databases-upgrade-banner");
        Assert.Contains("Preparing upgrade of 3 databases", banner.TextContent);
        Assert.DoesNotContain("Upgrading database 0", banner.TextContent);
    }

    [Fact]
    public void
        ManageDatabasesUpgradeProgressBanner_ManageDatabasesProgressWithEntryName_RendersUpgradeProgressBannerWithCancelButton()
    {
        // Arrange
        _progressBannerService.ManageDatabasesProgress.Returns(BuildProgress(
            currentBatchPosition: 2,
            currentBatchSize: 5,
            currentEntryName: "MyDb.evtx",
            currentPhase: UpgradePhase.MigratingSchema));

        // Act
        var component = Render<ManageDatabasesUpgradeProgressBanner>();

        // Assert — R5: banner now renders the per-phase verb prefix (e.g. "Migrating schema") instead
        // of the raw enum suffix; spinner CSS class moved to .manage-status-banner__spinner (shared).
        var banner = component.Find("aside.manage-databases-upgrade-banner");
        Assert.Contains("Migrating schema database 2 of 5", banner.TextContent);
        Assert.Contains("MyDb.evtx", banner.TextContent);
        Assert.DoesNotContain("MigratingSchema", banner.TextContent);
        Assert.Equal("Cancel", component.Find("aside.manage-databases-upgrade-banner button.banner-action").TextContent.Trim());
        Assert.Single(component.FindAll("aside.manage-databases-upgrade-banner .manage-status-banner__spinner"));
    }

    [Fact]
    public void ManageDatabasesUpgradeProgressBanner_QueuedBatchesAfterOne_UsesSingularBatchLabel()
    {
        // Arrange
        _progressBannerService.ManageDatabasesProgress.Returns(BuildProgress(queuedBatchesAfter: 1));

        // Act
        var component = Render<ManageDatabasesUpgradeProgressBanner>();

        // Assert
        var subtitle = component.Find("aside.manage-databases-upgrade-banner .banner-subtitle");
        Assert.Contains("+1 batch queued", subtitle.TextContent);
        Assert.DoesNotContain("batches", subtitle.TextContent);
    }

    [Fact]
    public void ManageDatabasesUpgradeProgressBanner_QueuedBatchesAfterTwo_UsesPluralBatchesLabel()
    {
        // Arrange
        _progressBannerService.ManageDatabasesProgress.Returns(BuildProgress(queuedBatchesAfter: 2));

        // Act
        var component = Render<ManageDatabasesUpgradeProgressBanner>();

        // Assert
        var subtitle = component.Find("aside.manage-databases-upgrade-banner .banner-subtitle");
        Assert.Contains("+2 batches queued", subtitle.TextContent);
    }

    [Fact]
    public void ManageDatabasesUpgradeProgressBanner_QueuedBatchesAfterZero_DoesNotRenderSubtitle()
    {
        // Arrange
        _progressBannerService.ManageDatabasesProgress.Returns(BuildProgress(queuedBatchesAfter: 0));

        // Act
        var component = Render<ManageDatabasesUpgradeProgressBanner>();

        // Assert
        Assert.Empty(component.FindAll("aside.manage-databases-upgrade-banner .banner-subtitle"));
    }

    [Fact]
    public async Task ManageDatabasesUpgradeProgressBanner_StateChangedRaised_RerendersWithNewProgress()
    {
        // Arrange
        _progressBannerService.ManageDatabasesProgress.Returns((BannerProgressEntry?)null);
        var component = Render<ManageDatabasesUpgradeProgressBanner>();
        Assert.Empty(component.FindAll("aside.manage-databases-upgrade-banner"));

        // Act
        _progressBannerService.ManageDatabasesProgress.Returns(BuildProgress(currentEntryName: "x.evtx"));
        _progressBannerService.StateChanged += Raise.Event<Action>();

        // Assert
        await component.WaitForAssertionAsync(() =>
            Assert.Single(component.FindAll("aside.manage-databases-upgrade-banner")));
    }

    [Theory]
    [InlineData(UpgradePhase.BackingUp, "Backing up")]
    [InlineData(UpgradePhase.MigratingSchema, "Migrating schema")]
    [InlineData(UpgradePhase.Verifying, "Verifying")]
    public void ManageDatabasesUpgradeProgressBanner_VerbPrefixPerUpgradePhase(UpgradePhase phase, string expectedVerb)
    {
        // Arrange — c3.1(b): each UpgradePhase enum value maps to a distinct user-facing verb.
        _progressBannerService.ManageDatabasesProgress.Returns(BuildProgress(
            currentBatchPosition: 1,
            currentBatchSize: 1,
            currentEntryName: "x.evtx",
            currentPhase: phase));

        // Act
        var component = Render<ManageDatabasesUpgradeProgressBanner>();

        // Assert
        var banner = component.Find("aside.manage-databases-upgrade-banner");
        Assert.Contains($"{expectedVerb} database", banner.TextContent);
    }

    private static BannerProgressEntry BuildProgress(
        UpgradeProgressScope scope = UpgradeProgressScope.ManageDatabasesTriggered,
        int currentBatchPosition = 1,
        int currentBatchSize = 1,
        string currentEntryName = "x.evtx",
        UpgradePhase currentPhase = UpgradePhase.MigratingSchema,
        int queuedBatchesAfter = 0,
        Action? cancel = null) =>
        new(
            UpgradeBatchId.Create(),
            scope,
            currentBatchPosition,
            currentBatchSize,
            currentEntryName,
            currentPhase,
            queuedBatchesAfter,
            cancel ?? (() => { }));
}
