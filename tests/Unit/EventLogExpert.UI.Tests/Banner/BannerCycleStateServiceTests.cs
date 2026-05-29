// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.Database.Upgrade;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.UI.Banner;
using EventLogExpert.UI.DatabaseTools;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Banner;

public sealed class BannerCycleStateServiceTests
{
    private readonly IAttentionBannerService _attention = Substitute.For<IAttentionBannerService>();
    private readonly ICriticalErrorService _critical = Substitute.For<ICriticalErrorService>();
    private readonly IErrorBannerService _errors = Substitute.For<IErrorBannerService>();
    private readonly IInfoBannerService _infos = Substitute.For<IInfoBannerService>();
    private readonly IModalCoordinator _modalCoordinator = Substitute.For<IModalCoordinator>();
    private readonly IProgressBannerService _progress = Substitute.For<IProgressBannerService>();

    public BannerCycleStateServiceTests()
    {
        _critical.CurrentCritical.Returns((Exception?)null);
        _errors.ErrorBanners.Returns([]);
        _infos.InfoBanners.Returns([]);
        _attention.AttentionEntries.Returns([]);
        _attention.AttentionDismissed.Returns(false);
        _progress.BackgroundProgress.Returns((BannerProgressEntry?)null);
        _modalCoordinator.ActiveSession.Returns((ModalSession?)null);
    }

    [Fact]
    public void Constructor_ComputesInitialSnapshotFromCurrentFacetState()
    {
        var err = MakeError();
        _errors.ErrorBanners.Returns([err]);

        var service = CreateService();

        Assert.Single(service.Items);
        Assert.Equal(BannerView.Error, service.CurrentView);
        Assert.Equal(err.Id, service.SelectedItem?.EntryId);
    }

    [Fact]
    public void Constructor_WithNoBanners_HasEmptyItems_AndNoneView()
    {
        var service = CreateService();

        Assert.Empty(service.Items);
        Assert.Equal(BannerView.None, service.CurrentView);
        Assert.Null(service.SelectedItem);
    }

    [Fact]
    public void Dispose_UnsubscribesFromAllSources()
    {
        var service = CreateService();
        int stateChangedFires = 0;
        service.StateChanged += () => stateChangedFires++;

        service.Dispose();

        _errors.ErrorBanners.Returns([MakeError()]);
        _errors.StateChanged += Raise.Event<Action>();
        _attention.StateChanged += Raise.Event<Action>();
        _infos.StateChanged += Raise.Event<Action>();
        _progress.StateChanged += Raise.Event<Action>();
        _critical.StateChanged += Raise.Event<Action>();
        _modalCoordinator.StateChanged += Raise.Event<Action>();

        Assert.Equal(0, stateChangedFires);
    }

    [Fact]
    public void Modal_Close_RestoresAttention_EvenWhenHigherPriorityErrorCoexists()
    {
        _errors.ErrorBanners.Returns([MakeError()]);
        _attention.AttentionEntries.Returns([MakeAttention("db1.db")]);
        var service = CreateService();
        Assert.Equal(BannerView.Error, service.SelectedItem?.View);

        service.MoveNext();
        Assert.Equal(BannerView.Attention, service.SelectedItem?.View);

        _modalCoordinator.ActiveSession.Returns(
            new ModalSession(new ModalId(1), typeof(DatabaseToolsModal), null));
        _modalCoordinator.StateChanged += Raise.Event<Action>();
        Assert.Equal(BannerView.Error, service.SelectedItem?.View);

        _modalCoordinator.ActiveSession.Returns((ModalSession?)null);
        _modalCoordinator.StateChanged += Raise.Event<Action>();
        Assert.Equal(BannerView.Attention, service.SelectedItem?.View);
    }

    [Fact]
    public void Modal_Open_RemovesAttention_AndModal_Close_RestoresAttentionSelection()
    {
        _errors.ErrorBanners.Returns([]);
        _attention.AttentionEntries.Returns([MakeAttention("db1.db")]);

        var service = CreateService();
        // Construction yields a single-item cycle; MoveNext is a no-op there. Add an Error to give
        // a 2-item cycle so we can navigate and set _userPreferredItem.
        _errors.ErrorBanners.Returns([MakeError()]);
        _errors.StateChanged += Raise.Event<Action>();
        Assert.Equal(2, service.Items.Count);

        service.MoveNext();
        Assert.Equal(BannerView.Attention, service.SelectedItem?.View);

        _modalCoordinator.ActiveSession.Returns(
            new ModalSession(new ModalId(1), typeof(DatabaseToolsModal), null));
        _modalCoordinator.StateChanged += Raise.Event<Action>();
        Assert.Equal(BannerView.Error, service.SelectedItem?.View);

        _modalCoordinator.ActiveSession.Returns((ModalSession?)null);
        _modalCoordinator.StateChanged += Raise.Event<Action>();
        Assert.Equal(BannerView.Attention, service.SelectedItem?.View);
    }

