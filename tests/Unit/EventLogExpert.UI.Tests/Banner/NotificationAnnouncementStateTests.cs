// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.Database.Upgrade;
using EventLogExpert.UI.Alerts;
using EventLogExpert.UI.Banner;
using EventLogExpert.UI.Modal;
using EventLogExpert.UI.Tests.TestUtils;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Banner;

public sealed class NotificationAnnouncementStateTests : IDisposable
{
    private readonly NotificationAnnouncementState _announcementState;
    private readonly IAttentionBannerService _attentionBannerService = Substitute.For<IAttentionBannerService>();
    private readonly ICriticalErrorService _criticalErrorService = Substitute.For<ICriticalErrorService>();
    private readonly IErrorBannerService _errorBannerService = Substitute.For<IErrorBannerService>();
    private readonly IExportProgressBannerService _exportProgressBannerService = Substitute.For<IExportProgressBannerService>();
    private readonly IInfoBannerService _infoBannerService = Substitute.For<IInfoBannerService>();
    private readonly IModalCoordinator _modalCoordinator = Substitute.For<IModalCoordinator>();
    private readonly IProgressBannerService _progressBannerService = Substitute.For<IProgressBannerService>();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2025, 2, 3, 4, 5, 6, TimeSpan.Zero));

    public NotificationAnnouncementStateTests()
    {
        _attentionBannerService.AttentionEntries.Returns([]);
        _attentionBannerService.AttentionDismissed.Returns(false);
        _attentionBannerService.AttentionCreatedUtc.Returns(new DateTime(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc));
        _criticalErrorService.CurrentCritical.Returns((Exception?)null);
        _errorBannerService.ErrorBanners.Returns([]);
        _exportProgressBannerService.CurrentExport.Returns((ExportProgressEntry?)null);
        _infoBannerService.InfoBanners.Returns([]);
        _modalCoordinator.ActiveSession.Returns((ModalSession?)null);
        _progressBannerService.BackgroundProgress.Returns((BannerProgressEntry?)null);

        _announcementState = new NotificationAnnouncementState(
            _attentionBannerService,
            _criticalErrorService,
            _errorBannerService,
            _exportProgressBannerService,
            _infoBannerService,
            _modalCoordinator,
            _progressBannerService,
            new MarkerLocalizer(),
            _timeProvider);
    }

    [Fact]
    public void AttentionToast_UsesEpisodeStartToChooseTheNewestNotice()
    {
        object activeHost = new();
        _announcementState.SetHostAvailability(activeHost, true, false);
        DateTime attentionStart = new(2025, 2, 3, 5, 0, 0, DateTimeKind.Utc);
        _attentionBannerService.AttentionEntries.Returns([
            new DatabaseEntry("attention.db", "attention.db", true, DatabaseStatus.UpgradeRequired)
        ]);
        _attentionBannerService.AttentionCreatedUtc.Returns(attentionStart);
        _infoBannerService.InfoBanners.Returns([CreateInfo("Earlier warning", "Earlier detail") with { Severity = BannerSeverity.Warning }]);

        _infoBannerService.StateChanged += Raise.Event<Action>();

        NotificationToast toast = Assert.IsType<NotificationToast>(_announcementState.GetToast(activeHost));
        Assert.Equal("[[Notifications_DatabaseAttention]]", toast.Title);
        Assert.Equal(NotificationSeverity.Warning, toast.Severity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClearAnnouncement_DoesNotReplayANoticePublishedBeforeTheTransaction(bool canceledPreviousClear)
    {
        object activeHost = new();
        _announcementState.SetHostAvailability(activeHost, true, false);
        if (canceledPreviousClear) { _announcementState.BeginClear(); }
        _infoBannerService.InfoBanners.Returns([CreateInfo("Earlier notice", "Earlier detail")]);
        _infoBannerService.StateChanged += Raise.Event<Action>();
        if (canceledPreviousClear) { _announcementState.EndClear(); }

        _announcementState.BeginClear();
        _announcementState.AnnounceCleared(2);
        _announcementState.EndClear();

        Assert.Equal("[[Notifications_Cleared]]", _announcementState.GetAnnouncements(activeHost).Polite);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClearAnnouncement_DoesNotReplayARemovedOrSupersededNotice(bool replacedByFailure)
    {
        object activeHost = new();
        _announcementState.SetHostAvailability(activeHost, true, false);
        _announcementState.BeginClear();
        _infoBannerService.InfoBanners.Returns([CreateInfo("Earlier notice", "Earlier detail")]);
        _infoBannerService.StateChanged += Raise.Event<Action>();
        if (replacedByFailure)
        {
            _errorBannerService.ErrorBanners.Returns([CreateError("Latest failure", "Failure detail")]);
            _errorBannerService.StateChanged += Raise.Event<Action>();
        }
        else
        {
            _infoBannerService.InfoBanners.Returns([]);
            _infoBannerService.StateChanged += Raise.Event<Action>();
        }

        _announcementState.AnnounceCleared(2);
        _announcementState.EndClear();

        Assert.Equal("[[Notifications_Cleared]]", _announcementState.GetAnnouncements(activeHost).Polite);
        if (replacedByFailure) { Assert.Contains("Latest failure", _announcementState.GetAnnouncements(activeHost).Assertive); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClearAnnouncement_WithNoticeArrivingDuringConfirmation_AnnouncesBoth(bool resumeBeforeClear)
    {
        object activeHost = new();
        _announcementState.SetHostAvailability(activeHost, true, false);
        _announcementState.BeginClear();
        _modalCoordinator.ActiveSession.Returns(new ModalSession(new ModalId(4), typeof(AlertModal), null));
        _modalCoordinator.StateChanged += Raise.Event<Action>();
        _infoBannerService.InfoBanners.Returns([CreateInfo("Import complete", "One database was imported.")]);
        _infoBannerService.StateChanged += Raise.Event<Action>();

        if (resumeBeforeClear)
        {
            _modalCoordinator.ActiveSession.Returns((ModalSession?)null);
            _modalCoordinator.StateChanged += Raise.Event<Action>();
        }
        _announcementState.AnnounceCleared(2);
        _announcementState.EndClear();
        if (!resumeBeforeClear)
        {
            _modalCoordinator.ActiveSession.Returns((ModalSession?)null);
            _modalCoordinator.StateChanged += Raise.Event<Action>();
        }

        string? polite = _announcementState.GetAnnouncements(activeHost).Polite;
        Assert.Contains("Notifications_Cleared", polite);
        Assert.Contains("Import complete", polite);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Construction_ReconcilesPublicationDuringSubscription(bool raisesEvent)
    {
        _attentionBannerService.When(service => service.StateChanged += Arg.Any<Action>()).Do(call =>
        {
            _infoBannerService.InfoBanners.Returns([CreateInfo("Construction notice", "Arrived during subscription")]);
            if (raisesEvent) { call.Arg<Action>()(); }
        });

        using NotificationAnnouncementState state = new(
            _attentionBannerService, _criticalErrorService, _errorBannerService, _exportProgressBannerService,
            _infoBannerService, _modalCoordinator, _progressBannerService, new MarkerLocalizer(), _timeProvider);
        object host = new();
        state.SetHostAvailability(host, true, false);

        Assert.Contains("Construction notice", state.GetAnnouncements(host).Polite);
    }

    [Fact]
    public void CriticalTakeover_DefersAndResumesFailureOnce()
    {
        object activeHost = new();
        _announcementState.SetHostAvailability(activeHost, true, false);
        _criticalErrorService.CurrentCritical.Returns(new InvalidOperationException("Critical takeover"));
        _criticalErrorService.StateChanged += Raise.Event<Action>();
        _errorBannerService.ErrorBanners.Returns([CreateError("Deferred failure", "Failure detail")]);
        _errorBannerService.StateChanged += Raise.Event<Action>();
        int announcementCount = 0;
        _announcementState.StateChanged += () => announcementCount++;

        Assert.Equal((null, null), _announcementState.GetAnnouncements(activeHost));

        _criticalErrorService.CurrentCritical.Returns((Exception?)null);
        _criticalErrorService.StateChanged += Raise.Event<Action>();
        string? announcement = _announcementState.GetAnnouncements(activeHost).Assertive;
        _criticalErrorService.StateChanged += Raise.Event<Action>();

        Assert.Contains("Deferred failure", announcement);
        Assert.Equal(1, announcementCount);
    }

    [Fact]
    public void DeferredFailureAndPoliteNotice_ResumesOnlyFailureAndKeepsItThroughTheNextPoliteArrival()
    {
        object activeHost = new();
        _announcementState.SetHostAvailability(activeHost, true, false);
        _modalCoordinator.ActiveSession.Returns(new ModalSession(new ModalId(3), typeof(PromptModal), null));
        _modalCoordinator.StateChanged += Raise.Event<Action>();
        _errorBannerService.ErrorBanners.Returns([CreateError("Deferred failure", "Failure detail")]);
        _infoBannerService.InfoBanners.Returns([CreateInfo("Deferred notice", "Deferred detail")]);
        _infoBannerService.StateChanged += Raise.Event<Action>();

        _modalCoordinator.ActiveSession.Returns((ModalSession?)null);
        _modalCoordinator.StateChanged += Raise.Event<Action>();
        Assert.Null(_announcementState.GetAnnouncements(activeHost).Polite);
        Assert.Contains("Deferred failure", _announcementState.GetAnnouncements(activeHost).Assertive);

        _infoBannerService.InfoBanners.Returns([CreateInfo("New notice", "New detail")]);
        _infoBannerService.StateChanged += Raise.Event<Action>();

        (string? polite, string? assertive) = _announcementState.GetAnnouncements(activeHost);
        Assert.Contains("Deferred failure", assertive);
        Assert.Contains("New notice", polite);
    }

    [Fact]
    public void DeferredPoliteNotices_WithTiedTimestamps_AnnounceTheLatestArrival()
    {
        object host = new();
        _announcementState.SetHostAvailability(host, true, false);
        _criticalErrorService.CurrentCritical.Returns(new InvalidOperationException("Takeover"));
        _criticalErrorService.StateChanged += Raise.Event<Action>();
        _infoBannerService.InfoBanners.Returns([
            CreateInfo("Earlier notice", "First detail"), CreateInfo("Latest notice", "Last detail")
        ]);
        _infoBannerService.StateChanged += Raise.Event<Action>();

        _criticalErrorService.CurrentCritical.Returns((Exception?)null);
        _criticalErrorService.StateChanged += Raise.Event<Action>();

        Assert.Contains("Latest notice", _announcementState.GetAnnouncements(host).Polite);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeferredProgress_UsesTheInjectedClockToChooseTheLatestNotice(bool export)
    {
        object activeHost = new();
        _announcementState.SetHostAvailability(activeHost, true, false);
        _modalCoordinator.ActiveSession.Returns(new ModalSession(new ModalId(4), typeof(AlertModal), null));
        _modalCoordinator.StateChanged += Raise.Event<Action>();
        SetProgress(export, true);
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        _infoBannerService.InfoBanners.Returns([CreateInfo("Later notice", "Later detail") with { CreatedUtc = _timeProvider.GetUtcNow().UtcDateTime }]);
        _infoBannerService.StateChanged += Raise.Event<Action>();

        _modalCoordinator.ActiveSession.Returns((ModalSession?)null);
        _modalCoordinator.StateChanged += Raise.Event<Action>();

        Assert.Contains("Later notice", _announcementState.GetAnnouncements(activeHost).Polite);
    }

    public void Dispose() => _announcementState.Dispose();

    [Fact]
    public void FailureAndNewerNoticeObservedTogether_AnnouncesBoth()
    {
        object activeHost = new();
        _announcementState.SetHostAvailability(activeHost, true, false);
        _errorBannerService.ErrorBanners.Returns([CreateError("Save failed", "The filter set was not saved.")]);
        _infoBannerService.InfoBanners.Returns([CreateInfo("Import complete", "One database was imported.")]);

        _infoBannerService.StateChanged += Raise.Event<Action>();

        (string? polite, string? assertive) = _announcementState.GetAnnouncements(activeHost);
        Assert.Contains("Save failed", assertive);
        Assert.Contains("Import complete", polite);
    }

    [Fact]
    public void InlineAlertTakeover_DefersFailureUntilTheMainCenterReturns()
    {
        object activeHost = new();
        _announcementState.SetHostAvailability(activeHost, true, false);
        _announcementState.SetHostAvailability(activeHost, false, false);
        _errorBannerService.ErrorBanners.Returns([CreateError("Inline alert failure", "Failure detail")]);
        _errorBannerService.StateChanged += Raise.Event<Action>();

        Assert.Equal((null, null), _announcementState.GetAnnouncements(activeHost));

        _announcementState.SetHostAvailability(activeHost, true, false);

        Assert.Contains("Inline alert failure", _announcementState.GetAnnouncements(activeHost).Assertive);
    }

    [Fact]
    public void NewError_UsesAssertiveRegionAndDeduplicatesRepeatedSourceUpdates()
    {
        object activeHost = new();
        _announcementState.SetHostAvailability(activeHost, true, false);
        int announcementCount = 0;
        _announcementState.StateChanged += () => announcementCount++;
        ErrorBannerEntry failure = CreateError("Disk is unavailable", "The log could not be opened.");
        _errorBannerService.ErrorBanners.Returns([failure]);

        _errorBannerService.StateChanged += Raise.Event<Action>();
        _errorBannerService.StateChanged += Raise.Event<Action>();

        (string? polite, string? assertive) = _announcementState.GetAnnouncements(activeHost);
        Assert.Null(polite);
        Assert.Contains("Disk is unavailable", assertive);
        Assert.Contains("The log could not be opened", assertive);
        Assert.Contains("[[Notifications_Severity_Error]]", assertive);
        Assert.Equal(1, announcementCount);
    }

    [Fact]
    public void NewInformation_UsesPoliteRegionAndOnlyTheActiveHostReceivesIt()
    {
        object mainHost = new();
        object modalHost = new();
        _announcementState.SetHostAvailability(mainHost, true, false);
        _announcementState.SetHostAvailability(modalHost, true, true);
        _infoBannerService.InfoBanners.Returns([CreateInfo("Import complete", "One database was imported.")]);

        _infoBannerService.StateChanged += Raise.Event<Action>();

        (string? polite, string? assertive) = _announcementState.GetAnnouncements(modalHost);
        Assert.Contains("Import complete", polite);
        Assert.Contains("One database was imported", polite);
        Assert.Contains("[[Notifications_Severity_Info]]", polite);
        Assert.Null(assertive);
        Assert.Equal((null, null), _announcementState.GetAnnouncements(mainHost));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProgressRestart_UsesNewEpisodeTimestampEvenWhenEntryIsReused(bool export)
    {
        object activeHost = new();
        _announcementState.SetHostAvailability(activeHost, true, false);
        SetProgress(export, true);
        BannerProgressEntry? backgroundEntry = _progressBannerService.BackgroundProgress;
        ExportProgressEntry? exportEntry = _exportProgressBannerService.CurrentExport;
        _modalCoordinator.ActiveSession.Returns(new ModalSession(new ModalId(4), typeof(AlertModal), null));
        _modalCoordinator.StateChanged += Raise.Event<Action>();
        SetProgress(export, false);
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        _infoBannerService.InfoBanners.Returns([CreateInfo("Earlier notice", "Earlier detail") with { CreatedUtc = _timeProvider.GetUtcNow().UtcDateTime }]);
        _infoBannerService.StateChanged += Raise.Event<Action>();
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        _progressBannerService.BackgroundProgress.Returns(backgroundEntry);
        _exportProgressBannerService.CurrentExport.Returns(exportEntry);
        if (export) { _exportProgressBannerService.StateChanged += Raise.Event<Action>(); }
        else { _progressBannerService.StateChanged += Raise.Event<Action>(); }

        _modalCoordinator.ActiveSession.Returns((ModalSession?)null);
        _modalCoordinator.StateChanged += Raise.Event<Action>();

        Assert.Contains(export ? "Notifications_ExportStarted" : "Notifications_UpgradeStarted", _announcementState.GetAnnouncements(activeHost).Polite);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProgressStart_AnnouncesProgressSeverityAndTheSpecificTask(bool export)
    {
        object host = new();
        _announcementState.SetHostAvailability(host, true, false);

        SetProgress(export, true);

        string? announcement = _announcementState.GetAnnouncements(host).Polite;
        Assert.Contains("Notifications_Severity_Progress", announcement);
        Assert.Contains(export ? "Banner_Export_Title" : "Banner_Upgrade_Title", announcement);
        Assert.DoesNotContain("Notifications_Severity_Info", announcement);
    }

    [Fact]
    public void ProgressTicks_DoNotReplaceTheSingleStartAnnouncement()
    {
        object activeHost = new();
        _announcementState.SetHostAvailability(activeHost, true, false);
        var progress = new BannerProgressEntry(
            new UpgradeBatchId(new Guid("9E8B4EB0-2590-4B6A-87CF-136A03CF4BF7")),
            UpgradeProgressScope.Background,
            0,
            2,
            string.Empty,
            UpgradePhase.BackingUp,
            0,
            () => { });
        _progressBannerService.BackgroundProgress.Returns(progress);
        int announcementCount = 0;
        _announcementState.StateChanged += () => announcementCount++;

        _progressBannerService.StateChanged += Raise.Event<Action>();
        string? startAnnouncement = _announcementState.GetAnnouncements(activeHost).Polite;
        _progressBannerService.BackgroundProgress.Returns(progress with
        {
            CurrentBatchPosition = 1,
            CurrentEntryName = "Next database"
        });
        _progressBannerService.StateChanged += Raise.Event<Action>();

        Assert.Contains("[[Notifications_UpgradeStarted]]", startAnnouncement);
        Assert.Equal(startAnnouncement, _announcementState.GetAnnouncements(activeHost).Polite);
        Assert.Null(_announcementState.GetToast(activeHost));
        Assert.Equal(1, announcementCount);
    }

    [Fact]
    public void PromptTakeover_DefersFailuresAndAnnouncesLatestOutstandingFailureOnResume()
    {
        object activeHost = new();
        _announcementState.SetHostAvailability(activeHost, true, false);
        _modalCoordinator.ActiveSession.Returns(new ModalSession(new ModalId(3), typeof(PromptModal), null));
        _modalCoordinator.StateChanged += Raise.Event<Action>();
        ErrorBannerEntry firstFailure = CreateError("Earlier failure", "Earlier detail");
        ErrorBannerEntry latestFailure = CreateError("Latest failure", "Latest detail");
        _errorBannerService.ErrorBanners.Returns([firstFailure]);
        _errorBannerService.StateChanged += Raise.Event<Action>();
        _errorBannerService.ErrorBanners.Returns([firstFailure, latestFailure]);
        _errorBannerService.StateChanged += Raise.Event<Action>();

        Assert.Equal((null, null), _announcementState.GetAnnouncements(activeHost));

        _modalCoordinator.ActiveSession.Returns((ModalSession?)null);
        _modalCoordinator.StateChanged += Raise.Event<Action>();

        string? resumedAnnouncement = _announcementState.GetAnnouncements(activeHost).Assertive;
        Assert.Contains("Latest failure", resumedAnnouncement);
        Assert.DoesNotContain("Earlier failure", resumedAnnouncement);
    }

    [Fact]
    public void RepeatedIdenticalFailure_ChangesTheLiveRegionEachTime()
    {
        object activeHost = new();
        _announcementState.SetHostAvailability(activeHost, true, false);
        List<string?> announcements = [];

        for (int attempt = 0; attempt < 3; attempt++)
        {
            _errorBannerService.ErrorBanners.Returns([CreateError("Save failed", "The filter set was not saved.")]);
            _errorBannerService.StateChanged += Raise.Event<Action>();
            announcements.Add(_announcementState.GetAnnouncements(activeHost).Assertive);
            _errorBannerService.ErrorBanners.Returns([]);
            _errorBannerService.StateChanged += Raise.Event<Action>();
        }

        Assert.All(announcements, announcement => Assert.Contains("Save failed", announcement));
        Assert.NotEqual(announcements[0], announcements[1]);
        Assert.NotEqual(announcements[1], announcements[2]);
    }

    [Fact]
    public void SameCountErrorReplacement_AnnouncesNewMaterialFailureContent()
    {
        object activeHost = new();
        _announcementState.SetHostAvailability(activeHost, true, false);
        ErrorBannerEntry firstFailure = CreateError("First failure", "First detail");
        _errorBannerService.ErrorBanners.Returns([firstFailure]);
        _errorBannerService.StateChanged += Raise.Event<Action>();
        string? firstAnnouncement = _announcementState.GetAnnouncements(activeHost).Assertive;

        ErrorBannerEntry replacementFailure = CreateError("Replacement failure", "Replacement detail");
        _errorBannerService.ErrorBanners.Returns([replacementFailure]);
        _errorBannerService.StateChanged += Raise.Event<Action>();
        string? replacementAnnouncement = _announcementState.GetAnnouncements(activeHost).Assertive;

        Assert.NotEqual(firstFailure.Id, replacementFailure.Id);
        Assert.Contains("Replacement failure", replacementAnnouncement);
        Assert.NotEqual(firstAnnouncement, replacementAnnouncement);
    }

    [Theory]
    [InlineData(nameof(NotificationSeverity.Info), true)]
    [InlineData(nameof(NotificationSeverity.Warning), true)]
    [InlineData(nameof(NotificationSeverity.Progress), true)]
    [InlineData(nameof(NotificationSeverity.Info), false)]
    [InlineData(nameof(NotificationSeverity.Warning), false)]
    [InlineData(nameof(NotificationSeverity.Progress), false)]
    public void SequentialSourceUpdates_KeepBothChannelsUntilEachGetsAReplacement(string politeSeverity, bool failureFirst)
    {
        object activeHost = new();
        _announcementState.SetHostAvailability(activeHost, true, false);
        ErrorBannerEntry failure = CreateError("Save failed", "The filter set was not saved.");
        BannerInfoEntry politeNotice = CreateInfo("Import complete", "One database was imported.") with
        {
            Severity = politeSeverity == nameof(NotificationSeverity.Warning) ? BannerSeverity.Warning : BannerSeverity.Info
        };

        if (failureFirst)
        {
            _errorBannerService.ErrorBanners.Returns([failure]);
            _errorBannerService.StateChanged += Raise.Event<Action>();
        }

        if (politeSeverity == nameof(NotificationSeverity.Progress))
        {
            SetProgress(export: false, running: true);
        }
        else
        {
            _infoBannerService.InfoBanners.Returns([politeNotice]);
            _infoBannerService.StateChanged += Raise.Event<Action>();
        }

        if (!failureFirst)
        {
            _errorBannerService.ErrorBanners.Returns([failure]);
            _errorBannerService.StateChanged += Raise.Event<Action>();
        }

        (string? polite, string? assertive) = _announcementState.GetAnnouncements(activeHost);
        Assert.Contains("Save failed", assertive);
        Assert.Contains(politeSeverity == nameof(NotificationSeverity.Progress) ? "Notifications_UpgradeStarted" : "Import complete", polite);
    }

    [Fact]
    public void Toast_BurstExpiresAtTwelveSecondsDespiteRepeatedSixSecondRestarts()
    {
        object activeHost = new();
        _announcementState.SetHostAvailability(activeHost, true, false);
        _infoBannerService.InfoBanners.Returns([CreateInfo("First notice", "First detail")]);
        _infoBannerService.StateChanged += Raise.Event<Action>();

        _timeProvider.Advance(TimeSpan.FromSeconds(5));
        _errorBannerService.ErrorBanners.Returns([CreateError("Second notice", "Second detail")]);
        _errorBannerService.StateChanged += Raise.Event<Action>();

        _timeProvider.Advance(TimeSpan.FromSeconds(5));
        _infoBannerService.InfoBanners.Returns([CreateInfo("Third notice", "Third detail")]);
        _infoBannerService.StateChanged += Raise.Event<Action>();

        _timeProvider.Advance(TimeSpan.FromSeconds(1.999));
        Assert.NotNull(_announcementState.GetToast(activeHost));
        _timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Null(_announcementState.GetToast(activeHost));
    }

    [Fact]
    public void Toast_CoalescesFor250MillisecondsThenReplacesAndRestartsSixSecondLifetime()
    {
        object activeHost = new();
        _announcementState.SetHostAvailability(activeHost, true, false);
        _infoBannerService.InfoBanners.Returns([CreateInfo("First notice", "First detail")]);
        _infoBannerService.StateChanged += Raise.Event<Action>();
        _infoBannerService.StateChanged += Raise.Event<Action>();

        NotificationToast firstToast = Assert.IsType<NotificationToast>(_announcementState.GetToast(activeHost));
        Assert.Equal(1, firstToast.ArrivalCount);
        Assert.Equal(NotificationSeverity.Info, firstToast.Severity);

        _timeProvider.Advance(TimeSpan.FromMilliseconds(249));
        _errorBannerService.ErrorBanners.Returns([CreateError("Coalesced failure", "Failure detail")]);
        _errorBannerService.StateChanged += Raise.Event<Action>();

        NotificationToast coalescedToast = Assert.IsType<NotificationToast>(_announcementState.GetToast(activeHost));
        Assert.Equal(2, coalescedToast.ArrivalCount);
        Assert.Equal(NotificationSeverity.Error, coalescedToast.Severity);

        _timeProvider.Advance(TimeSpan.FromMilliseconds(2));
        _infoBannerService.InfoBanners.Returns([CreateInfo("Later notice", "Later detail")]);
        _infoBannerService.StateChanged += Raise.Event<Action>();

        NotificationToast replacedToast = Assert.IsType<NotificationToast>(_announcementState.GetToast(activeHost));
        Assert.Equal(3, replacedToast.ArrivalCount);
        Assert.Equal(NotificationSeverity.Info, replacedToast.Severity);
        Assert.Equal("Later notice", replacedToast.Title);

        _timeProvider.Advance(TimeSpan.FromSeconds(5.999));
        Assert.NotNull(_announcementState.GetToast(activeHost));
        _timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Null(_announcementState.GetToast(activeHost));
        Assert.Single(_infoBannerService.InfoBanners);
    }

    [Fact]
    public void Toast_IsClearedByPanelAndModalTakeoverWithoutReplay()
    {
        object activeHost = new();
        _announcementState.SetHostAvailability(activeHost, true, false);
        _infoBannerService.InfoBanners.Returns([CreateInfo("Initial notice", "Initial detail")]);
        _infoBannerService.StateChanged += Raise.Event<Action>();
        Assert.NotNull(_announcementState.GetToast(activeHost));

        _announcementState.SetPanelOpen(activeHost, true);
        Assert.Null(_announcementState.GetToast(activeHost));
        _infoBannerService.InfoBanners.Returns([CreateInfo("Panel arrival", "Panel detail")]);
        _infoBannerService.StateChanged += Raise.Event<Action>();
        _announcementState.SetPanelOpen(activeHost, false);
        Assert.Null(_announcementState.GetToast(activeHost));

        _modalCoordinator.ActiveSession.Returns(new ModalSession(new ModalId(8), typeof(PromptModal), null));
        _modalCoordinator.StateChanged += Raise.Event<Action>();
        _infoBannerService.InfoBanners.Returns([CreateInfo("Prompt arrival", "Prompt detail")]);
        _infoBannerService.StateChanged += Raise.Event<Action>();
        _modalCoordinator.ActiveSession.Returns((ModalSession?)null);
        _modalCoordinator.StateChanged += Raise.Event<Action>();
        _criticalErrorService.CurrentCritical.Returns(new InvalidOperationException("Critical takeover"));
        _criticalErrorService.StateChanged += Raise.Event<Action>();
        _errorBannerService.ErrorBanners.Returns([CreateError("Critical-period failure", "Failure detail")]);
        _errorBannerService.StateChanged += Raise.Event<Action>();
        _criticalErrorService.CurrentCritical.Returns((Exception?)null);
        _criticalErrorService.StateChanged += Raise.Event<Action>();

        Assert.Null(_announcementState.GetToast(activeHost));
    }

    private static ErrorBannerEntry CreateError(string title, string detail) =>
        new(BannerId.Create(), new Preformatted(title, detail), null, new DateTime(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc));

    private static BannerInfoEntry CreateInfo(string title, string message) =>
        new(BannerId.Create(), new Preformatted(title, message), BannerSeverity.Info, new DateTime(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc));

    private void SetProgress(bool export, bool running)
    {
        if (export)
        {
            _exportProgressBannerService.CurrentExport.Returns(running ? new ExportProgressEntry(() => { }) : null);
            _exportProgressBannerService.StateChanged += Raise.Event<Action>();
        }
        else
        {
            _progressBannerService.BackgroundProgress.Returns(running ? new BannerProgressEntry(
                new UpgradeBatchId(new Guid("9E8B4EB0-2590-4B6A-87CF-136A03CF4BF7")),
                UpgradeProgressScope.Background, 0, 2, string.Empty, UpgradePhase.BackingUp, 0, () => { }) : null);
            _progressBannerService.StateChanged += Raise.Event<Action>();
        }
    }

    private sealed class FakeTimeProvider(DateTimeOffset initialTime) : TimeProvider
    {
        private readonly List<FakeTimer> _timers = [];
        private DateTimeOffset _currentTime = initialTime;

        public void Advance(TimeSpan duration)
        {
            _currentTime += duration;
            while (_timers.FirstOrDefault(timer => timer.IsDue(_currentTime)) is { } dueTimer)
            {
                dueTimer.Fire();
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            FakeTimer timer = new(this, callback, state, period);
            _timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }

        public override DateTimeOffset GetUtcNow() => _currentTime;

        private sealed class FakeTimer(
            FakeTimeProvider timeProvider,
            TimerCallback callback,
            object? state,
            TimeSpan period) : ITimer
        {
            private bool _disposed;
            private DateTimeOffset? _dueTime;

            public bool Change(TimeSpan dueTime, TimeSpan nextPeriod)
            {
                if (_disposed) { return false; }

                _dueTime = dueTime == Timeout.InfiniteTimeSpan ? null : timeProvider.GetUtcNow() + dueTime;
                return true;
            }

            public void Dispose()
            {
                _disposed = true;
                _dueTime = null;
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public void Fire()
            {
                if (_disposed || _dueTime is null) { return; }

                _dueTime = period == Timeout.InfiniteTimeSpan ? null : timeProvider.GetUtcNow() + period;
                callback(state);
            }

            public bool IsDue(DateTimeOffset now) => !_disposed && _dueTime is { } dueTime && dueTime <= now;
        }
    }
}
