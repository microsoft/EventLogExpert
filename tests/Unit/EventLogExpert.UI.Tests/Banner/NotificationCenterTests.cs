// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using AngleSharp.Dom;
using Bunit;
using EventLogExpert.Localization;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.Database.Upgrade;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.UI.Alerts;
using EventLogExpert.UI.Banner;
using EventLogExpert.UI.DatabaseTools;
using EventLogExpert.UI.Modal;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using NSubstitute;
using System.Reflection;

namespace EventLogExpert.UI.Tests.Banner;

public sealed class NotificationCenterTests : BunitContext
{
    private readonly BannerService _service = new(Substitute.For<IDatabaseService>(), Substitute.For<ITraceLogger>());

    public NotificationCenterTests()
    {
        Services.AddBannerHostDependencies();
        Services.AddSingleton<IErrorBannerService>(_service);
        Services.AddSingleton<INotificationBulkDismissService>(_service);
        Services.AddSingleton<IInfoBannerService>(_service);
        Services.AddSingleton<ICriticalErrorService>(_service);
        Services.AddSingleton<ITraceLogger>(Substitute.For<ITraceLogger>());
        Services.AddSingleton<IStringLocalizer<SharedResource>>(new MarkerLocalizer());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task BeforeActionInterop_ToleratesDisposalFailuresOnlyAfterDisposal(bool objectDisposed, bool dispose)
    {
        var component = Render<NotificationCenter>();
        IJSObjectReference module = Substitute.For<IJSObjectReference>();
        TaskCompletionSource<IJSVoidResult> pendingInterop = new(TaskCreationOptions.RunContinuationsAsynchronously);
        module.InvokeAsync<IJSVoidResult>("prepareModalAction", Arg.Any<object?[]>())
            .Returns(_ => new ValueTask<IJSVoidResult>(pendingInterop.Task));
        typeof(NotificationCenter).GetField("_module", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component.Instance, module);
        MethodInfo prepare = typeof(NotificationCenter).GetMethod("PrepareModalActionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Task action = (Task)prepare.Invoke(component.Instance, null)!;
        if (dispose) { await component.Instance.DisposeAsync(); }
        Exception failure = objectDisposed ? new ObjectDisposedException("notification-module") : new JSException("Interop failed.");

        pendingInterop.SetException(failure);

        if (dispose) { await action; }
        else { Assert.Same(failure, await Assert.ThrowsAnyAsync<Exception>(() => action)); }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void ClearAllFooter_CountsDismissibleCardsNotRunningTasks(int dismissibleCount, bool expectedFooter)
    {
        for (int index = 0; index < dismissibleCount; index++)
        {
            _service.ReportError(new Preformatted($"Failure {index}", "Detail"));
        }
        Services.GetRequiredService<IExportProgressBannerService>().CurrentExport.Returns(new ExportProgressEntry(() => { }));
        var component = Render<NotificationCenter>();

        Assert.Equal(expectedFooter ? 1 : 0, component.FindAll(".notification-panel-footer").Count);
        if (expectedFooter)
        {
            var button = component.Find("[data-notification-clear-all]");
            Assert.Equal("[[Notifications_ClearAll]]", button.TextContent);
            Assert.Equal("[[Notifications_ClearAllAria]]", button.GetAttribute("aria-label"));
            Assert.Equal("[[Notifications_ClearAllDescription]]", button.GetAttribute("data-tooltip"));
            Assert.Equal("[[Notifications_ClearAllDescription]]",
                component.Find($"#{button.GetAttribute("aria-describedby")}").TextContent);
            Assert.Null(component.Find(".notification-panel-footer").ParentElement?.Closest(".notification-active-list"));
        }
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void ClearAllFooter_UsesOnlyListedAttentionForItsThreshold(bool omitted, int expectedFooterCount)
    {
        _service.ReportError(new Preformatted("First", "Detail"));
        Services.AddSingleton(Substitute.For<IMenuActionService>());
        IAttentionBannerService attention = Services.GetRequiredService<IAttentionBannerService>();
        attention.AttentionEntries.Returns([new DatabaseEntry("attention.db", "attention.db", true, DatabaseStatus.UpgradeRequired)]);
        Services.GetRequiredService<IModalCoordinator>().ActiveSession.Returns(
            new ModalSession(new ModalId(2), omitted ? typeof(DatabaseToolsModal) : typeof(ModalChrome), null));
        Services.GetRequiredService<IBannerCycleStateService>().SetModalContentDisplayed(true);
        var component = Render<NotificationCenter>(parameters => parameters.Add(center => center.Location, BannerHostLocation.InsideModal));

        Assert.Equal(expectedFooterCount, component.FindAll(".notification-panel-footer").Count);
    }

    [Theory]
    [InlineData("prepareModalAction", false, false)]
    [InlineData("prepareModalAction", true, false)]
    [InlineData("prepareModalAction", false, true)]
    [InlineData("prepareModalAction", true, true)]
    [InlineData("completeClearFocus", false, false)]
    [InlineData("completeClearFocus", true, false)]
    [InlineData("completeClearFocus", false, true)]
    [InlineData("completeClearFocus", true, true)]
    public async Task ClearAllInterop_ToleratesDisposalFailuresOnlyAfterDisposal(string identifier, bool objectDisposed, bool dispose)
    {
        _service.ReportError(new Preformatted("First", "Detail"));
        _service.ReportError(new Preformatted("Second", "Detail"));
        var component = Render<NotificationCenter>();
        IJSObjectReference module = Substitute.For<IJSObjectReference>();
        TaskCompletionSource<IJSVoidResult> pendingInterop = new(TaskCreationOptions.RunContinuationsAsynchronously);
        module.InvokeAsync<IJSVoidResult>(identifier, Arg.Any<object?[]>())
            .Returns(_ => new ValueTask<IJSVoidResult>(pendingInterop.Task));
        typeof(NotificationCenter).GetField("_module", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component.Instance, module);
        MethodInfo clearAll = typeof(NotificationCenter).GetMethod("ClearAllAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Task action = await component.InvokeAsync(() => Task.FromResult((Task)clearAll.Invoke(component.Instance, null)!));
        await module.Received(1).InvokeAsync<IJSVoidResult>(identifier, Arg.Any<object?[]>());
        if (dispose) { await component.Instance.DisposeAsync(); }
        Exception failure = objectDisposed ? new ObjectDisposedException("notification-module") : new JSException("Interop failed.");

        pendingInterop.SetException(failure);

        if (dispose) { await action; }
        else { Assert.Same(failure, await Assert.ThrowsAnyAsync<Exception>(() => action)); }
    }

    [Theory]
    [InlineData(0, "0 notifications cleared")]
    [InlineData(1, "1 notification cleared")]
    [InlineData(2, "2 notifications cleared")]
    public async Task ClearAll_AnnouncesTheActualCountExactlyOnce(int remainingAtAcceptance, string expectedAnnouncement)
    {
        Services.RemoveAll<IStringLocalizer<SharedResource>>();
        Services.AddEventLogLocalization();
        BannerId first = _service.ReportError(new Preformatted("First", "Detail"));
        BannerId second = _service.ReportError(new Preformatted("Second", "Detail"));
        TaskCompletionSource<bool> confirmation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Services.GetRequiredService<IAlertDialogService>()
            .ShowAlert("Clear notifications?", "Clear 2 notifications? They can't be restored.", "Clear all", "Cancel", AlertPresentation.Auto, true)
            .Returns(confirmation.Task);
        var component = Render<NotificationCenter>();
        Task click = component.Find("[data-notification-clear-all]").ClickAsync(new MouseEventArgs());
        if (remainingAtAcceptance < 2) { _service.DismissError(first); }
        if (remainingAtAcceptance == 0) { _service.DismissError(second); }
        confirmation.SetResult(true);
        await click;

        Assert.Equal(expectedAnnouncement, component.Find("[role='status']").TextContent);
        Assert.Empty(component.Find("[role='alert']").TextContent);
        Assert.Single(JSInterop.Invocations, invocation => invocation.Identifier == "completeClearFocus");
    }

    [Fact]
    public async Task ClearAll_AttentionConfirmationDescribesOnlyTheCapturedDatabases()
    {
        Services.RemoveAll<IStringLocalizer<SharedResource>>();
        Services.AddEventLogLocalization();
        IDatabaseService databases = Substitute.For<IDatabaseService>();
        databases.Entries.Returns([new DatabaseEntry("attention.db", "attention.db", true, DatabaseStatus.UpgradeRequired)]);
        BannerService service = new(databases, Substitute.For<ITraceLogger>());
        Services.AddSingleton<IAttentionBannerService>(service);
        Services.AddSingleton<IErrorBannerService>(service);
        Services.AddSingleton<IInfoBannerService>(service);
        Services.AddSingleton<INotificationBulkDismissService>(service);
        service.ReportError(new Preformatted("First", "Detail"));
        Services.AddSingleton(Substitute.For<IMenuActionService>());
        var component = Render<NotificationCenter>();

        await component.Find("[data-notification-clear-all]").ClickAsync(new MouseEventArgs());

        await Services.GetRequiredService<IAlertDialogService>().Received(1).ShowAlert(
            "Clear notifications?",
            "Clear 2 notifications? They can't be restored. Attention for these databases won't reappear this session. Database Tools still lists the databases.",
            "Clear all", "Cancel", AlertPresentation.Auto, true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClearAll_AttentionIsIncludedOnlyWhenListedInThisPanel(bool omitted)
    {
        IDatabaseService databases = Substitute.For<IDatabaseService>();
        databases.Entries.Returns([new DatabaseEntry("attention.db", "attention.db", true, DatabaseStatus.UpgradeRequired)]);
        BannerService service = new(databases, Substitute.For<ITraceLogger>());
        Services.AddSingleton<IAttentionBannerService>(service);
        Services.AddSingleton<IErrorBannerService>(service);
        Services.AddSingleton<IInfoBannerService>(service);
        Services.AddSingleton<INotificationBulkDismissService>(service);
        Services.AddSingleton(Substitute.For<IMenuActionService>());
        service.ReportError(new Preformatted("First", "Detail"));
        service.ReportError(new Preformatted("Recovery", "Backup", "Resolve") { IsRecoveryReminder = true }, () => Task.CompletedTask);
        Services.GetRequiredService<IBannerCycleStateService>().SetModalContentDisplayed(true);
        if (omitted)
        {
            IModalCoordinator coordinator = Services.GetRequiredService<IModalCoordinator>();
            coordinator.ActiveSession.Returns(new ModalSession(new ModalId(2), typeof(DatabaseToolsModal), null));
            coordinator.StateChanged += Raise.Event<Action>();
        }
        IAlertDialogService alerts = Services.GetRequiredService<IAlertDialogService>();
        alerts.ShowAlert(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), AlertPresentation.Auto, true).Returns(true);
        var component = Render<NotificationCenter>(parameters => parameters.Add(center => center.Location, BannerHostLocation.InsideModal));

        await component.Find("[data-notification-clear-all]").ClickAsync(new MouseEventArgs());

        Assert.Equal(!omitted, service.AttentionDismissed);
        Assert.Empty(service.ErrorBanners);
        string expectedMessage = "[[Notifications_ClearConfirmation]]"
            + (omitted ? string.Empty : "[[Notifications_ClearAttentionWarning]]")
            + "[[Notifications_ClearRecoveryWarning]][[Notifications_ClearDatabaseToolsHint]]";
        await alerts.Received(1).ShowAlert("[[Notifications_ClearTitle]]", expectedMessage,
            "[[Notifications_ClearAll]]", "[[Modal_Cancel]]", AlertPresentation.Auto, true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClearAll_CancelOrAcceptUsesTheActivationSnapshotAndCompletesFocus(bool accepted)
    {
        _service.ReportError(new Preformatted("First", "Detail"));
        _service.ReportInfoBanner(new Preformatted("Second", "Detail"), BannerSeverity.Info);
        TaskCompletionSource<bool> confirmation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IAlertDialogService alerts = Services.GetRequiredService<IAlertDialogService>();
        alerts.ShowAlert("[[Notifications_ClearTitle]]", "[[Notifications_ClearConfirmation]]",
            "[[Notifications_ClearAll]]", "[[Modal_Cancel]]", AlertPresentation.Auto, true).Returns(confirmation.Task);
        var component = Render<NotificationCenter>();

        Task click = component.Find("[data-notification-clear-all]").ClickAsync(new MouseEventArgs());
        _ = alerts.Received(1).ShowAlert("[[Notifications_ClearTitle]]", "[[Notifications_ClearConfirmation]]",
            "[[Notifications_ClearAll]]", "[[Modal_Cancel]]", AlertPresentation.Auto, true);
        Assert.Contains(JSInterop.Invocations, invocation => invocation.Identifier == "prepareModalAction");
        Assert.Single(_service.ErrorBanners);
        _service.ReportError(new Preformatted("Later", "Not in snapshot"));
        confirmation.SetResult(accepted);
        await click;

        Assert.Equal(accepted ? 1 : 2, _service.ErrorBanners.Count);
        Assert.Equal(accepted ? 0 : 1, _service.InfoBanners.Count);
        Assert.Equal("Later", ((Preformatted)_service.ErrorBanners[^1].Content).Title);
        var focus = Assert.Single(JSInterop.Invocations, invocation => invocation.Identifier == "completeClearFocus");
        Assert.Equal("remaining", focus.Arguments[1]);
        Assert.Equal(accepted ? 0 : 1, component.FindAll("[data-notification-clear-all]").Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClearAll_LastNoticesMoveFocusBeforeTheTriggerIsRetired(bool snapshotAlreadyGone)
    {
        BannerId first = _service.ReportError(new Preformatted("First", "Detail"));
        BannerId second = _service.ReportError(new Preformatted("Second", "Detail"));
        TaskCompletionSource<bool> confirmation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IAlertDialogService alerts = Services.GetRequiredService<IAlertDialogService>();
        alerts.ShowAlert(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), AlertPresentation.Auto, true)
            .Returns(confirmation.Task);
        var component = Render<NotificationCenter>();
        Task click = component.Find("[data-notification-clear-all]").ClickAsync(new MouseEventArgs());
        if (snapshotAlreadyGone)
        {
            _service.DismissError(first);
            _service.DismissError(second);
            Assert.DoesNotContain("notification-trigger-hidden", component.Find(".notification-trigger").ClassList);
        }
        confirmation.SetResult(true);
        await click;

        var identifiers = JSInterop.Invocations.Select(invocation => invocation.Identifier).ToList();
        Assert.True(identifiers.IndexOf("completeClearFocus") < identifiers.IndexOf("retireFocusBeforeHide"));
        Assert.Equal("empty", JSInterop.Invocations.Single(invocation => invocation.Identifier == "completeClearFocus").Arguments[1]);
        component.WaitForAssertion(() => Assert.Contains("notification-trigger-hidden", component.Find(".notification-trigger").ClassList));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClearAll_ProgressRemainsAndIsDescribedInTheSinglePoliteAnnouncement(bool insideModal)
    {
        Services.RemoveAll<IStringLocalizer<SharedResource>>();
        Services.AddEventLogLocalization();
        _service.ReportError(new Preformatted("First", "Detail"));
        _service.ReportError(new Preformatted("Second", "Detail"));
        bool canceled = false;
        Services.GetRequiredService<IExportProgressBannerService>().CurrentExport.Returns(new ExportProgressEntry(() => canceled = true));
        Services.GetRequiredService<IAlertDialogService>()
            .ShowAlert("Clear notifications?", "Clear 2 notifications? They can't be restored.", "Clear all", "Cancel", AlertPresentation.Auto, true)
            .Returns(true);
        if (insideModal)
        {
            Services.GetRequiredService<IModalCoordinator>().ActiveSession.Returns(new ModalSession(new ModalId(2), typeof(ModalChrome), null));
            Services.GetRequiredService<IBannerCycleStateService>().SetModalContentDisplayed(true);
        }
        var component = Render<NotificationCenter>(parameters => parameters
            .Add(center => center.Location, insideModal ? BannerHostLocation.InsideModal : BannerHostLocation.MainLayout));

        await component.Find("[data-notification-clear-all]").ClickAsync(new MouseEventArgs());

        Assert.False(canceled);
        Assert.Single(component.FindAll(".notification-card-progress"));
        Assert.Empty(component.FindAll("[data-notification-clear-all]"));
        Assert.Equal("2 notifications cleared; 1 task still running", component.Find("[role='status']").TextContent);
        Assert.Equal("remaining", JSInterop.Invocations.Single(invocation => invocation.Identifier == "completeClearFocus").Arguments[1]);
        Assert.DoesNotContain("notification-trigger-hidden", component.Find(".notification-trigger").ClassList);
    }

    [Fact]
    public async Task ClearAll_WithNoticePublishedBeforeConfirmationCompletes_AnnouncesBoth()
    {
        _service.ReportError(new Preformatted("First", "Detail"));
        _service.ReportError(new Preformatted("Second", "Detail"));
        ModalService modalService = new();
        using ModalCoordinator coordinator = new(modalService);
        Services.AddSingleton<IModalCoordinator>(coordinator);
        Services.GetRequiredService<IAlertDialogService>()
            .ShowAlert(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), AlertPresentation.Auto, true)
            .Returns(_ => modalService.Show<AlertModal, bool>());
        var component = Render<NotificationCenter>();
        Task click = component.Find("[data-notification-clear-all]").ClickAsync(new MouseEventArgs());
        Assert.Equal(typeof(AlertModal), modalService.ActiveModalType);
        await component.InvokeAsync(() => _service.ReportInfoBanner(new Preformatted("Import complete", "New database"), BannerSeverity.Info));

        await component.InvokeAsync(() => modalService.Complete(modalService.ActiveModalId, true));
        await click;

        Assert.Empty(_service.ErrorBanners);
        Assert.Single(_service.InfoBanners);
        string announcement = component.Find("[role='status']").TextContent;
        Assert.Contains("Notifications_Cleared", announcement);
        Assert.Contains("Import complete", announcement);
        Assert.Contains("Notifications_ActiveCount(1)", announcement);
    }

    [Fact]
    public void ContextuallySuppressedAttention_RemainsInTotalCountWithExplanation()
    {
        IAttentionBannerService attention = Services.GetRequiredService<IAttentionBannerService>();
        attention.AttentionEntries.Returns([new DatabaseEntry("attention.db", "attention.db", true, DatabaseStatus.UpgradeRequired)]);
        attention.AttentionDismissed.Returns(false);
        attention.AttentionCreatedUtc.Returns(new DateTime(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc));
        attention.StateChanged += Raise.Event<Action>();
        IModalCoordinator modalCoordinator = Services.GetRequiredService<IModalCoordinator>();
        modalCoordinator.ActiveSession.Returns(new ModalSession(new ModalId(2), typeof(DatabaseToolsModal), null));
        modalCoordinator.StateChanged += Raise.Event<Action>();
        Services.GetRequiredService<IBannerCycleStateService>().SetModalContentDisplayed(true);

        var component = Render<NotificationCenter>(parameters => parameters
            .Add(center => center.Location, BannerHostLocation.InsideModal));

        Assert.Equal("1", component.Find(".notification-count").TextContent);
        Assert.Empty(component.FindAll(".banner-attention"));
        Assert.Contains("[[Notifications_ContextualAttentionOmitted]]", component.Find(".notification-panel").TextContent);
    }

    [Fact]
    public async Task CriticalFailure_DisablesOrdinaryAccessWithoutReplacingLogicalCountWithZero()
    {
        _service.ReportError(new Preformatted("Failure", "Details"));
        var component = Render<NotificationCenter>();

        await component.InvokeAsync(() => _service.ReportCritical(new InvalidOperationException("Critical failure.")));

        Assert.True(component.Find(".notification-trigger").HasAttribute("disabled"));
        Assert.Equal("1", component.Find(".notification-count").TextContent);
        Assert.Contains(JSInterop.Invocations, invocation =>
            invocation.Identifier == "setUnavailable" && invocation.Arguments.Last() is true);
        Assert.Empty(component.FindAll(".notification-active-list aside"));
        Assert.Empty(component.Find("[role='status']").TextContent);
    }

    [Fact]
    public async Task DisposalDuringPendingPostRenderInterop_StopsWithoutFurtherModuleCalls()
    {
        var module = JSInterop.SetupModule("./_content/EventLogExpert.UI/Banner/NotificationCenter.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        var pendingUnavailable = module.SetupVoid("setUnavailable", _ => true);
        var component = Render<NotificationCenter>();
        int invocationsBeforeDisposal = module.Invocations.Count;

        await component.Instance.DisposeAsync();
        Assert.Single(pendingUnavailable.Invocations);
        pendingUnavailable.SetVoidResult();
        await component.InvokeAsync(() => { });

        Assert.False(Renderer.UnhandledException.IsCompleted);
        Assert.DoesNotContain(module.Invocations.Skip(invocationsBeforeDisposal),
            invocation => invocation.Identifier is "slotChanged" or "positionToast");
    }

    [Fact]
    public async Task Disposal_UnregistersPopoverAndUnsubscribesFromUpdates()
    {
        var component = Render<NotificationCenter>();

        await component.Instance.DisposeAsync();
        _service.ReportInfoBanner(new Preformatted("Later", "After disposal"), BannerSeverity.Info);

        Assert.Contains(JSInterop.Invocations, invocation => invocation.Identifier == "detach");
    }

    [Fact]
    public async Task DisposedCenter_IgnoresPopoverCallbacksAndCapturedStateEvents()
    {
        var component = Render<NotificationCenter>();
        MethodInfo stateChanged = typeof(NotificationCenter).GetMethod("OnStateChanged", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await component.Instance.DisposeAsync();
        int renders = component.RenderCount;

        await component.Instance.OnPopoverToggle(true);
        stateChanged.Invoke(component.Instance, null);
        await component.InvokeAsync(() => { });

        Assert.Equal(renders, component.RenderCount);
        Assert.False(Renderer.UnhandledException.IsCompleted);
    }

    [Fact]
    public async Task InactiveHost_KeepsEmptyInitializedLiveRegionsAndDoesNotDuplicateAnnouncements()
    {
        var main = Render<NotificationCenter>();
        var modal = Render<NotificationCenter>(parameters => parameters
            .Add(center => center.Location, BannerHostLocation.InsideModal));
        Assert.Equal("polite", modal.Find("[role='status']").GetAttribute("aria-live"));
        Assert.Equal("assertive", modal.Find("[role='alert']").GetAttribute("aria-live"));
        Assert.Empty(modal.Find("[role='status']").TextContent);
        Assert.Empty(modal.Find("[role='alert']").TextContent);

        await main.InvokeAsync(() => _service.ReportInfoBanner(new Preformatted("Main notice", "Detail"), BannerSeverity.Info));
        Assert.Contains("Main notice", main.Find("[role='status']").TextContent);
        Assert.Empty(modal.Find("[role='status']").TextContent);
        Services.GetRequiredService<IModalCoordinator>().ActiveSession.Returns(new ModalSession(new ModalId(2), typeof(ModalChrome), null));
        await main.InvokeAsync(() => Services.GetRequiredService<IBannerCycleStateService>().SetModalContentDisplayed(true));
        await modal.InvokeAsync(() => _service.ReportError(new Preformatted("Modal failure", "Detail")));

        Assert.Contains("Modal failure", modal.Find("[role='alert']").TextContent);
        Assert.Empty(main.Find("[role='status']").TextContent);
        Assert.Empty(main.Find("[role='alert']").TextContent);
        Assert.Equal("polite", main.Find("[role='status']").GetAttribute("aria-live"));
        Assert.Equal("assertive", main.Find("[role='alert']").GetAttribute("aria-live"));
    }

    [Fact]
    public void InformationWithoutHigherPriority_KeepsNeutralTextAndExposesItsSummaryAccessibly()
    {
        _service.ReportInfoBanner(new Preformatted("Information title", "Information detail"), BannerSeverity.Info);

        var component = Render<NotificationCenter>();
        var trigger = component.Find(".notification-trigger");

        Assert.Empty(trigger.QuerySelectorAll(".notification-label"));
        Assert.Equal("1", component.Find(".notification-count").TextContent);
        Assert.Contains("[[Notifications_Severity_Info]]", trigger.GetAttribute("aria-label"));
        Assert.Contains("[[Notifications_NoticeSummary(Information title|Information detail)]]", trigger.GetAttribute("aria-label"));
        Assert.Contains("[[Notifications_NoticeSummary(Information title|Information detail)]]", trigger.GetAttribute("data-tooltip"));
    }

    [Fact]
    public async Task ModalPanel_DescribesDisabledActionAndKeepsDismissAvailable()
    {
        bool invoked = false;
        _service.ReportError(new Preformatted("Failure", "Details", "Recover"), () =>
        {
            invoked = true;
            return Task.CompletedTask;
        });
        Services.GetRequiredService<IBannerCycleStateService>().SetModalContentDisplayed(true);
        var component = Render<NotificationCenter>(parameters => parameters
            .Add(center => center.Location, BannerHostLocation.InsideModal));

        var action = component.Find(".banner-action");
        Assert.True(action.HasAttribute("disabled"));
        Assert.Equal(component.Find(".banner-action-hint").GetAttribute("id"), action.GetAttribute("aria-describedby"));
        Assert.Contains("[[Notifications_CloseDialogForAction]]", component.Find(".banner-action-hint").TextContent);
        Assert.False(component.Find(".banner-dismiss").HasAttribute("disabled"));
        await component.InvokeAsync(() => action.Click());
        Assert.False(invoked);
    }

    [Fact]
    public async Task NoticeAnnouncement_NormalizesSentencePunctuation()
    {
        Services.RemoveAll<IStringLocalizer<SharedResource>>();
        Services.AddEventLogLocalization();
        var component = Render<NotificationCenter>();

        await component.InvokeAsync(() => _service.ReportInfoBanner(new Preformatted("Import complete.", "The database was imported."), BannerSeverity.Info));

        Assert.Equal("Information. Import complete. The database was imported. Active notifications: 1",
            component.Find("[role='status']").TextContent.Trim());
    }

    [Fact]
    public void PanelCards_SeparateContentAndExposeSeverityIconsForEveryNoticeKind()
    {
        _service.ReportError(new Preformatted("Failure title", "Failure detail", "Recover"), () => Task.CompletedTask);
        _service.ReportInfoBanner(new Preformatted("Information title", "Information detail"), BannerSeverity.Info);
        Services.AddSingleton(Substitute.For<IMenuActionService>());

        IAttentionBannerService attention = Services.GetRequiredService<IAttentionBannerService>();
        attention.AttentionEntries.Returns([new DatabaseEntry("attention.db", "attention.db", true, DatabaseStatus.UpgradeRequired)]);
        attention.AttentionDismissed.Returns(false);
        attention.AttentionCreatedUtc.Returns(new DateTime(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc));

        IProgressBannerService progressService = Services.GetRequiredService<IProgressBannerService>();
        progressService.BackgroundProgress.Returns(new BannerProgressEntry(
            new UpgradeBatchId(new Guid("9E8B4EB0-2590-4B6A-87CF-136A03CF4BF7")),
            UpgradeProgressScope.Background,
            2,
            5,
            "database.evtx",
            UpgradePhase.BackingUp,
            1,
            () => { }));

        IExportProgressBannerService exportProgressService = Services.GetRequiredService<IExportProgressBannerService>();
        exportProgressService.CurrentExport.Returns(new ExportProgressEntry(() => { }));

        var component = Render<NotificationCenter>();
        var cards = component.FindAll("article[data-notification-card]");
        Assert.Equal(5, cards.Count);

        var failureCard = component.Find("article.notification-card-error");
        AssertCardContent(failureCard, "Failure title", "Failure detail", "[[Notifications_Severity_Error]]", "bi-exclamation-circle");
        Assert.NotNull(failureCard.QuerySelector(".banner-action"));
        Assert.Null(failureCard.QuerySelector(".banner-action-hint"));

        var informationCard = component.Find("article.notification-card-info");
        AssertCardContent(informationCard, "Information title", "Information detail", "[[Notifications_Severity_Info]]", "bi-info-circle");
        Assert.Null(informationCard.QuerySelector(".banner-action, .banner-action-hint"));

        var attentionCard = component.Find("article.notification-card-warning");
        AssertCardContent(attentionCard, "[[Notifications_DatabaseAttention]]", "[[Banner_Attention]]", "[[Notifications_Severity_Warning]]", "bi-exclamation-triangle");
        Assert.NotNull(attentionCard.QuerySelector(".banner-action"));

        var upgradeCard = component.Find("article.notification-card-progress");
        AssertCardContent(upgradeCard, "[[Banner_Upgrade_Title]]", "[[Banner_Upgrade_InProgress(2|5|database.evtx|BackingUp)]] [[Banner_Upgrade_QueuedBatches]]", "[[Notifications_Severity_Progress]]", "bi-arrow-repeat", isProgress: true);
        Assert.NotNull(upgradeCard.QuerySelector(".banner-action"));

        var exportCard = cards.Single(card => card.QuerySelector(".banner-export-progress") is not null);
        AssertCardContent(exportCard, "[[Banner_Export_Title]]", "[[Banner_Export_Progress]]", "[[Notifications_Severity_Progress]]", "bi-arrow-repeat", isProgress: true);
        Assert.NotNull(exportCard.QuerySelector(".banner-action"));
    }

    [Fact]
    public async Task PanelControls_DescribeTheirOwnStableUniqueCardTitles()
    {
        _service.ReportError(new Preformatted("First failure", "Detail"));
        _service.ReportError(new Preformatted("Second failure", "Detail"));
        _service.ReportInfoBanner(new Preformatted("Information", "Detail"), BannerSeverity.Info);
        Services.AddSingleton(Substitute.For<IMenuActionService>());
        Services.GetRequiredService<IAttentionBannerService>().AttentionEntries.Returns([
            new DatabaseEntry("attention.db", "attention.db", true, DatabaseStatus.UpgradeRequired)
        ]);
        IExportProgressBannerService export = Services.GetRequiredService<IExportProgressBannerService>();
        export.CurrentExport.Returns(new ExportProgressEntry(() => { }));
        Services.GetRequiredService<IProgressBannerService>().BackgroundProgress.Returns(new BannerProgressEntry(
            UpgradeBatchId.Create(), UpgradeProgressScope.Background, 0, 1, string.Empty, UpgradePhase.BackingUp, 0, () => { }));
        var component = Render<NotificationCenter>();
        string[] titleIds = component.FindAll(".notification-card-title").Select(title => title.Id!).ToArray();

        Assert.All(titleIds, titleId => Assert.False(string.IsNullOrEmpty(titleId)));
        Assert.Equal(titleIds.Length, titleIds.Distinct(StringComparer.Ordinal).Count());
        foreach (var card in component.FindAll("[data-notification-card]"))
        {
            string titleId = card.QuerySelector(".notification-card-title")!.Id!;
            var control = card.QuerySelector(".banner-dismiss") ?? card.QuerySelector(".banner-action");
            if (control is not null) { Assert.Equal(titleId, control.GetAttribute("aria-describedby")); }
        }
        await component.InvokeAsync(() => export.StateChanged += Raise.Event<Action>());
        Assert.Equal(titleIds, component.FindAll(".notification-card-title").Select(title => title.Id));
    }

    [Fact]
    public void Panel_StaysNamedAndUsesStaticCardsWithoutHistoryOrHeader()
    {
        _service.ReportError(new Preformatted("First failure", "First detail"));
        _service.ReportInfoBanner(new Preformatted("Second notice", "Second detail"), BannerSeverity.Info);
        var component = Render<NotificationCenter>();
        var panel = component.Find("[data-notification-panel]");

        Assert.Equal("[[Notifications_Title]]", panel.GetAttribute("aria-label"));
        Assert.Equal("-1", panel.GetAttribute("tabindex"));
        Assert.Empty(panel.QuerySelectorAll("h2, h3, [data-notification-close]"));
        Assert.Equal(2, panel.QuerySelectorAll("[data-notification-card]").Length);
        Assert.Equal(2, panel.QuerySelectorAll("article[data-notification-card]").Length);
        Assert.Empty(panel.QuerySelectorAll("[role='alert'], [role='status'], [aria-live]:not([aria-live='off'])"));
        Assert.DoesNotContain("History", panel.TextContent);
        Assert.DoesNotContain("Active notifications", panel.TextContent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceChange_CallbackFailureIsObservedAndLoggedWithoutTearingDownTheRenderer(bool objectDisposed)
    {
        ICriticalErrorService criticalErrors = Substitute.For<ICriticalErrorService>();
        Services.AddSingleton(criticalErrors);
        var component = Render<NotificationCenter>();
        ITraceLogger traceLogger = Services.GetRequiredService<ITraceLogger>();
        Exception failure = objectDisposed ? new ObjectDisposedException("live-source") : new InvalidOperationException("Source refresh failed.");
        criticalErrors.CurrentCritical.Returns(_ => throw failure);

        Services.GetRequiredService<IBannerCycleStateService>().SetModalContentDisplayed(true);
        await component.InvokeAsync(() => { });

        traceLogger.Received(1).Error(Arg.Is<ErrorLogHandler>(handler =>
            handler.ToString().Contains(nameof(NotificationCenter)) && handler.ToString().Contains(failure.ToString())));
        Assert.False(Renderer.UnhandledException.IsCompleted);
    }

    [Fact]
    public void Toast_IsAriaHiddenAndNeverBecomesFocusableOrAnAnnouncer()
    {
        var component = Render<NotificationCenter>();
        _service.ReportInfoBanner(new Preformatted("New information", "An item was imported."), BannerSeverity.Info);
        component.WaitForAssertion(() => Assert.Contains("New information", component.Find(".notification-toast").TextContent));

        var toast = component.Find(".notification-toast");
        Assert.Equal("true", toast.GetAttribute("aria-hidden"));
        Assert.False(toast.HasAttribute("role"));
        Assert.False(toast.HasAttribute("tabindex"));
        Assert.Contains("bi-info-circle", toast.QuerySelector("i")?.GetAttribute("class"));
        Assert.Contains("New information", toast.QuerySelector(".notification-toast-title")?.TextContent);
        Assert.Contains("An item was imported.", toast.QuerySelector(".notification-toast-message")?.TextContent);
        Assert.Empty(toast.QuerySelectorAll(".notification-toast-severity, .notification-toast-more"));
    }

    [Fact]
    public void Toast_LocalizesCoalescedArrivalCount()
    {
        var component = Render<NotificationCenter>();
        _service.ReportInfoBanner(new Preformatted("First notice", "First detail"), BannerSeverity.Info);
        _service.ReportError(new Preformatted("Second notice", "Second detail"));

        component.WaitForAssertion(() =>
            Assert.Contains("[[Notifications_ToastMore(1)]]", component.Find(".notification-toast").TextContent));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TriggerRetirement_LiveFocusFailureLogsWarningAndStillRetires(bool objectDisposed)
    {
        BannerId failureId = _service.ReportError(new Preformatted("Retiring failure", "Detail"));
        var component = Render<NotificationCenter>();
        ITraceLogger traceLogger = Services.GetRequiredService<ITraceLogger>();
        var retirement = JSInterop.SetupVoid("retireFocusBeforeHide", _ => true);
        Exception failure = objectDisposed ? new ObjectDisposedException("retirement-module") : new JSException("Retirement focus failed.");

        _service.DismissError(failureId);
        component.WaitForAssertion(() => Assert.Single(retirement.Invocations));
        retirement.SetException(failure);

        component.WaitForAssertion(() =>
        {
            traceLogger.Received(1).Warning(Arg.Is<WarningLogHandler>(handler =>
                handler.ToString().Contains("retireFocusBeforeHide") && handler.ToString().Contains(failure.ToString())));
            Assert.Contains("notification-trigger-hidden", component.Find(".notification-trigger").ClassList);
        });
        Assert.False(Renderer.UnhandledException.IsCompleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TriggerRetirement_PendingFocusFailureAfterDisposalDoesNotLog(bool objectDisposed)
    {
        var component = Render<NotificationCenter>();
        ITraceLogger traceLogger = Services.GetRequiredService<ITraceLogger>();
        var pendingInterop = JSInterop.SetupVoid("retireFocusBeforeHide", _ => true);
        MethodInfo? retireTrigger = typeof(NotificationCenter).GetMethod("RetireTriggerAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(retireTrigger);
        Task retirement = Assert.IsAssignableFrom<Task>(retireTrigger.Invoke(component.Instance, [0L]));
        Assert.Single(pendingInterop.Invocations);
        await component.Instance.DisposeAsync();
        Exception failure = objectDisposed ? new ObjectDisposedException("disposed-retirement-module") : new JSException("Disposed retirement focus failed.");

        pendingInterop.SetException(failure);
        await retirement;

        traceLogger.DidNotReceive().Warning(Arg.Any<WarningLogHandler>());
        Assert.False(Renderer.UnhandledException.IsCompleted);
    }

    [Fact]
    public void TriggerSummary_UsesLocalizedSeparatorsAndCapsOnlyItsVisualCount()
    {
        Services.RemoveAll<IStringLocalizer<SharedResource>>();
        Services.AddEventLogLocalization();
        for (int index = 0; index < 100; index++)
        {
            _service.ReportInfoBanner(new Preformatted("Information", "Details."), BannerSeverity.Info);
        }
        Services.GetRequiredService<IExportProgressBannerService>().CurrentExport.Returns(new ExportProgressEntry(() => { }));

        var component = Render<NotificationCenter>();

        Assert.Equal("99+", component.Find(".notification-count").TextContent);
        Assert.Equal("Notifications: 101 active. Information. Information: Details. In progress",
            component.Find(".notification-trigger").GetAttribute("aria-label"));
    }

    [Fact]
    public void Trigger_HidesWhenEmptyWithoutRetiringOnInitialRender()
    {
        var component = Render<NotificationCenter>();
        var trigger = component.Find(".notification-trigger");

        Assert.Equal("true", trigger.GetAttribute("aria-hidden"));
        Assert.Equal("-1", trigger.GetAttribute("tabindex"));
        Assert.Contains("notification-trigger-hidden", trigger.GetAttribute("class"));
        var slot = component.Find(".status-bar-notifications");
        Assert.Equal("false", slot.GetAttribute("data-trigger-shown"));
        Assert.NotNull(slot.QuerySelector("[data-notification-panel]"));
        Assert.NotNull(slot.QuerySelector("[role='status']"));
        Assert.NotNull(slot.QuerySelector("[role='alert']"));
        Assert.NotNull(slot.QuerySelector("[data-notification-toast]"));
        Assert.Equal("auto", component.Find("[data-notification-panel]").GetAttribute("popover"));
        Assert.DoesNotContain(JSInterop.Invocations, invocation => invocation.Identifier == "retireFocusBeforeHide");
    }

    [Fact]
    public void Trigger_PrioritizesWarningThenProgressThenNotifications()
    {
        _service.ReportInfoBanner(new Preformatted("Warning title", "Warning detail"), BannerSeverity.Warning);
        var component = Render<NotificationCenter>();
        Assert.Contains("Warning title", component.Find(".notification-trigger").GetAttribute("data-tooltip"));
        Assert.Contains("bi-exclamation-triangle", component.Find(".notification-trigger i").GetAttribute("class"));

        _service.DismissInfoBanner(Assert.Single(_service.InfoBanners).Id);
        IExportProgressBannerService exportProgress = Services.GetRequiredService<IExportProgressBannerService>();
        exportProgress.CurrentExport.Returns(new ExportProgressEntry(() => { }));
        exportProgress.StateChanged += Raise.Event<Action>();
        component.WaitForAssertion(() => Assert.Contains("[[Notifications_InProgress]]", component.Find(".notification-trigger").GetAttribute("data-tooltip")));
        Assert.Contains("[[Notifications_Severity_Progress]]", component.Find(".notification-trigger").GetAttribute("aria-label"));
        Assert.Contains("bi-arrow-repeat", component.Find(".notification-trigger i").GetAttribute("class"));

        exportProgress.CurrentExport.Returns((ExportProgressEntry?)null);
        exportProgress.StateChanged += Raise.Event<Action>();
        component.WaitForAssertion(() => Assert.Contains("[[Notifications_Severity_None]]", component.Find(".notification-trigger").GetAttribute("aria-label")));
        Assert.Contains("bi-bell", component.Find(".notification-trigger i").GetAttribute("class"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Trigger_ShowsWhicheverWarningOrAttentionEpisodeIsNewer(bool attentionIsNewer)
    {
        _service.ReportInfoBanner(new Preformatted("Warning title", "Warning detail"), BannerSeverity.Warning);
        Services.AddSingleton(Substitute.For<IMenuActionService>());
        IAttentionBannerService attention = Services.GetRequiredService<IAttentionBannerService>();
        attention.AttentionEntries.Returns([new DatabaseEntry("attention.db", "attention.db", true, DatabaseStatus.UpgradeRequired)]);
        attention.AttentionDismissed.Returns(false);
        attention.AttentionCreatedUtc.Returns(new DateTime(attentionIsNewer ? 2030 : 2020, 2, 3, 4, 5, 6, DateTimeKind.Utc));

        var component = Render<NotificationCenter>();

        string triggerTooltip = component.Find(".notification-trigger").GetAttribute("data-tooltip")!;
        Assert.Equal(attentionIsNewer, triggerTooltip.Contains("[[Notifications_DatabaseAttention]]"));
        Assert.Equal(!attentionIsNewer, triggerTooltip.Contains("Warning title"));
    }

    [Fact]
    public void Trigger_StaysVisibleAcrossParentRenderAndIgnoresStaleRetirement()
    {
        _service.ReportError(new Preformatted("Current failure", "Current detail"));
        var component = Render<NotificationCenter>();
        IJSObjectReference module = Substitute.For<IJSObjectReference>();
        TaskCompletionSource<IJSVoidResult> retirement = new(TaskCreationOptions.RunContinuationsAsynchronously);
        module.InvokeAsync<IJSVoidResult>("retireFocusBeforeHide", Arg.Any<object?[]>())
            .Returns(new ValueTask<IJSVoidResult>(retirement.Task));
        FieldInfo? moduleField = typeof(NotificationCenter).GetField(
            "_module",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(moduleField);
        moduleField.SetValue(component.Instance, module);
        BannerId firstFailure = Assert.Single(_service.ErrorBanners).Id;

        _service.DismissError(firstFailure);
        component.WaitForAssertion(() => module.Received(1)
            .InvokeAsync<IJSVoidResult>("retireFocusBeforeHide", Arg.Any<object?[]>()));
        component.Render();
        Assert.DoesNotContain("notification-trigger-hidden", component.Find(".notification-trigger").GetAttribute("class"));

        _service.ReportError(new Preformatted("Replacement failure", "Replacement detail"));
        Assert.DoesNotContain("notification-trigger-hidden", component.Find(".notification-trigger").GetAttribute("class"));
        retirement.SetResult(Substitute.For<IJSVoidResult>());
        component.WaitForAssertion(() =>
            Assert.DoesNotContain("notification-trigger-hidden", component.Find(".notification-trigger").GetAttribute("class")));
    }

    [Fact]
    public void Trigger_UsesNewestFailureWithSeverityAndExactAccessibleCount()
    {
        for (int index = 0; index < 100; index++)
        {
            _service.ReportError(new Preformatted($"Failure {index}", $"Detail {index}"));
        }

        var component = Render<NotificationCenter>();
        var trigger = component.Find(".notification-trigger");

        Assert.False(trigger.HasAttribute("aria-hidden"));
        Assert.Equal("0", trigger.GetAttribute("tabindex"));
        Assert.Equal("[[Notifications_CountBadgeOverflow(99)]]", component.Find(".notification-count").TextContent);
        Assert.Contains("(100|", trigger.GetAttribute("aria-label"));
        Assert.Contains("[[Notifications_Severity_Error]]", trigger.GetAttribute("aria-label"));
        Assert.Contains("[[Notifications_NoticeSummary(Failure 99|Detail 99)]]", trigger.GetAttribute("aria-label"));
        Assert.Contains("[[Notifications_NoticeSummary(Failure 99|Detail 99)]]", trigger.GetAttribute("data-tooltip"));
        Assert.Contains("(100|", trigger.GetAttribute("data-tooltip"));
        Assert.Equal("[[Notifications_CountBadgeOverflow(99)]]", trigger.TextContent.Trim());
        Assert.Empty(trigger.QuerySelectorAll(".notification-label"));
        Assert.Contains("bi-exclamation-circle", trigger.QuerySelector("i")?.GetAttribute("class"));
    }

    [Fact]
    public void Trigger_WithTiedFailureTimestamps_DescribesTheLatestArrival()
    {
        DateTime createdUtc = new(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        IErrorBannerService errors = Substitute.For<IErrorBannerService>();
        errors.ErrorBanners.Returns([
            new ErrorBannerEntry(BannerId.Create(), new Preformatted("Earlier failure", "First detail"), null, createdUtc),
            new ErrorBannerEntry(BannerId.Create(), new Preformatted("Latest failure", "Last detail"), null, createdUtc)
        ]);
        Services.AddSingleton(errors);
        var component = Render<NotificationCenter>();

        var trigger = component.Find(".notification-trigger");
        Assert.Contains("[[Notifications_NoticeSummary(Latest failure|Last detail)]]", trigger.GetAttribute("aria-label"));
        Assert.Contains("[[Notifications_NoticeSummary(Latest failure|Last detail)]]", trigger.GetAttribute("data-tooltip"));
        Assert.DoesNotContain("Earlier failure", trigger.GetAttribute("aria-label"));
    }

    private static void AssertCardContent(
        IElement card,
        string expectedTitle,
        string expectedMessage,
        string expectedSeverity,
        string expectedIconClass,
        bool isProgress = false)
    {
        Assert.Equal(expectedTitle, card.QuerySelector(".notification-card-title")?.TextContent.Trim());
        Assert.Equal(expectedMessage, card.QuerySelector(".notification-card-message")?.TextContent.Trim());
        Assert.Equal(expectedSeverity, card.QuerySelector(".notification-card-severity")?.TextContent.Trim());

        var icon = card.QuerySelector(".notification-card-icon");
        Assert.NotNull(icon);
        Assert.Contains(expectedIconClass, icon.GetAttribute("class"));
        Assert.Equal("true", icon.GetAttribute("aria-hidden"));
        Assert.Equal(expectedSeverity, icon.GetAttribute("data-tooltip"));
        Assert.Null(icon.GetAttribute("title"));

        if (isProgress)
        {
            Assert.Null(card.QuerySelector(".banner-dismiss"));
            Assert.Equal("[[Modal_Cancel]]", card.QuerySelector(".banner-action")?.TextContent.Trim());
        }
        else
        {
            Assert.NotNull(card.QuerySelector(".banner-dismiss[aria-label]"));
        }
    }
}