    [Fact]
    public void ModalContentDisplayed_DefaultsFalse_OnNewInstance()
    {
        var service = CreateService();

        Assert.False(service.ModalContentDisplayed);
    }

    [Fact]
    public void ModalSuppression_PreservesUserPreferredAttention_DoesNotTriggerPriorityOverrideOnReentry()
    {
        _attention.AttentionEntries.Returns([MakeAttention("db1.db")]);
        _errors.ErrorBanners.Returns([MakeError()]);
        var service = CreateService();
        service.MoveNext();
        Assert.Equal(BannerView.Attention, service.SelectedItem?.View);

        _modalCoordinator.ActiveSession.Returns(
            new ModalSession(new ModalId(1), typeof(DatabaseToolsModal), null));
        _modalCoordinator.StateChanged += Raise.Event<Action>();

        _modalCoordinator.ActiveSession.Returns((ModalSession?)null);
        _modalCoordinator.StateChanged += Raise.Event<Action>();
        Assert.Equal(BannerView.Attention, service.SelectedItem?.View);

        // Trigger an unrelated rebuild — Attention must STAY selected (no stale priority steal).
        _infos.InfoBanners.Returns([MakeInfo()]);
        _infos.StateChanged += Raise.Event<Action>();
        Assert.Equal(BannerView.Attention, service.SelectedItem?.View);
    }

    [Fact]
    public void MoveNext_AtFirstIndex_AdvancesAndFiresStateChanged()
    {
        _errors.ErrorBanners.Returns([MakeError(), MakeError()]);
        var service = CreateService();

        int stateChangedFires = 0;
        service.StateChanged += () => stateChangedFires++;

        service.MoveNext();

        Assert.Equal(1, service.DisplayedIndex);
        Assert.Equal(1, stateChangedFires);
    }

    [Fact]
    public void MoveNext_AtLastIndex_DoesNothing()
    {
        _errors.ErrorBanners.Returns([MakeError(), MakeError()]);
        var service = CreateService();
        service.MoveNext();

        int stateChangedFires = 0;
        service.StateChanged += () => stateChangedFires++;

        service.MoveNext();

        Assert.Equal(1, service.DisplayedIndex);
        Assert.Equal(0, stateChangedFires);
    }

    [Fact]
    public void MovePrev_AtFirstIndex_DoesNothing()
    {
        _errors.ErrorBanners.Returns([MakeError(), MakeError()]);
        var service = CreateService();

        int stateChangedFires = 0;
        service.StateChanged += () => stateChangedFires++;

        service.MovePrev();

        Assert.Equal(0, service.DisplayedIndex);
        Assert.Equal(0, stateChangedFires);
    }

    [Fact]
    public void MovePrev_AtMiddleIndex_DecrementsAndFiresStateChanged()
    {
        _errors.ErrorBanners.Returns([MakeError(), MakeError(), MakeError()]);
        var service = CreateService();
        service.MoveNext();
        service.MoveNext();

        int stateChangedFires = 0;
        service.StateChanged += () => stateChangedFires++;

        service.MovePrev();

        Assert.Equal(1, service.DisplayedIndex);
        Assert.Equal(1, stateChangedFires);
    }

    [Fact]
    public void MultipleNewItems_HighestPriorityWins()
    {
        var info = MakeInfo();
        _infos.InfoBanners.Returns([info]);
        var service = CreateService();

        _critical.CurrentCritical.Returns(new InvalidOperationException("crit"));
        _errors.ErrorBanners.Returns([MakeError()]);
        _critical.StateChanged += Raise.Event<Action>();

        // Critical present → BannerViewSelector returns ONLY Critical (exclusive).
        Assert.Equal(BannerView.Critical, service.SelectedItem?.View);
    }

