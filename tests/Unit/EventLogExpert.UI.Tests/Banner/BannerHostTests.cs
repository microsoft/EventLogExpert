// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Common.Restart;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.Database.Upgrade;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.UI.Banner;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Banner;

public sealed class BannerHostTests : BunitContext
{
    private readonly IApplicationRestartService _applicationRestartService =
        Substitute.For<IApplicationRestartService>();
    private readonly IBannerService _bannerService = Substitute.For<IBannerService>();
    private readonly IClipboardService _clipboardService = Substitute.For<IClipboardService>();
    private readonly IMenuActionService _menuActionService = Substitute.For<IMenuActionService>();
    private readonly ITraceLogger _traceLogger = Substitute.For<ITraceLogger>();

    public BannerHostTests()
    {
        _bannerService.CurrentCritical.Returns((Exception?)null);
        _bannerService.ErrorBanners.Returns([]);
        _bannerService.InfoBanners.Returns([]);
        _bannerService.AttentionEntries.Returns([]);
        _bannerService.AttentionDismissed.Returns(false);
        _bannerService.BackgroundProgress.Returns((BannerProgressEntry?)null);

        Services.AddSingleton(_bannerService);
        Services.AddSingleton(_applicationRestartService);
        Services.AddSingleton(_clipboardService);
        Services.AddSingleton(_menuActionService);
        Services.AddSingleton(_traceLogger);

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task BannerHost_AttentionDismissClicked_CallsDismissAttention()
    {
        _bannerService.AttentionEntries.Returns([BuildDatabaseEntry("a.db")]);

        var component = Render<BannerHost>();
        await component.Find("aside.banner-attention button.banner-dismiss").ClickAsync(new MouseEventArgs());

        _bannerService.Received(1).DismissAttention();
    }

    [Fact]
    public void BannerHost_AttentionDismissed_DoesNotRenderAttentionBanner()
    {
        _bannerService.AttentionEntries.Returns([BuildDatabaseEntry("a.db")]);
        _bannerService.AttentionDismissed.Returns(true);

        var component = Render<BannerHost>();

        Assert.Empty(component.FindAll("aside.banner-attention"));
    }

    [Fact]
    public void BannerHost_AttentionEntries_RendersAttentionBannerWithOpenSettingsAndDismiss()
    {
        _bannerService.AttentionEntries.Returns(
            [BuildDatabaseEntry("a.db"), BuildDatabaseEntry("b.db")]);

        var component = Render<BannerHost>();

        var banner = component.Find("aside.banner-attention");
        Assert.Contains("2 databases need attention", banner.TextContent);
        Assert.Equal("Open Settings", component.Find("aside.banner-attention button.banner-action").TextContent.Trim());
        Assert.Single(component.FindAll("aside.banner-attention button.banner-dismiss"));
    }

    [Fact]
    public void BannerHost_AttentionEntriesSingleEntry_UsesSingularDatabaseLabel()
    {
        _bannerService.AttentionEntries.Returns([BuildDatabaseEntry("a.db")]);

        var component = Render<BannerHost>();

        var banner = component.Find("aside.banner-attention");
        Assert.Contains("1 database need", banner.TextContent);
        Assert.DoesNotContain("databases need", banner.TextContent);
    }

    [Fact]
    public void BannerHost_BackgroundProgressWithEmptyEntryName_RendersPreparingMessage()
    {
        // Pre-first-tick rendering: BannerService creates the entry with Position=0/EntryName="" before the
        // first per-entry progress event arrives. The "Preparing..." text avoids the misleading
        // "Upgrading database 0 of N: " string in that gap.
        _bannerService.BackgroundProgress.Returns(
            new BannerProgressEntry(
                UpgradeBatchId.Create(),
                UpgradeProgressScope.Background,
                0,
                3,
                string.Empty,
                UpgradePhase.BackingUp,
                0,
                () => { }));

        var component = Render<BannerHost>();

        var banner = component.Find("aside.banner-upgrade-progress");
        Assert.Contains("Preparing upgrade of 3 databases", banner.TextContent);
        Assert.DoesNotContain("Upgrading database 0", banner.TextContent);
    }

    [Fact]
    public void BannerHost_BackgroundProgressWithEntryName_RendersUpgradeProgressBannerWithCancelButton()
    {
        _bannerService.BackgroundProgress.Returns(
            new BannerProgressEntry(
                UpgradeBatchId.Create(),
                UpgradeProgressScope.Background,
                2,
                5,
                "MyDb.evtx",
                UpgradePhase.MigratingSchema,
                0,
                () => { }));

        var component = Render<BannerHost>();

        var banner = component.Find("aside.banner-upgrade-progress");
        Assert.Contains("Upgrading database 2 of 5", banner.TextContent);
        Assert.Contains("MyDb.evtx", banner.TextContent);
        Assert.Contains("MigratingSchema", banner.TextContent);
        Assert.Equal("Cancel", component.Find("aside.banner-upgrade-progress button.banner-action").TextContent.Trim());
        Assert.Single(component.FindAll("aside.banner-upgrade-progress .banner-spinner"));
    }

    [Fact]
    public void BannerHost_BackgroundProgressWithQueuedBatches_RendersQueuedBatchesSubtitle()
    {
        _bannerService.BackgroundProgress.Returns(
            new BannerProgressEntry(
                UpgradeBatchId.Create(),
                UpgradeProgressScope.Background,
                1,
                2,
                "x.evtx",
                UpgradePhase.Verifying,
                3,
                () => { }));

        var component = Render<BannerHost>();

        var subtitle = component.Find("aside.banner-upgrade-progress .banner-subtitle");
        Assert.Contains("+3 batches queued", subtitle.TextContent);
    }

    [Fact]
    public async Task BannerHost_CancelUpgradeClicked_InvokesCancelDelegate()
    {
        int cancelInvocationCount = 0;
        _bannerService.BackgroundProgress.Returns(
            new BannerProgressEntry(
                UpgradeBatchId.Create(),
                UpgradeProgressScope.Background,
                1,
                1,
                "x.evtx",
                UpgradePhase.MigratingSchema,
                0,
                () => cancelInvocationCount++));

        var component = Render<BannerHost>();
        await component.Find("aside.banner-upgrade-progress button.banner-action").ClickAsync(new MouseEventArgs());

        Assert.Equal(1, cancelInvocationCount);
    }

    [Fact]
    public async Task BannerHost_CancelUpgradeThrows_LogsViaTraceLogger_DoesNotPropagate()
    {
        _bannerService.BackgroundProgress.Returns(
            new BannerProgressEntry(
                UpgradeBatchId.Create(),
                UpgradeProgressScope.Background,
                1,
                1,
                "x.evtx",
                UpgradePhase.MigratingSchema,
                0,
                () => throw new InvalidOperationException("cts disposed")));

        var component = Render<BannerHost>();
        await component.Find("aside.banner-upgrade-progress button.banner-action").ClickAsync(new MouseEventArgs());

        Assert.Single(component.FindAll("aside.banner-upgrade-progress"));
        _bannerService.DidNotReceive().ReportCritical(Arg.Any<Exception>());
        _traceLogger.Received(1).Error(Arg.Is<ErrorLogHandler>(h =>
            h.ToString().Contains(nameof(BannerHost)) && h.ToString().Contains("cts disposed")));
    }

    [Fact]
    public async Task BannerHost_CopyDetailsClicked_CopiesExceptionAndShowsCopiedChip()
    {
        var critical = new InvalidOperationException("kaboom");
        _bannerService.CurrentCritical.Returns(critical);

        var component = Render<BannerHost>();

        // Sync Click() returns at the handler's first real async point (the 2s Task.Delay) with
        // the chip rendered; ClickAsync would block for the full delay until the chip clears.
        component.Find("aside.banner-critical .banner-actions button:nth-child(3)").Click();

        await _clipboardService.Received(1)
            .CopyTextAsync(Arg.Is<string>(s => s.Contains("InvalidOperationException") && s.Contains("kaboom")));

        Assert.Single(component.FindAll("aside.banner-critical .banner-feedback .banner-chip"));
    }

    [Fact]
    public void BannerHost_CriticalActive_DoesNotRenderCycleNav_EvenWithOtherSlices()
    {
        // Critical pre-empts the entire cycle — no Prev/Next chevrons should appear.
        _bannerService.CurrentCritical.Returns(new InvalidOperationException("kaboom"));
        _bannerService.ErrorBanners.Returns(
            [new ErrorBannerEntry(BannerId.Create(), "E", "m", null, null, DateTime.UtcNow)]);
        _bannerService.AttentionEntries.Returns([BuildDatabaseEntry("a.db")]);

        var component = Render<BannerHost>();

        Assert.Single(component.FindAll("aside.banner-critical"));
        Assert.Empty(component.FindAll("button.banner-cycle-prev"));
        Assert.Empty(component.FindAll("button.banner-cycle-next"));
        Assert.Empty(component.FindAll(".banner-pagination"));
    }

    [Fact]
    public void BannerHost_CriticalAndErrorAndInfoAllPresent_RendersOnlyCritical()
    {
        _bannerService.CurrentCritical.Returns(new InvalidOperationException("kaboom"));

        _bannerService.ErrorBanners.Returns(
            [new ErrorBannerEntry(BannerId.Create(), "Error", "E", null, null, DateTime.UtcNow)]);

        _bannerService.InfoBanners.Returns([
            new BannerInfoEntry(BannerId.Create(), "Info", "I", BannerSeverity.Info, DateTime.UtcNow)
        ]);

        var component = Render<BannerHost>();

        Assert.Single(component.FindAll("aside.banner-critical"));
        Assert.Empty(component.FindAll("aside.banner-error"));
        Assert.Empty(component.FindAll("aside.banner-info"));
    }

    [Fact]
    public void BannerHost_CurrentCritical_RendersCriticalBannerWithThreeButtons()
    {
        var critical = new InvalidOperationException("kaboom");
        _bannerService.CurrentCritical.Returns(critical);

        var component = Render<BannerHost>();

        var banner = component.Find("aside.banner-critical");
        Assert.Contains("InvalidOperationException", banner.TextContent);
        Assert.Contains("kaboom", banner.TextContent);

        var buttons = component.FindAll("aside.banner-critical .banner-actions button");
        Assert.Equal(3, buttons.Count);
        Assert.Contains("Reload", buttons[0].TextContent);
        Assert.Contains("Relaunch", buttons[1].TextContent);
        Assert.Contains("Copy details", buttons[2].TextContent);
    }

    [Fact]
    public void BannerHost_CycleErrorAndAttention_RendersFirstErrorWithCyclePagination_TwoOfTwo()
    {
        var error = new ErrorBannerEntry(BannerId.Create(), "Err", "msg", null, null, DateTime.UtcNow);
        _bannerService.ErrorBanners.Returns([error]);
        _bannerService.AttentionEntries.Returns([BuildDatabaseEntry("a.db")]);

        var component = Render<BannerHost>();

        var banner = component.Find("aside.banner-error");
        var pagination = component.Find("aside.banner-error .banner-pagination");
        Assert.Equal("1 of 2", pagination.TextContent.Trim());
        Assert.Contains("Err: msg", banner.TextContent);
    }

    [Fact]
    public async Task BannerHost_CycleNextAtLast_DisabledAndDoesNotAdvance()
    {
        _bannerService.ErrorBanners.Returns(
            [new ErrorBannerEntry(BannerId.Create(), "E", "m", null, null, DateTime.UtcNow)]);
        _bannerService.AttentionEntries.Returns([BuildDatabaseEntry("a.db")]);

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
        _bannerService.ErrorBanners.Returns([error]);
        _bannerService.AttentionEntries.Returns([BuildDatabaseEntry("a.db")]);

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
        _bannerService.ErrorBanners.Returns(
            [new ErrorBannerEntry(BannerId.Create(), "E", "m", null, null, DateTime.UtcNow)]);
        _bannerService.AttentionEntries.Returns([BuildDatabaseEntry("a.db")]);

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
        _bannerService.ErrorBanners.Returns([e0, e1, e2]);

        var component = Render<BannerHost>();
        // Advance to e1.
        await component.Find("button.banner-cycle-next").ClickAsync(new MouseEventArgs());
        Assert.Contains("Second: second message", component.Find("aside.banner-error").TextContent);
        Assert.Equal("2 of 3", component.Find(".banner-pagination").TextContent.Trim());

        // Simulate e0 being dismissed externally — IndexWithinSlice for e1/e2 shifts down by one, but EntryId
        // stays stable so selection-by-EntryId still resolves to e1.
        _bannerService.ErrorBanners.Returns([e1, e2]);
        _bannerService.StateChanged += Raise.Event<Action>();

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
    public async Task BannerHost_DismissErrorClicked_CallsDismissErrorWithEntryId()
    {
        var entry = new ErrorBannerEntry(BannerId.Create(), "Database", "Schema invalid", null, null, DateTime.UtcNow);
        _bannerService.ErrorBanners.Returns([entry]);

        var component = Render<BannerHost>();
        await component.Find("aside.banner-error button.banner-dismiss").ClickAsync(new MouseEventArgs());

        _bannerService.Received(1).DismissError(entry.Id);
    }

    [Fact]
    public async Task BannerHost_DismissInfoClicked_CallsDismissInfoBannerWithEntryId()
    {
        var info = new BannerInfoEntry(BannerId.Create(), "Notice", "Heads up", BannerSeverity.Info, DateTime.UtcNow);
        _bannerService.InfoBanners.Returns([info]);

        var component = Render<BannerHost>();
        await component.Find("aside.banner-info button.banner-dismiss").ClickAsync(new MouseEventArgs());

        _bannerService.Received(1).DismissInfoBanner(info.Id);
    }

    [Fact]
    public async Task BannerHost_ErrorBannerActionClicked_InvokesSuppliedCallback()
    {
        int actionInvocationCount = 0;
        var entry = new ErrorBannerEntry(BannerId.Create(),
            "Database",
            "Recovery required",
            "Resolve",
            () => { actionInvocationCount++; return Task.CompletedTask; },
            DateTime.UtcNow);
        _bannerService.ErrorBanners.Returns([entry]);

        var component = Render<BannerHost>();
        await component.Find("aside.banner-error button.banner-action").ClickAsync(new MouseEventArgs());

        Assert.Equal(1, actionInvocationCount);
    }

    [Fact]
    public async Task BannerHost_ErrorBannerActionThrows_LogsViaTraceLogger_DoesNotPropagate()
    {
        // Arrange — action exceptions must be swallowed by BannerHost so they do not bubble up to ErrorBoundary
        // and escalate the visible banner from Error to Critical (which would replace the user's actionable error
        // with a Reload-tier critical banner).
        var actionException = new InvalidOperationException("action boom");
        var entry = new ErrorBannerEntry(BannerId.Create(),
            "Database",
            "Recovery required",
            "Resolve",
            () => throw actionException,
            DateTime.UtcNow);
        _bannerService.ErrorBanners.Returns([entry]);

        var component = Render<BannerHost>();

        // Act
        await component.Find("aside.banner-error button.banner-action").ClickAsync(new MouseEventArgs());

        // Assert — banner stays visible, the critical slot was not populated, and the exception was logged.
        Assert.Single(component.FindAll("aside.banner-error"));
        _bannerService.DidNotReceive().ReportCritical(Arg.Any<Exception>());
        _traceLogger.Received(1).Error(Arg.Is<ErrorLogHandler>(h =>
            h.ToString().Contains(nameof(BannerHost)) && h.ToString().Contains("action boom")));
    }

    [Fact]
    public void BannerHost_ErrorBannerWithAction_RendersActionButtonWithLabel()
    {
        var entry = new ErrorBannerEntry(BannerId.Create(),
            "Database",
            "Recovery required",
            "Resolve",
            () => Task.CompletedTask,
            DateTime.UtcNow);
        _bannerService.ErrorBanners.Returns([entry]);

        var component = Render<BannerHost>();

        var actionButton = component.Find("aside.banner-error button.banner-action");
        Assert.Equal("Resolve", actionButton.TextContent.Trim());
    }

    [Fact]
    public void BannerHost_ErrorBannerWithoutAction_DoesNotRenderActionButton()
    {
        var entry = new ErrorBannerEntry(BannerId.Create(), "Database", "Schema invalid", null, null, DateTime.UtcNow);
        _bannerService.ErrorBanners.Returns([entry]);

        var component = Render<BannerHost>();

        Assert.Empty(component.FindAll("aside.banner-error button.banner-action"));
    }

    [Fact]
    public void BannerHost_InfoSeverity_RendersInfoStyledBanner()
    {
        var info = new BannerInfoEntry(BannerId.Create(), "Notice", "Heads up", BannerSeverity.Info, DateTime.UtcNow);
        _bannerService.InfoBanners.Returns([info]);

        var component = Render<BannerHost>();

        Assert.Single(component.FindAll("aside.banner.banner-info"));
        Assert.Empty(component.FindAll("aside.banner.banner-warning"));
        Assert.Contains("Notice: Heads up", component.Find("aside.banner-info").TextContent);
    }

    [Fact]
    public void BannerHost_MultipleErrorBanners_RendersFirstWithPagination()
    {
        var first = new ErrorBannerEntry(BannerId.Create(), "First", "First message", null, null, DateTime.UtcNow);
        var second = new ErrorBannerEntry(BannerId.Create(), "Second", "Second message", null, null, DateTime.UtcNow);
        _bannerService.ErrorBanners.Returns([first, second]);

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
    public async Task BannerHost_OpenSettingsClicked_DismissesAttention_BeforeAwaitingOpenSettings()
    {
        // Clicking the action button is itself the user-acknowledgement that they're acting on the items;
        // dismissing immediately means the banner doesn't linger while the modal opens (which can take a
        // perceptible beat). On failure the error banner replaces the attention banner as the visible signal.
        _bannerService.AttentionEntries.Returns([BuildDatabaseEntry("a.db")]);
        _menuActionService.OpenSettingsAsync().Returns(Task.FromResult(true));

        var component = Render<BannerHost>();
        await component.Find("aside.banner-attention button.banner-action").ClickAsync(new MouseEventArgs());

        Received.InOrder(
            () =>
            {
                _bannerService.DismissAttention();
                _ = _menuActionService.OpenSettingsAsync();
            });
        _bannerService.DidNotReceive().ReportError(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task BannerHost_OpenSettingsReturnsFalse_DismissesAttentionImmediately_AndReportsRecoverableError()
    {
        // Dismiss-immediately semantics: the attention banner is gone the instant the user clicked, regardless
        // of outcome. When OpenSettingsAsync returns false (caught internally), surface a recoverable Error so
        // the user knows the click was received but the modal failed to open.
        _bannerService.AttentionEntries.Returns([BuildDatabaseEntry("a.db")]);
        _menuActionService.OpenSettingsAsync().Returns(Task.FromResult(false));

        var component = Render<BannerHost>();
        await component.Find("aside.banner-attention button.banner-action").ClickAsync(new MouseEventArgs());

        _bannerService.Received(1).DismissAttention();
        _bannerService.Received(1)
            .ReportError("Settings", Arg.Is<string>(s => s.Contains("Failed to open settings")));
        _bannerService.DidNotReceive().ReportCritical(Arg.Any<Exception>());
    }

    [Fact]
    public async Task BannerHost_OpenSettingsReturnsFalse_RendersNewErrorBanner_NotStaleAttention()
    {
        // Round-2 regression: without explicitly steering _selectedItem to the new error after ReportError,
        // ItemMatches preserves the stale Attention selection by (View=Attention, EntryId=null) and the user
        // never sees the failure message they need — the error banner would be one page back in the cycle.
        var attention = BuildDatabaseEntry("a.db");
        var newErrorId = BannerId.Create();
        var newError = new ErrorBannerEntry(
            newErrorId,
            "Settings",
            "Failed to open settings; try again from the menu.",
            null,
            null,
            DateTime.UtcNow);

        _bannerService.AttentionEntries.Returns([attention]);
        _menuActionService.OpenSettingsAsync().Returns(Task.FromResult(false));
        _bannerService.ReportError("Settings", Arg.Any<string>())
            .Returns(_ =>
            {
                // Simulate the real BannerService side effect: the new error joins the ErrorBanners list so
                // the next rebuild has both [Error, Attention] in the cycle.
                _bannerService.ErrorBanners.Returns([newError]);
                return newErrorId;
            });

        var component = Render<BannerHost>();
        Assert.Single(component.FindAll("aside.banner-attention"));

        await component.Find("aside.banner-attention button.banner-action").ClickAsync(new MouseEventArgs());
        // Real BannerService raises StateChanged from inside ReportError; the mock does not, so raise it here
        // to drive the re-render that proves _selectedItem was steered to the new error.
        _bannerService.StateChanged += Raise.Event<Action>();

        component.WaitForState(() => component.FindAll("aside.banner-error").Count > 0);

        var errorBanner = component.Find("aside.banner-error");
        Assert.Contains("Failed to open settings; try again from the menu.", errorBanner.TextContent);
        // Bug being prevented: stale Attention selection would render Attention here instead of Error.
        Assert.Empty(component.FindAll("aside.banner-attention"));
    }

    [Fact]
    public async Task BannerHost_OpenSettingsThrowsJSDisconnected_DismissesAttention_NoErrorReport()
    {
        // Per rule 3.9, JSDisconnectedException is expected during teardown and must be caught silently — it
        // does not warrant ReportError surface (the user closed the circuit themselves). The dismiss happened
        // before the await so the attention banner is already gone by the time the throw lands.
        _bannerService.AttentionEntries.Returns([BuildDatabaseEntry("a.db")]);
        _menuActionService.OpenSettingsAsync()
            .Returns(Task.FromException<bool>(new JSDisconnectedException("circuit gone")));

        var component = Render<BannerHost>();
        await component.Find("aside.banner-attention button.banner-action").ClickAsync(new MouseEventArgs());

        _bannerService.Received(1).DismissAttention();
        _bannerService.DidNotReceive().ReportError(Arg.Any<string>(), Arg.Any<string>());
        _bannerService.DidNotReceive().ReportCritical(Arg.Any<Exception>());
    }

    [Fact]
    public async Task BannerHost_OpenSettingsThrowsUnexpectedly_DismissesAttention_LogsAndReportsRecoverableError()
    {
        // Defensive path: contract says OpenSettingsAsync catches internally, but a synchronous throw before the
        // first await would still bubble. Must not propagate to ErrorBoundary (which would escalate the visible
        // banner from Attention to Critical). Surface as Error; attention was already dismissed on click.
        _bannerService.AttentionEntries.Returns([BuildDatabaseEntry("a.db")]);
        var openException = new InvalidOperationException("modal boom");
        _menuActionService.OpenSettingsAsync().Returns(Task.FromException<bool>(openException));

        var component = Render<BannerHost>();
        await component.Find("aside.banner-attention button.banner-action").ClickAsync(new MouseEventArgs());

        _bannerService.Received(1).DismissAttention();
        _bannerService.Received(1)
            .ReportError("Settings", Arg.Is<string>(s => s.Contains("modal boom")));
        _bannerService.DidNotReceive().ReportCritical(Arg.Any<Exception>());
        _traceLogger.Received(1).Error(Arg.Is<ErrorLogHandler>(h =>
            h.ToString().Contains(nameof(BannerHost)) && h.ToString().Contains("modal boom")));
    }

    [Fact]
    public async Task BannerHost_RecoveryThrows_ShowsRecoveryFailureSubtitle()
    {
        _bannerService.CurrentCritical.Returns(new InvalidOperationException("kaboom"));
        _bannerService.TryRecoverAsync().Returns(Task.FromException(new InvalidOperationException("recovery failed")));

        var component = Render<BannerHost>();
        await component.Find("aside.banner-critical .banner-actions button:nth-child(1)").ClickAsync(new MouseEventArgs());

        var subtitle = component.Find("aside.banner-critical .banner-feedback .banner-subtitle");
        Assert.Contains("Recovery failed", subtitle.TextContent);
        Assert.Contains("recovery failed", subtitle.TextContent);
    }

    [Fact]
    public async Task BannerHost_RelaunchClicked_InvokesTryRestartAsync()
    {
        _bannerService.CurrentCritical.Returns(new InvalidOperationException("kaboom"));
        _applicationRestartService.TryRestartAsync().Returns(true);

        var component = Render<BannerHost>();
        await component.Find("aside.banner-critical .banner-actions button:nth-child(2)").ClickAsync(new MouseEventArgs());

        await _applicationRestartService.Received(1).TryRestartAsync();
    }

    [Fact]
    public async Task BannerHost_RelaunchFails_ShowsRestartFailureSubtitle()
    {
        _bannerService.CurrentCritical.Returns(new InvalidOperationException("kaboom"));
        _applicationRestartService.TryRestartAsync().Returns(false);

        var component = Render<BannerHost>();
        await component.Find("aside.banner-critical .banner-actions button:nth-child(2)").ClickAsync(new MouseEventArgs());

        var subtitle = component.Find("aside.banner-critical .banner-feedback .banner-subtitle");
        Assert.Contains("Restart failed", subtitle.TextContent);
    }

    [Fact]
    public async Task BannerHost_ReloadClicked_InvokesTryRecoverAsync()
    {
        _bannerService.CurrentCritical.Returns(new InvalidOperationException("kaboom"));
        _bannerService.TryRecoverAsync().Returns(Task.CompletedTask);

        var component = Render<BannerHost>();
        await component.Find("aside.banner-critical .banner-actions button:nth-child(1)").ClickAsync(new MouseEventArgs());

        await _bannerService.Received(1).TryRecoverAsync();
    }

    [Fact]
    public void BannerHost_SingleErrorBanner_RendersWithoutPagination()
    {
        var entry = new ErrorBannerEntry(BannerId.Create(), "Database", "Schema invalid", null, null, DateTime.UtcNow);
        _bannerService.ErrorBanners.Returns([entry]);

        var component = Render<BannerHost>();

        var banner = component.Find("aside.banner-error");
        Assert.Contains("Database: Schema invalid", banner.TextContent);
        Assert.Empty(component.FindAll("aside.banner-error .banner-pagination"));
        Assert.Single(component.FindAll("aside.banner-error button.banner-dismiss"));
    }

    [Fact]
    public void BannerHost_WarningSeverity_RendersWarningStyledBanner()
    {
        var info = new BannerInfoEntry(BannerId.Create(),
            "Slow",
            "Performance dip",
            BannerSeverity.Warning,
            DateTime.UtcNow);

        _bannerService.InfoBanners.Returns([info]);

        var component = Render<BannerHost>();

        Assert.Single(component.FindAll("aside.banner.banner-warning"));
        Assert.Empty(component.FindAll("aside.banner.banner-info"));
    }

    private static DatabaseEntry BuildDatabaseEntry(string fileName) =>
        new(fileName, $@"C:\dbs\{fileName}", false, DatabaseStatus.UpgradeRequired);
}
