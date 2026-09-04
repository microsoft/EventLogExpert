// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Localization;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Common.Restart;
using EventLogExpert.Runtime.Database.Upgrade;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.UI.Banner;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.Modal;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Banner;

public sealed class BannerLocalizationTests : BunitContext
{
    private static readonly DateTime s_createdUtc = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private readonly IApplicationRestartService _applicationRestartService = Substitute.For<IApplicationRestartService>();
    private readonly IAttentionBannerService _attentionBannerService;
    private readonly IClipboardService _clipboardService = Substitute.For<IClipboardService>();
    private readonly ICriticalErrorService _criticalErrorService;
    private readonly IErrorBannerService _errorBannerService;
    private readonly IExportProgressBannerService _exportProgressBannerService;
    private readonly IInfoBannerService _infoBannerService;
    private readonly IMenuActionService _menuActionService = Substitute.For<IMenuActionService>();
    private readonly IModalCoordinator _modalCoordinator = Substitute.For<IModalCoordinator>();
    private readonly IProgressBannerService _progressBannerService;
    private readonly ITraceLogger _traceLogger = Substitute.For<ITraceLogger>();

    public BannerLocalizationTests()
    {
        Services.AddBannerSubstitutes(
            out _attentionBannerService,
            out _progressBannerService,
            out _exportProgressBannerService,
            out _criticalErrorService,
            out _errorBannerService,
            out _infoBannerService);
        Services.AddSingleton<IBannerCycleStateService, BannerCycleStateService>();
        Services.AddSingleton<IStringLocalizer<SharedResource>>(new MarkerLocalizer());
        Services.AddSingleton(_applicationRestartService);
        Services.AddSingleton(_clipboardService);
        Services.AddSingleton(_menuActionService);
        Services.AddSingleton(_modalCoordinator);
        Services.AddSingleton(_traceLogger);

        _attentionBannerService.AttentionEntries.Returns([]);
        _attentionBannerService.AttentionDismissed.Returns(false);
        _criticalErrorService.CurrentCritical.Returns((Exception?)null);
        _errorBannerService.ErrorBanners.Returns([]);
        _exportProgressBannerService.CurrentExport.Returns((ExportProgressEntry?)null);
        _infoBannerService.InfoBanners.Returns([]);
        _modalCoordinator.ActiveSession.Returns((ModalSession?)null);
        _progressBannerService.BackgroundProgress.Returns((BannerProgressEntry?)null);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void AttentionBanner_RoutesOneAndManyMessagesAndChromeThroughLocalizer()
    {
        var one = Render<AttentionBanner>(parameters => parameters.Add(component => component.AttentionCount, 1));
        var many = Render<AttentionBanner>(parameters => parameters.Add(component => component.AttentionCount, 2));

        Assert.Equal("[[Banner_Attention_One(1)]]", one.Find(".banner-message").TextContent.Trim());
        Assert.Equal("[[Banner_Attention_Many(2)]]", many.Find(".banner-message").TextContent.Trim());
        Assert.Equal("[[Banner_Attention_OpenDatabases]]", many.Find("button.banner-action").TextContent.Trim());
        Assert.Equal("[[Banner_Attention_DismissAria]]", many.Find("button.banner-dismiss").GetAttribute("aria-label"));
    }

    [Fact]
    public async Task AttentionBanner_WhenOpenReturnsFalse_PostsLocalizedFallbackError()
    {
        BannerId errorId = BannerId.Create();
        BannerCycleItem? postedItem = null;
        _menuActionService.OpenDatabaseToolsAsync().Returns(Task.FromResult(false));
        _errorBannerService.ReportError("[[Banner_Attention_ErrorTitle]]", "[[Banner_Attention_OpenFailed]]")
            .Returns(errorId);

        var component = Render<AttentionBanner>(parameters => parameters
            .Add(banner => banner.AttentionCount, 1)
            .Add(
                banner => banner.OnFallbackErrorPosted,
                EventCallback.Factory.Create<BannerCycleItem>(this, item => postedItem = item)));

        await component.Find("button.banner-action").ClickAsync(new MouseEventArgs());

        _errorBannerService.Received(1)
            .ReportError("[[Banner_Attention_ErrorTitle]]", "[[Banner_Attention_OpenFailed]]");
        Assert.NotNull(postedItem);
        Assert.Equal(new BannerCycleItem(BannerView.Error, 0, errorId), postedItem);
    }

    [Fact]
    public async Task AttentionBanner_WhenOpenThrows_PostsLocalizedDetailedFallbackError()
    {
        BannerId errorId = BannerId.Create();
        BannerCycleItem? postedItem = null;
        _menuActionService.OpenDatabaseToolsAsync()
            .Returns(Task.FromException<bool>(new InvalidOperationException("modal boom")));
        _errorBannerService.ReportError("[[Banner_Attention_ErrorTitle]]", "[[Banner_Attention_OpenFailedDetail(modal boom)]]")
            .Returns(errorId);

        var component = Render<AttentionBanner>(parameters => parameters
            .Add(banner => banner.AttentionCount, 1)
            .Add(
                banner => banner.OnFallbackErrorPosted,
                EventCallback.Factory.Create<BannerCycleItem>(this, item => postedItem = item)));

        await component.Find("button.banner-action").ClickAsync(new MouseEventArgs());

        _errorBannerService.Received(1)
            .ReportError("[[Banner_Attention_ErrorTitle]]", "[[Banner_Attention_OpenFailedDetail(modal boom)]]");
        Assert.NotNull(postedItem);
        Assert.Equal(new BannerCycleItem(BannerView.Error, 0, errorId), postedItem);
    }

    [Fact]
    public void BannerHost_RoutesPaginationAndNavigationAriaThroughLocalizer()
    {
        _errorBannerService.ErrorBanners.Returns([
            new ErrorBannerEntry(BannerId.Create(), "First", "Message", null, null, s_createdUtc),
            new ErrorBannerEntry(BannerId.Create(), "Second", "Message", null, null, s_createdUtc)
        ]);

        var component = Render<BannerHost>();

        Assert.Equal("[[Banner_Pagination(1|2)]]", component.Find(".banner-pagination").TextContent.Trim());
        var previous = component.Find("button.banner-cycle-prev");
        var next = component.Find("button.banner-cycle-next");
        Assert.Equal("[[Banner_Nav_PreviousAria]]", previous.GetAttribute("aria-label"));
        Assert.Equal("[[Banner_Nav_NextAria]]", next.GetAttribute("aria-label"));
        Assert.True(previous.HasAttribute("disabled"));
        Assert.False(next.HasAttribute("disabled"));

        next.Click();

        Assert.Equal("[[Banner_Pagination(2|2)]]", component.Find(".banner-pagination").TextContent.Trim());
        Assert.False(component.Find("button.banner-cycle-prev").HasAttribute("disabled"));
        Assert.True(component.Find("button.banner-cycle-next").HasAttribute("disabled"));
    }

    [Fact]
    public async Task CriticalBanner_RoutesTemplateButtonsChipAndFailureSubtitlesThroughLocalizer()
    {
        var critical = new InvalidOperationException("kaboom");
        _applicationRestartService.TryRestartAsync().Returns(Task.FromResult(false));
        _criticalErrorService.TryRecoverAsync()
            .Returns(Task.FromException(new InvalidOperationException("recovery failed")));

        var component = Render<CriticalBanner>(parameters => parameters.Add(banner => banner.Critical, critical));

        Assert.Equal(
            "[[Banner_Critical_Unexpected(InvalidOperationException|kaboom)]]",
            component.Find(".banner-message").TextContent.Trim());
        var buttons = component.FindAll(".banner-actions button");
        Assert.Equal("[[Banner_Critical_Reload]]", buttons[0].TextContent.Trim());
        Assert.Equal("[[Banner_Critical_Relaunch]]", buttons[1].TextContent.Trim());
        Assert.Equal("[[Banner_Critical_CopyDetails]]", buttons[2].TextContent.Trim());

        buttons[2].Click();
        Assert.Equal("[[Banner_Critical_Copied]]", component.Find(".banner-chip").TextContent.Trim());

        await buttons[1].ClickAsync(new MouseEventArgs());
        Assert.Contains("[[Banner_Critical_RestartFailed]]", component.Markup, StringComparison.Ordinal);

        await buttons[0].ClickAsync(new MouseEventArgs());
        Assert.Contains("[[Banner_Critical_RecoveryFailed(recovery failed)]]", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CriticalBanner_WhenRelaunchRetrySucceeds_ClearsRestartFailureSubtitle()
    {
        var critical = new InvalidOperationException("kaboom");
        _applicationRestartService.TryRestartAsync().Returns(Task.FromResult(false), Task.FromResult(true));
        var component = Render<CriticalBanner>(parameters => parameters.Add(banner => banner.Critical, critical));
        var relaunchButton = component.FindAll(".banner-actions button")[1];

        await relaunchButton.ClickAsync(new MouseEventArgs());
        Assert.Single(component.FindAll(".banner-subtitle"));

        await relaunchButton.ClickAsync(new MouseEventArgs());

        Assert.Empty(component.FindAll(".banner-subtitle"));
    }

    [Fact]
    public async Task CriticalBanner_WhenReloadRetrySucceeds_ClearsRecoveryFailureSubtitle()
    {
        var critical = new InvalidOperationException("kaboom");
        _criticalErrorService.TryRecoverAsync()
            .Returns(Task.FromException(new InvalidOperationException("recovery failed")), Task.CompletedTask);
        var component = Render<CriticalBanner>(parameters => parameters.Add(banner => banner.Critical, critical));
        var reloadButton = component.FindAll(".banner-actions button")[0];

        await reloadButton.ClickAsync(new MouseEventArgs());
        Assert.Single(component.FindAll(".banner-subtitle"));

        await reloadButton.ClickAsync(new MouseEventArgs());

        Assert.Empty(component.FindAll(".banner-subtitle"));
    }

    [Fact]
    public void DeferredContentSurfaces_RenderRawDataWithoutMarkerWrapping()
    {
        var error = Render<ErrorBanner>(parameters => parameters.Add(
            banner => banner.Entry,
            new ErrorBannerEntry(BannerId.Create(), "Database", "Recovery required", "Resolve", () => Task.CompletedTask, s_createdUtc)));
        var info = Render<InfoBanner>(parameters => parameters.Add(
            banner => banner.Entry,
            new BannerInfoEntry(BannerId.Create(), "Notice", "Heads up", BannerSeverity.Info, s_createdUtc)));
        var export = Render<ExportProgressBanner>(parameters => parameters.Add(
            banner => banner.Export,
            new ExportProgressEntry("Export raw message", () => { })));

        Assert.Equal("Database: Recovery required", error.Find(".banner-message").TextContent.Trim());
        Assert.DoesNotContain("[[", error.Find(".banner-message").TextContent, StringComparison.Ordinal);
        Assert.Equal("Resolve", error.Find("button.banner-action").TextContent.Trim());
        Assert.DoesNotContain("[[", error.Find("button.banner-action").TextContent, StringComparison.Ordinal);
        Assert.Equal("Notice: Heads up", info.Find(".banner-message").TextContent.Trim());
        Assert.DoesNotContain("[[", info.Find(".banner-message").TextContent, StringComparison.Ordinal);
        Assert.Equal("Export raw message", export.Find(".banner-message").TextContent.Trim());
        Assert.DoesNotContain("[[", export.Find(".banner-message").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorAndInfoBanners_RouteDismissAriaOnlyThroughLocalizer()
    {
        var error = new ErrorBannerEntry(BannerId.Create(), "Raw title", "Raw message", null, null, s_createdUtc);
        var info = new BannerInfoEntry(BannerId.Create(), "Raw info", "Raw details", BannerSeverity.Info, s_createdUtc);

        var errorComponent = Render<ErrorBanner>(parameters => parameters.Add(banner => banner.Entry, error));
        var infoComponent = Render<InfoBanner>(parameters => parameters.Add(banner => banner.Entry, info));

        Assert.Equal("Raw title: Raw message", errorComponent.Find(".banner-message").TextContent.Trim());
        Assert.Equal("[[Banner_Error_DismissAria]]", errorComponent.Find("button.banner-dismiss").GetAttribute("aria-label"));
        Assert.Equal("Raw info: Raw details", infoComponent.Find(".banner-message").TextContent.Trim());
        Assert.Equal("[[Banner_Info_DismissAria]]", infoComponent.Find("button.banner-dismiss").GetAttribute("aria-label"));
    }

    [Fact]
    public void ExportProgressBanner_RoutesCancelOnlyThroughLocalizer()
    {
        var component = Render<ExportProgressBanner>(parameters => parameters.Add(
            banner => banner.Export,
            new ExportProgressEntry("Export raw message", () => { })));

        Assert.Equal("Export raw message", component.Find(".banner-message").TextContent.Trim());
        Assert.Equal("[[Modal_Cancel]]", component.Find("button.banner-action").TextContent.Trim());
    }

    [Fact]
    public void RawCountSites_PassUngroupedIntegersToLocalizer()
    {
        var localizer = new MarkerLocalizer();

        Assert.Equal(
            "[[Banner_Attention_Many(1000)]]",
            LocalizedCount.OneOrManyRaw(localizer, 1000, "Banner_Attention_One", "Banner_Attention_Many"));
        Assert.Equal(
            "[[Banner_Attention_Many(1000)]]",
            Render<AttentionBanner>(parameters => parameters.Add(banner => banner.AttentionCount, 1000))
                .Find(".banner-message").TextContent.Trim());
        Assert.Equal(
            "[[Banner_Upgrade_Preparing_Many(1000)]]",
            Render<UpgradeProgressBanner>(parameters => parameters.Add(
                    banner => banner.Progress,
                    CreateUpgradeProgress(position: 0, size: 1000, entryName: string.Empty, queuedBatches: 0)))
                .Find(".banner-message").TextContent.Trim());
        Assert.Equal(
            "[[Banner_Upgrade_InProgress(1000|1001|db.evtx|BackingUp)]]",
            Render<UpgradeProgressBanner>(parameters => parameters.Add(
                    banner => banner.Progress,
                    CreateUpgradeProgress(position: 1000, size: 1001, entryName: "db.evtx", queuedBatches: 0)))
                .Find(".banner-message").TextContent.Trim());
        Assert.Equal(
            "[[Banner_Upgrade_QueuedBatches_Many(1000)]]",
            Render<UpgradeProgressBanner>(parameters => parameters.Add(
                    banner => banner.Progress,
                    CreateUpgradeProgress(position: 1, size: 1, entryName: "db.evtx", queuedBatches: 1000)))
                .Find(".banner-subtitle").TextContent.Trim());

        ErrorBannerEntry[] errorEntries = CreateErrorEntries(1000);
        _errorBannerService.ErrorBanners.Returns(errorEntries);
        var host = Render<BannerHost>();

        Assert.Equal("[[Banner_Pagination(1|1000)]]", host.Find(".banner-pagination").TextContent.Trim());
        Services.GetRequiredService<IBannerCycleStateService>()
            .RegisterFallbackError(new BannerCycleItem(BannerView.Error, 999, errorEntries[999].Id));

        host.WaitForAssertion(() =>
            Assert.Equal("[[Banner_Pagination(1000|1000)]]", host.Find(".banner-pagination").TextContent.Trim()));
    }

    [Fact]
    public void UpgradeProgressBanner_RoutesPreparingInProgressQueuedAndCancelThroughLocalizer()
    {
        var preparingOne = Render<UpgradeProgressBanner>(parameters => parameters.Add(
            banner => banner.Progress,
            CreateUpgradeProgress(position: 0, size: 1, entryName: string.Empty, queuedBatches: 0)));
        var preparingMany = Render<UpgradeProgressBanner>(parameters => parameters.Add(
            banner => banner.Progress,
            CreateUpgradeProgress(position: 0, size: 2, entryName: string.Empty, queuedBatches: 0)));
        var inProgress = Render<UpgradeProgressBanner>(parameters => parameters.Add(
            banner => banner.Progress,
            CreateUpgradeProgress(position: 2, size: 5, entryName: "db.evtx", queuedBatches: 1)));
        var queuedMany = Render<UpgradeProgressBanner>(parameters => parameters.Add(
            banner => banner.Progress,
            CreateUpgradeProgress(position: 2, size: 5, entryName: "db.evtx", queuedBatches: 2)));

        Assert.Equal("[[Banner_Upgrade_Preparing_One(1)]]", preparingOne.Find(".banner-message").TextContent.Trim());
        Assert.Empty(preparingOne.FindAll(".banner-subtitle"));
        Assert.Equal("[[Banner_Upgrade_Preparing_Many(2)]]", preparingMany.Find(".banner-message").TextContent.Trim());
        Assert.Equal("[[Banner_Upgrade_InProgress(2|5|db.evtx|BackingUp)]]", inProgress.Find(".banner-message").TextContent.Trim());
        Assert.Equal("[[Banner_Upgrade_QueuedBatches_One(1)]]", inProgress.Find(".banner-subtitle").TextContent.Trim());
        Assert.Equal("[[Banner_Upgrade_QueuedBatches_Many(2)]]", queuedMany.Find(".banner-subtitle").TextContent.Trim());
        Assert.Equal("[[Modal_Cancel]]", inProgress.Find("button.banner-action").TextContent.Trim());
    }

    private static ErrorBannerEntry[] CreateErrorEntries(int count) =>
        Enumerable.Range(1, count)
            .Select(index => new ErrorBannerEntry(BannerId.Create(), $"Title {index}", "Message", null, null, s_createdUtc))
            .ToArray();

    private static BannerProgressEntry CreateUpgradeProgress(int position, int size, string entryName, int queuedBatches) =>
        new(
            UpgradeBatchId.Create(),
            UpgradeProgressScope.Background,
            position,
            size,
            entryName,
            UpgradePhase.BackingUp,
            queuedBatches,
            () => { });
}