    [Fact]
    public void NewlyArrivedBackgroundProgress_DoesNotStealFromAttention()
    {
        _attention.AttentionEntries.Returns([MakeAttention("db1.db")]);
        var service = CreateService();
        Assert.Equal(BannerView.Attention, service.SelectedItem?.View);

        _progress.BackgroundProgress.Returns(MakeProgress());
        _progress.StateChanged += Raise.Event<Action>();

        Assert.Equal(BannerView.Attention, service.SelectedItem?.View);
    }

    [Fact]
    public void NewlyArrivedCriticalError_StealsSelectionFromInfo()
    {
        var info = MakeInfo();
        _infos.InfoBanners.Returns([info]);
        var service = CreateService();
        Assert.Equal(BannerView.Info, service.SelectedItem?.View);

        var critEx = new InvalidOperationException("boom");
        _critical.CurrentCritical.Returns(critEx);
        _critical.StateChanged += Raise.Event<Action>();

        Assert.Equal(BannerView.Critical, service.SelectedItem?.View);
    }

    [Fact]
    public void NewlyArrivedInfo_DoesNotStealFromAttention()
    {
        _attention.AttentionEntries.Returns([MakeAttention("db1.db")]);
        var service = CreateService();

        _infos.InfoBanners.Returns([MakeInfo()]);
        _infos.StateChanged += Raise.Event<Action>();

        Assert.Equal(BannerView.Attention, service.SelectedItem?.View);
    }

    [Fact]
    public void OnFacetChanged_AfterDatabaseToolsModalActive_AttentionSuppressionApplied()
    {
        _attention.AttentionEntries.Returns([MakeAttention("a.db")]);
        var service = CreateService();
        Assert.Equal(BannerView.Attention, service.CurrentView);

        _modalCoordinator.ActiveSession.Returns(
            new ModalSession(new ModalId(1), typeof(DatabaseToolsModal), null));
        _modalCoordinator.StateChanged += Raise.Event<Action>();

        Assert.Empty(service.Items);
        Assert.Equal(BannerView.None, service.CurrentView);
    }

    [Fact]
    public void OnFacetChanged_AfterErrorAdded_RebuildsAndFiresStateChanged()
    {
        var service = CreateService();
        int stateChangedFires = 0;
        service.StateChanged += () => stateChangedFires++;

        _errors.ErrorBanners.Returns([MakeError()]);
        _errors.StateChanged += Raise.Event<Action>();

        Assert.Single(service.Items);
        Assert.Equal(BannerView.Error, service.CurrentView);
        Assert.Equal(1, stateChangedFires);
    }

    [Fact]
    public void PriorityOverride_DoesNotFireOnProgressTickOfExistingBatch()
    {
        _attention.AttentionEntries.Returns([MakeAttention("db1.db")]);
        _progress.BackgroundProgress.Returns(MakeProgress());
        var service = CreateService();
        Assert.Equal(BannerView.Attention, service.SelectedItem?.View);

        // Progress tick on the same background batch — fingerprint key (UpgradeProgress, null)
        // unchanged, so NOT newly-arrived → selection stays.
        _progress.BackgroundProgress.Returns(MakeProgress());
        _progress.StateChanged += Raise.Event<Action>();

        Assert.Equal(BannerView.Attention, service.SelectedItem?.View);
    }

    [Fact]
    public void PriorityOverride_HoldsAgainstStaleUserPreferredItem()
    {
        var info = MakeInfo();
        _infos.InfoBanners.Returns([info]);
        var service = CreateService();
        var info2 = MakeInfo();
        _infos.InfoBanners.Returns([info, info2]);
        _infos.StateChanged += Raise.Event<Action>();
        service.MoveNext();
        Assert.Equal(info2.Id, service.SelectedItem?.EntryId);

        _critical.CurrentCritical.Returns(new InvalidOperationException("crit"));
        _critical.StateChanged += Raise.Event<Action>();
        Assert.Equal(BannerView.Critical, service.SelectedItem?.View);

        // Unrelated rebuild — must NOT bounce to info2.
        _critical.StateChanged += Raise.Event<Action>();
        Assert.Equal(BannerView.Critical, service.SelectedItem?.View);
    }

