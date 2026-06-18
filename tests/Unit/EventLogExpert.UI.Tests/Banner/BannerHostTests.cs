// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Common.Restart;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.UI.Banner;
using EventLogExpert.UI.DatabaseTools;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Banner;

public sealed class BannerHostTests : BunitContext
{
    private readonly IApplicationRestartService _applicationRestartService =
        Substitute.For<IApplicationRestartService>();
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

    public BannerHostTests()
    {
        Services.AddBannerSubstitutes(out _attentionBannerService, out _progressBannerService, out _exportProgressBannerService, out _criticalErrorService, out _errorBannerService, out _infoBannerService);
        Services.AddSingleton<IBannerCycleStateService, BannerCycleStateService>();

        _criticalErrorService.CurrentCritical.Returns((Exception?)null);
        _errorBannerService.ErrorBanners.Returns([]);
        _infoBannerService.InfoBanners.Returns([]);
        _attentionBannerService.AttentionEntries.Returns([]);
        _attentionBannerService.AttentionDismissed.Returns(false);
        _progressBannerService.BackgroundProgress.Returns((BannerProgressEntry?)null);
        _exportProgressBannerService.CurrentExport.Returns((ExportProgressEntry?)null);
        _modalCoordinator.ActiveSession.Returns((ModalSession?)null);

        Services.AddSingleton(_applicationRestartService);
        Services.AddSingleton(_clipboardService);
        Services.AddSingleton(_menuActionService);
        Services.AddSingleton(_modalCoordinator);
        Services.AddSingleton(_traceLogger);

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void BannerHost_AttentionDismissed_DoesNotRenderAttentionBanner()
    {
        _attentionBannerService.AttentionEntries.Returns([BuildDatabaseEntry("a.db")]);
        _attentionBannerService.AttentionDismissed.Returns(true);

        var component = Render<BannerHost>();

        Assert.Empty(component.FindAll("aside.banner-attention"));
    }

    [Fact]
    public void BannerHost_CriticalActive_DoesNotRenderCycleNav_EvenWithOtherSlices()
    {
        // Critical pre-empts the entire cycle — no Prev/Next chevrons should appear.
        _criticalErrorService.CurrentCritical.Returns(new InvalidOperationException("kaboom"));
        _errorBannerService.ErrorBanners.Returns(
            [new ErrorBannerEntry(BannerId.Create(), "E", "m", null, null, DateTime.UtcNow)]);
        _attentionBannerService.AttentionEntries.Returns([BuildDatabaseEntry("a.db")]);

        var component = Render<BannerHost>();

        Assert.Single(component.FindAll("aside.banner-critical"));
        Assert.Empty(component.FindAll("button.banner-cycle-prev"));
        Assert.Empty(component.FindAll("button.banner-cycle-next"));
        Assert.Empty(component.FindAll(".banner-pagination"));
    }

    [Fact]
    public void BannerHost_CriticalAndErrorAndInfoAllPresent_RendersOnlyCritical()
    {
        _criticalErrorService.CurrentCritical.Returns(new InvalidOperationException("kaboom"));

        _errorBannerService.ErrorBanners.Returns(
            [new ErrorBannerEntry(BannerId.Create(), "Error", "E", null, null, DateTime.UtcNow)]);

        _infoBannerService.InfoBanners.Returns([
            new BannerInfoEntry(BannerId.Create(), "Info", "I", BannerSeverity.Info, DateTime.UtcNow)
        ]);

        var component = Render<BannerHost>();

        Assert.Single(component.FindAll("aside.banner-critical"));
        Assert.Empty(component.FindAll("aside.banner-error"));
        Assert.Empty(component.FindAll("aside.banner-info"));
    }

    [Fact]
    public void BannerHost_CycleErrorAndAttention_RendersFirstErrorWithCyclePagination_TwoOfTwo()
    {
        var error = new ErrorBannerEntry(BannerId.Create(), "Err", "msg", null, null, DateTime.UtcNow);
        _errorBannerService.ErrorBanners.Returns([error]);
        _attentionBannerService.AttentionEntries.Returns([BuildDatabaseEntry("a.db")]);

        var component = Render<BannerHost>();

        var banner = component.Find("aside.banner-error");
        var pagination = component.Find("aside.banner-error .banner-pagination");
        Assert.Equal("1 of 2", pagination.TextContent.Trim());
        Assert.Contains("Err: msg", banner.TextContent);
    }

    [Fact]
    public async Task BannerHost_CycleNextAndPrev_AcrossThreeInfoBanners_UpdatesDisplayedEntryAndPagination()
    {
        var i0 = new BannerInfoEntry(BannerId.Create(), "First", "first message", BannerSeverity.Info, DateTime.UtcNow);
        var i1 = new BannerInfoEntry(BannerId.Create(), "Second", "second message", BannerSeverity.Info, DateTime.UtcNow);
        var i2 = new BannerInfoEntry(BannerId.Create(), "Third", "third message", BannerSeverity.Info, DateTime.UtcNow);
        _infoBannerService.InfoBanners.Returns([i0, i1, i2]);

        var component = Render<BannerHost>();

        Assert.Contains("First: first message", component.Find("aside.banner-info").TextContent);
        Assert.Equal("1 of 3", component.Find(".banner-pagination").TextContent.Trim());

        await component.Find("button.banner-cycle-next").ClickAsync(new MouseEventArgs());

        Assert.Contains("Second: second message", component.Find("aside.banner-info").TextContent);
        Assert.DoesNotContain("First", component.Find("aside.banner-info").TextContent);
        Assert.Equal("2 of 3", component.Find(".banner-pagination").TextContent.Trim());

        await component.Find("button.banner-cycle-next").ClickAsync(new MouseEventArgs());

        Assert.Contains("Third: third message", component.Find("aside.banner-info").TextContent);
        Assert.DoesNotContain("Second", component.Find("aside.banner-info").TextContent);
        Assert.Equal("3 of 3", component.Find(".banner-pagination").TextContent.Trim());

        await component.Find("button.banner-cycle-prev").ClickAsync(new MouseEventArgs());

        Assert.Contains("Second: second message", component.Find("aside.banner-info").TextContent);
        Assert.DoesNotContain("Third", component.Find("aside.banner-info").TextContent);
        Assert.Equal("2 of 3", component.Find(".banner-pagination").TextContent.Trim());

        await component.Find("button.banner-cycle-prev").ClickAsync(new MouseEventArgs());

        Assert.Contains("First: first message", component.Find("aside.banner-info").TextContent);
        Assert.DoesNotContain("Second", component.Find("aside.banner-info").TextContent);
        Assert.Equal("1 of 3", component.Find(".banner-pagination").TextContent.Trim());
    }

    [Fact]
    public async Task BannerHost_CycleNextAtLast_DisabledAndDoesNotAdvance()
    {
        _errorBannerService.ErrorBanners.Returns(
            [new ErrorBannerEntry(BannerId.Create(), "E", "m", null, null, DateTime.UtcNow)]);
        _attentionBannerService.AttentionEntries.Returns([BuildDatabaseEntry("a.db")]);

        var component = Render<BannerHost>();
        // Advance to last item (index 1).
        await component.Find("button.banner-cycle-next").ClickAsync(new MouseEventArgs());

        var next = component.Find("button.banner-cycle-next");
        Assert.True(next.HasAttribute("disabled"));

        await next.ClickAsync(new MouseEventArgs());

        // Index stays at 1.
        Assert.Equal("2 of 2", component.Find(".banner-pagination").TextContent.Trim());
        Assert.Single(component.FindAll("aside.banner-attention"));
    }

    [Fact]
    public async Task BannerHost_CycleNextClicked_AdvancesToAttentionItem()
    {
        var error = new ErrorBannerEntry(BannerId.Create(), "Err", "msg", null, null, DateTime.UtcNow);
        _errorBannerService.ErrorBanners.Returns([error]);
        _attentionBannerService.AttentionEntries.Returns([BuildDatabaseEntry("a.db")]);

        var component = Render<BannerHost>();
        await component.Find("button.banner-cycle-next").ClickAsync(new MouseEventArgs());

        var pagination = component.Find(".banner-pagination");
        Assert.Equal("2 of 2", pagination.TextContent.Trim());
        Assert.Single(component.FindAll("aside.banner-attention"));
        Assert.Empty(component.FindAll("aside.banner-error"));
    }

    [Fact]
    public async Task BannerHost_CyclePrevAtFirst_DisabledAndDoesNotAdvance()
    {
        _errorBannerService.ErrorBanners.Returns(
            [new ErrorBannerEntry(BannerId.Create(), "E", "m", null, null, DateTime.UtcNow)]);
        _attentionBannerService.AttentionEntries.Returns([BuildDatabaseEntry("a.db")]);

        var component = Render<BannerHost>();
        var prev = component.Find("button.banner-cycle-prev");
        Assert.True(prev.HasAttribute("disabled"));

        await prev.ClickAsync(new MouseEventArgs());

        // Index stays at 0 — first error still rendered.
        Assert.Equal("1 of 2", component.Find(".banner-pagination").TextContent.Trim());
        Assert.Single(component.FindAll("aside.banner-error"));
    }

    [Fact]
    public async Task BannerHost_CycleStableSelection_DismissingPrecedingError_StaysOnSameLogicalError()
    {
        // Regression: an earlier design matched selection by (View, IndexWithinSlice) record equality, which
        // silently jumped the user to a different error whenever a preceding error was dismissed (e.g., user on
        // E1 = index 1, then E0 dismissed → new (Error, 1) refers to E2 → user is now reading E2 without any
        // intent to navigate). BannerCycleItem.EntryId provides stable identity so the user stays on E1.
        var e0 = new ErrorBannerEntry(BannerId.Create(), "First", "first message", null, null, DateTime.UtcNow);
        var e1 = new ErrorBannerEntry(BannerId.Create(), "Second", "second message", null, null, DateTime.UtcNow);
        var e2 = new ErrorBannerEntry(BannerId.Create(), "Third", "third message", null, null, DateTime.UtcNow);
        _errorBannerService.ErrorBanners.Returns([e0, e1, e2]);

        var component = Render<BannerHost>();
        // Advance to e1.
        await component.Find("button.banner-cycle-next").ClickAsync(new MouseEventArgs());
        Assert.Contains("Second: second message", component.Find("aside.banner-error").TextContent);
        Assert.Equal("2 of 3", component.Find(".banner-pagination").TextContent.Trim());

        // Simulate e0 being dismissed externally — IndexWithinSlice for e1/e2 shifts down by one, but EntryId
        // stays stable so selection-by-EntryId still resolves to e1.
        _errorBannerService.ErrorBanners.Returns([e1, e2]);
        _errorBannerService.StateChanged += Raise.Event<Action>();

        component.WaitForState(() =>
        {
            var pages = component.FindAll(".banner-pagination");
            return pages.Count > 0 && pages[0].TextContent.Trim() == "1 of 2";
        });

        // After rebuild, the user must still see e1 (now at index 0). The bug being prevented: jumping to e2
        // because the old (Error, 1) tuple matched e2's new (Error, 1) position.
        Assert.Contains("Second: second message", component.Find("aside.banner-error").TextContent);
        Assert.DoesNotContain("Third", component.Find("aside.banner-error").TextContent);
    }

    [Fact]
    public async Task BannerHost_ExportCancelClicked_InvokesCancelDelegate()
    {
        bool canceled = false;
        _exportProgressBannerService.CurrentExport.Returns(
            new ExportProgressEntry("Exporting events…", () => canceled = true));

        var component = Render<BannerHost>();
        await component.Find("aside.banner-export-progress button.banner-action").ClickAsync(new MouseEventArgs());

        Assert.True(canceled);
    }

    [Fact]
    public void BannerHost_ExportInProgress_RendersExportProgressBannerWithMessage()
    {
        _exportProgressBannerService.CurrentExport.Returns(
            new ExportProgressEntry("Exporting events…", () => { }));

        var component = Render<BannerHost>();

        var banner = component.Find("aside.banner-export-progress");
        Assert.Contains("Exporting events…", banner.TextContent);
        Assert.Single(component.FindAll("aside.banner-export-progress button.banner-action"));
    }

    [Fact]
    public void BannerHost_MultipleErrorBanners_RendersFirstWithPagination()
    {
        var first = new ErrorBannerEntry(BannerId.Create(), "First", "First message", null, null, DateTime.UtcNow);
        var second = new ErrorBannerEntry(BannerId.Create(), "Second", "Second message", null, null, DateTime.UtcNow);
        _errorBannerService.ErrorBanners.Returns([first, second]);

        var component = Render<BannerHost>();

        var banner = component.Find("aside.banner-error");
        Assert.Contains("First: First message", banner.TextContent);
        Assert.DoesNotContain("Second", banner.TextContent);

        var pagination = component.Find("aside.banner-error .banner-pagination");
        Assert.Equal("1 of 2", pagination.TextContent.Trim());
    }

    [Fact]
    public void BannerHost_NoState_RendersNothing()
    {
        var component = Render<BannerHost>();

        Assert.Equal(string.Empty, component.Markup.Trim());
    }

    [Fact]
    public async Task BannerHost_OpenDatabasesReturnsFalse_RendersNewErrorBanner_NotStaleAttention()
    {
        var attention = BuildDatabaseEntry("a.db");
        var newErrorId = BannerId.Create();
        var newError = new ErrorBannerEntry(
            newErrorId,
            "Databases",
            "Failed to open databases; try again from the menu.",
            null,
            null,
            DateTime.UtcNow);

        _attentionBannerService.AttentionEntries.Returns([attention]);
        _menuActionService.OpenDatabaseToolsAsync().Returns(Task.FromResult(false));
        _errorBannerService.ReportError("Databases", Arg.Any<string>())
            .Returns(_ =>
            {
                _errorBannerService.ErrorBanners.Returns([newError]);
                return newErrorId;
            });

        var component = Render<BannerHost>();
        Assert.Single(component.FindAll("aside.banner-attention"));

        await component.Find("aside.banner-attention button.banner-action").ClickAsync(new MouseEventArgs());
        // Substitute does not raise StateChanged from ReportError; raise manually to trigger re-render.
        _errorBannerService.StateChanged += Raise.Event<Action>();

        component.WaitForState(() => component.FindAll("aside.banner-error").Count > 0);

        var errorBanner = component.Find("aside.banner-error");
        Assert.Contains("Failed to open databases; try again from the menu.", errorBanner.TextContent);
        Assert.Empty(component.FindAll("aside.banner-attention"));
    }

    [Fact]
    public void BannerHost_SingleErrorBanner_RendersWithoutPagination()
    {
        var entry = new ErrorBannerEntry(BannerId.Create(), "Database", "Schema invalid", null, null, DateTime.UtcNow);
        _errorBannerService.ErrorBanners.Returns([entry]);

        var component = Render<BannerHost>();

        var banner = component.Find("aside.banner-error");
        Assert.Contains("Database: Schema invalid", banner.TextContent);
        Assert.Empty(component.FindAll("aside.banner-error .banner-pagination"));
        Assert.Single(component.FindAll("aside.banner-error button.banner-dismiss"));
    }

    [Fact]
    public void Dispose_UnsubscribesFromCycleState()
    {
        _attentionBannerService.AttentionEntries.Returns([BuildDatabaseEntry("a.db")]);

        var component = Render<BannerHost>();
        Assert.Single(component.FindAll("aside.banner-attention"));

        component.Instance.Dispose();

        _attentionBannerService.AttentionEntries.Returns([]);
        _attentionBannerService.StateChanged += Raise.Event<Action>();

        Assert.Single(component.FindAll("aside.banner-attention"));
    }

    [Fact]
    public void Render_AsInsideModal_WithDatabaseToolsModalActive_OmitsAttentionBanner_RendersOtherBanners()
    {
        var errorEntry = new ErrorBannerEntry(
            BannerId.Create(), "Title", "msg", null, null, DateTime.UtcNow);
        _attentionBannerService.AttentionEntries.Returns([BuildDatabaseEntry("a.db")]);
        _errorBannerService.ErrorBanners.Returns([errorEntry]);
        _modalCoordinator.ActiveSession.Returns(
            new ModalSession(new ModalId(5), typeof(DatabaseToolsModal), null));
        var cycleState = Services.GetRequiredService<IBannerCycleStateService>();
        cycleState.SetModalContentDisplayed(true);

        var component = Render<BannerHost>(parameters => parameters
            .Add(p => p.Location, BannerHostLocation.InsideModal));

        Assert.Empty(component.FindAll("aside.banner-attention"));
        Assert.Single(component.FindAll("aside.banner-error"));
    }

    [Fact]
    public void Render_AsInsideModal_WithDifferentModalActive_RendersAttentionBanner()
    {
        _attentionBannerService.AttentionEntries.Returns([BuildDatabaseEntry("a.db")]);
        _modalCoordinator.ActiveSession.Returns(
            new ModalSession(new ModalId(2), typeof(BannerHost), null));
        var cycleState = Services.GetRequiredService<IBannerCycleStateService>();
        cycleState.SetModalContentDisplayed(true);

        var component = Render<BannerHost>(parameters => parameters
            .Add(p => p.Location, BannerHostLocation.InsideModal));

        Assert.Single(component.FindAll("aside.banner-attention"));
    }

    [Fact]
    public void Render_AsInsideModalLocation_WithNoModalActive_RendersNothing()
    {
        _attentionBannerService.AttentionEntries.Returns([BuildDatabaseEntry("a.db")]);
        _modalCoordinator.ActiveSession.Returns((ModalSession?)null);

        var component = Render<BannerHost>(parameters => parameters
            .Add(p => p.Location, BannerHostLocation.InsideModal));

        Assert.Empty(component.FindAll("aside.banner-attention"));
    }

    [Fact]
    public void Render_AsMainLayoutLocation_WithModalActive_RendersNothing()
    {
        _attentionBannerService.AttentionEntries.Returns([BuildDatabaseEntry("a.db")]);
        _modalCoordinator.ActiveSession.Returns(
            new ModalSession(new ModalId(6), typeof(BannerHost), null));
        var cycleState = Services.GetRequiredService<IBannerCycleStateService>();
        cycleState.SetModalContentDisplayed(true);

        var component = Render<BannerHost>();

        Assert.Empty(component.FindAll("aside.banner-attention"));
    }

    [Fact]
    public async Task Render_ModalActivationTransition_TogglesAttentionVisibility()
    {
        _attentionBannerService.AttentionEntries.Returns([BuildDatabaseEntry("a.db")]);
        var cycleState = Services.GetRequiredService<IBannerCycleStateService>();

        var component = Render<BannerHost>();
        Assert.Single(component.FindAll("aside.banner-attention"));

        _modalCoordinator.ActiveSession.Returns(
            new ModalSession(new ModalId(3), typeof(DatabaseToolsModal), null));
        _modalCoordinator.StateChanged += Raise.Event<Action>();
        cycleState.SetModalContentDisplayed(true);

        await component.WaitForAssertionAsync(() =>
            Assert.Empty(component.FindAll("aside.banner-attention")));

        _modalCoordinator.ActiveSession.Returns((ModalSession?)null);
        _modalCoordinator.StateChanged += Raise.Event<Action>();

        await component.WaitForAssertionAsync(() =>
            Assert.Single(component.FindAll("aside.banner-attention")));
    }

    [Fact]
    public void Render_WithDatabaseToolsModalActive_OmitsAttentionBanner()
    {
        _attentionBannerService.AttentionEntries.Returns([BuildDatabaseEntry("a.db")]);
        _modalCoordinator.ActiveSession.Returns(
            new ModalSession(new ModalId(1), typeof(DatabaseToolsModal), null));
        var cycleState = Services.GetRequiredService<IBannerCycleStateService>();
        cycleState.SetModalContentDisplayed(true);

        var component = Render<BannerHost>();

        Assert.Empty(component.FindAll("aside.banner-attention"));
    }

    [Fact]
    public void Render_WithNoModalActive_RendersAttentionBanner()
    {
        _attentionBannerService.AttentionEntries.Returns([BuildDatabaseEntry("a.db")]);
        _modalCoordinator.ActiveSession.Returns((ModalSession?)null);

        var component = Render<BannerHost>();

        Assert.Single(component.FindAll("aside.banner-attention"));
    }

    private static DatabaseEntry BuildDatabaseEntry(string fileName) =>
        new(fileName, $@"C:\dbs\{fileName}", false, DatabaseStatus.UpgradeRequired);
}
