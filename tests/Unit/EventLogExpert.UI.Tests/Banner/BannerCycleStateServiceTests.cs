// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Database;
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
    public void ModalContentDisplayed_DefaultsFalse_OnNewInstance()
    {
        var service = CreateService();

        Assert.False(service.ModalContentDisplayed);
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

    private static DatabaseEntry MakeAttention(string fileName) =>
        new(fileName, $@"C:\dbs\{fileName}", IsEnabled: false, DatabaseStatus.UpgradeRequired);

    private static ErrorBannerEntry MakeError() =>
        new(BannerId.Create(), "Title", "msg", null, null, DateTime.UtcNow);

    private BannerCycleStateService CreateService() =>
        new(_attention, _errors, _infos, _progress, _critical, _modalCoordinator);
}