    [Fact]
    public void RebuildAndReselect_AfterSelectedItemRemoved_ClampsIndex()
    {
        var e1 = MakeError();
        var e2 = MakeError();
        _errors.ErrorBanners.Returns([e1, e2]);
        var service = CreateService();
        service.MoveNext();
        Assert.Equal(e2.Id, service.SelectedItem?.EntryId);

        _errors.ErrorBanners.Returns([e1]);
        _errors.StateChanged += Raise.Event<Action>();

        Assert.Equal(0, service.DisplayedIndex);
        Assert.Equal(e1.Id, service.SelectedItem?.EntryId);
    }

    [Fact]
    public void RebuildAndReselect_PreservesSelectedItemWhenStillPresent()
    {
        var e1 = MakeError();
        var e2 = MakeError();
        _errors.ErrorBanners.Returns([e1, e2]);
        var service = CreateService();
        service.MoveNext();
        Assert.Equal(e2.Id, service.SelectedItem?.EntryId);

        _errors.ErrorBanners.Returns([e1, e2, MakeError()]);
        _errors.StateChanged += Raise.Event<Action>();

        Assert.Equal(e2.Id, service.SelectedItem?.EntryId);
    }

    [Fact]
    public void RebuildAndReselect_WhenActiveSessionIsNotNull_PreservesModalContentDisplayedFlag()
    {
        var service = CreateService();
        service.SetModalContentDisplayed(true);

        _modalCoordinator.ActiveSession.Returns(
            new ModalSession(new ModalId(7), typeof(BannerCycleStateServiceTests), null));
        _errors.ErrorBanners.Returns([MakeError()]);
        _errors.StateChanged += Raise.Event<Action>();

        Assert.True(service.ModalContentDisplayed);
    }

    [Fact]
    public void RebuildAndReselect_WhenActiveSessionIsNull_AutoClearsModalContentDisplayed()
    {
        var service = CreateService();
        service.SetModalContentDisplayed(true);
        Assert.True(service.ModalContentDisplayed);

        _modalCoordinator.ActiveSession.Returns((ModalSession?)null);
        _errors.ErrorBanners.Returns([MakeError()]);
        _errors.StateChanged += Raise.Event<Action>();

        Assert.False(service.ModalContentDisplayed);
    }

    [Fact]
    public void RegisterFallbackError_AppliesOverrideImmediately_FiresStateChangedOnce()
    {
        var existing = MakeError();
        var fallback = MakeError();
        _errors.ErrorBanners.Returns([existing, fallback]);
        var service = CreateService();

        int stateChangedFires = 0;
        service.StateChanged += () => stateChangedFires++;

        service.RegisterFallbackError(new BannerCycleItem(BannerView.Error, 1, fallback.Id));

        Assert.Equal(1, service.DisplayedIndex);
        Assert.Equal(fallback.Id, service.SelectedItem?.EntryId);
        Assert.Equal(1, stateChangedFires);
    }

    [Fact]
    public void RegisterFallbackError_OverrideConsumedAfterApplication()
    {
        var first = MakeError();
        var second = MakeError();
        _errors.ErrorBanners.Returns([first, second]);
        var service = CreateService();

        service.RegisterFallbackError(new BannerCycleItem(BannerView.Error, 1, second.Id));
        Assert.Equal(1, service.DisplayedIndex);

        var third = MakeError();
        _errors.ErrorBanners.Returns([first, second, third]);
        _errors.StateChanged += Raise.Event<Action>();

        Assert.Equal(1, service.DisplayedIndex);
        Assert.Equal(second.Id, service.SelectedItem?.EntryId);
    }

    [Fact]
    public void RegisterFallbackError_WhenOverrideItemNotInCurrentItems_FallsThroughToPriorSelection()
    {
        var existing = MakeError();
        _errors.ErrorBanners.Returns([existing]);
        var service = CreateService();

        var notInList = new BannerCycleItem(BannerView.Error, 99, BannerId.Create());
        service.RegisterFallbackError(notInList);

        Assert.Equal(0, service.DisplayedIndex);
        Assert.Equal(existing.Id, service.SelectedItem?.EntryId);
    }

    [Fact]
    public void SetModalContentDisplayed_SameValue_IsNoOp_DoesNotFireStateChanged()
    {
        var service = CreateService();
        int stateChangedFires = 0;
        service.StateChanged += () => stateChangedFires++;

        service.SetModalContentDisplayed(false);

        Assert.False(service.ModalContentDisplayed);
        Assert.Equal(0, stateChangedFires);
    }

    [Fact]
    public void SetModalContentDisplayed_True_TogglesFlag_FiresStateChangedOnce()
    {
        var service = CreateService();
        int stateChangedFires = 0;
        service.StateChanged += () => stateChangedFires++;

        service.SetModalContentDisplayed(true);

        Assert.True(service.ModalContentDisplayed);
        Assert.Equal(1, stateChangedFires);
    }

    [Fact]
    public void UserExplicitMoveAfterPriorityOverride_ClearsPriorityStolenMarker_AllowsRestoreAfterSuppression()
    {
        // Uses Error (non-exclusive — stays in the cycle) as the stealer so the user-ack clear is
        // actually exercised; Critical is exclusive and self-clears from the fingerprint, making
        // the user-ack code path a no-op for that view.
        _attention.AttentionEntries.Returns([MakeAttention("db1.db")]);
        _infos.InfoBanners.Returns([MakeInfo()]);
        var service = CreateService();
        Assert.Equal(BannerView.Attention, service.SelectedItem?.View);

        var errorEntry = MakeError();
        _errors.ErrorBanners.Returns([errorEntry]);
        _errors.StateChanged += Raise.Event<Action>();
        Assert.Equal(BannerView.Error, service.SelectedItem?.View);

        service.MoveNext();
        Assert.Equal(BannerView.Attention, service.SelectedItem?.View);

        _modalCoordinator.ActiveSession.Returns(
            new ModalSession(new ModalId(1), typeof(DatabaseToolsModal), null));
        _modalCoordinator.StateChanged += Raise.Event<Action>();
        Assert.NotEqual(BannerView.Attention, service.SelectedItem?.View);

        _modalCoordinator.ActiveSession.Returns((ModalSession?)null);
        _modalCoordinator.StateChanged += Raise.Event<Action>();
        Assert.Equal(BannerView.Attention, service.SelectedItem?.View);
    }

    [Fact]
    public void UserPreferredItem_NotUpdatedByRebuild_OnlyByExplicitMove()
    {
        var info1 = MakeInfo();
        var info2 = MakeInfo();
        _infos.InfoBanners.Returns([info1, info2]);
        var service = CreateService();
        Assert.Equal(info1.Id, service.SelectedItem?.EntryId);

        _infos.StateChanged += Raise.Event<Action>();

        // Remove info1: if the passive tick had implicitly recorded a preference for info1, the
        // subsequent rebuild would still land on info2 via clamp — confirming no preference was set.
        _infos.InfoBanners.Returns([info2]);
        _infos.StateChanged += Raise.Event<Action>();
        Assert.Equal(info2.Id, service.SelectedItem?.EntryId);
    }

    [Fact]
    public void UserPreferredItem_RemovedFromSource_ClearedFromPreferred()
    {
        var info1 = MakeInfo();
        var info2 = MakeInfo();
        _infos.InfoBanners.Returns([info1, info2]);
        var service = CreateService();
        service.MoveNext();
        Assert.Equal(info2.Id, service.SelectedItem?.EntryId);

        _infos.InfoBanners.Returns([info1]);
        _infos.StateChanged += Raise.Event<Action>();
        Assert.Equal(info1.Id, service.SelectedItem?.EntryId);

        // Re-add info2 — should NOT snap back via stale preference.
        _infos.InfoBanners.Returns([info1, info2]);
        _infos.StateChanged += Raise.Event<Action>();
        Assert.Equal(info1.Id, service.SelectedItem?.EntryId);
    }

    private static DatabaseEntry MakeAttention(string fileName) =>
        new(fileName, $@"C:\dbs\{fileName}", IsEnabled: false, DatabaseStatus.UpgradeRequired);

    private static ErrorBannerEntry MakeError() =>
        new(BannerId.Create(), "Title", "msg", null, null, DateTime.UtcNow);

    private static BannerInfoEntry MakeInfo() =>
        new(BannerId.Create(), "Title", "msg", BannerSeverity.Info, DateTime.UtcNow);

    private static BannerProgressEntry MakeProgress() =>
        new(
            new UpgradeBatchId(Guid.NewGuid()),
            UpgradeProgressScope.Background,
            0,
            1,
            string.Empty,
            UpgradePhase.BackingUp,
            0,
            () => { });

    private BannerCycleStateService CreateService() =>
        new(_attention, _errors, _infos, _progress, _critical, _modalCoordinator);
}
