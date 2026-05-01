// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using EventLogExpert.UI.Services;

namespace EventLogExpert.UI.Tests.Services;

public sealed class BannerServiceTests
{
    [Fact]
    public void ClearCritical_RaisesStateChanged_AndNullsCurrentCritical()
    {
        // Arrange
        var sut = new BannerService();
        sut.ReportCritical(new InvalidOperationException("boom"));
        int stateChangedCount = 0;
        sut.StateChanged += () => stateChangedCount++;

        // Act
        sut.ClearCritical();

        // Assert
        Assert.Null(sut.CurrentCritical);
        Assert.Equal(1, stateChangedCount);
    }

    [Fact]
    public void DismissError_RemovesByGuid_RaisesStateChanged()
    {
        // Arrange
        var sut = new BannerService();
        sut.ReportError("First Title", "First Message");
        sut.ReportError("Second Title", "Second Message");
        Guid firstId = sut.ErrorBanners[0].Id;
        int stateChangedCount = 0;
        sut.StateChanged += () => stateChangedCount++;

        // Act
        sut.DismissError(firstId);

        // Assert
        Assert.Single(sut.ErrorBanners);
        Assert.Equal("Second Title", sut.ErrorBanners[0].Title);
        Assert.Equal(1, stateChangedCount);
    }

    [Fact]
    public void DismissError_WithUnknownId_NoOp()
    {
        // Arrange
        var sut = new BannerService();
        sut.ReportError("Title", "Message");
        int stateChangedCount = 0;
        sut.StateChanged += () => stateChangedCount++;

        // Act
        sut.DismissError(Guid.NewGuid());

        // Assert
        Assert.Single(sut.ErrorBanners);
        Assert.Equal(0, stateChangedCount);
    }

    [Fact]
    public void DismissInfoBanner_RemovesByGuid_RaisesStateChanged()
    {
        // Arrange
        var sut = new BannerService();
        sut.ReportInfoBanner("First Title", "First Message", BannerSeverity.Info);
        sut.ReportInfoBanner("Second Title", "Second Message", BannerSeverity.Warning);
        Guid firstId = sut.InfoBanners[0].Id;
        int stateChangedCount = 0;
        sut.StateChanged += () => stateChangedCount++;

        // Act
        sut.DismissInfoBanner(firstId);

        // Assert
        Assert.Single(sut.InfoBanners);
        Assert.Equal("Second Title", sut.InfoBanners[0].Title);
        Assert.Equal(1, stateChangedCount);
    }

    [Fact]
    public void DismissInfoBanner_WithUnknownId_NoOp()
    {
        // Arrange
        var sut = new BannerService();
        sut.ReportInfoBanner("Title", "Message", BannerSeverity.Info);
        int stateChangedCount = 0;
        sut.StateChanged += () => stateChangedCount++;

        // Act
        sut.DismissInfoBanner(Guid.NewGuid());

        // Assert
        Assert.Single(sut.InfoBanners);
        Assert.Equal(0, stateChangedCount);
    }

    [Fact]
    public async Task RegisterRecoveryCallback_DisposingActiveHandle_UnregistersCallback()
    {
        // Arrange
        var sut = new BannerService();
        sut.ReportCritical(new InvalidOperationException("boom"));
        bool callbackInvoked = false;
        IDisposable registration = sut.RegisterRecoveryCallback(() => { callbackInvoked = true; return Task.CompletedTask; });

        // Act
        registration.Dispose();
        await sut.TryRecoverAsync();

        // Assert — callback was unregistered before recovery, so it must not have run.
        Assert.False(callbackInvoked);
        Assert.Null(sut.CurrentCritical);
    }

    [Fact]
    public async Task RegisterRecoveryCallback_DisposingStaleHandle_DoesNotClearActiveCallback()
    {
        // Arrange — stale handle from a prior registration must not clear the newer registration.
        var sut = new BannerService();
        sut.ReportCritical(new InvalidOperationException("boom"));
        bool firstInvoked = false;
        bool secondInvoked = false;
        IDisposable firstRegistration = sut.RegisterRecoveryCallback(() => { firstInvoked = true; return Task.CompletedTask; });
        sut.RegisterRecoveryCallback(() => { secondInvoked = true; return Task.CompletedTask; });

        // Act
        firstRegistration.Dispose();
        await sut.TryRecoverAsync();

        // Assert
        Assert.False(firstInvoked);
        Assert.True(secondInvoked);
    }

    [Fact]
    public void RegisterRecoveryCallback_DisposingTwice_IsIdempotent()
    {
        // Arrange
        var sut = new BannerService();
        IDisposable registration = sut.RegisterRecoveryCallback(() => Task.CompletedTask);

        // Act + Assert — second dispose must not throw.
        registration.Dispose();
        registration.Dispose();
    }

    [Fact]
    public void RegisterRecoveryCallback_WhenCallbackIsNull_Throws()
    {
        // Arrange
        var sut = new BannerService();

        // Act + Assert
        Assert.Throws<ArgumentNullException>(() => sut.RegisterRecoveryCallback(null!));
    }

    [Fact]
    public async Task RegisterRecoveryCallback_WhenCalledTwice_OverwritesPriorCallback()
    {
        // Arrange
        var sut = new BannerService();
        int firstInvokeCount = 0;
        int secondInvokeCount = 0;
        sut.RegisterRecoveryCallback(() => { firstInvokeCount++; return Task.CompletedTask; });
        sut.RegisterRecoveryCallback(() => { secondInvokeCount++; return Task.CompletedTask; });

        // Act
        await sut.TryRecoverAsync();

        // Assert
        Assert.Equal(0, firstInvokeCount);
        Assert.Equal(1, secondInvokeCount);
    }

    [Fact]
    public void ReportCritical_RaisesStateChanged_AndPopulatesCurrentCritical()
    {
        // Arrange
        var sut = new BannerService();
        int stateChangedCount = 0;
        sut.StateChanged += () => stateChangedCount++;
        var critical = new InvalidOperationException("boom");

        // Act
        sut.ReportCritical(critical);

        // Assert
        Assert.Same(critical, sut.CurrentCritical);
        Assert.Equal(1, stateChangedCount);
    }

    [Fact]
    public void ReportCritical_TwiceReplacesPrior_RaisesStateChangedTwice()
    {
        // Arrange
        var sut = new BannerService();
        int stateChangedCount = 0;
        sut.StateChanged += () => stateChangedCount++;
        var first = new InvalidOperationException("first");
        var second = new InvalidOperationException("second");

        // Act
        sut.ReportCritical(first);
        sut.ReportCritical(second);

        // Assert
        Assert.Same(second, sut.CurrentCritical);
        Assert.Equal(2, stateChangedCount);
    }

    [Fact]
    public void ReportCritical_WithNull_Throws()
    {
        // Arrange
        var sut = new BannerService();

        // Act + Assert
        Assert.Throws<ArgumentNullException>(() => sut.ReportCritical(null!));
    }

    [Fact]
    public void ReportError_AppendsToList_RaisesStateChanged()
    {
        // Arrange
        var sut = new BannerService();
        int stateChangedCount = 0;
        sut.StateChanged += () => stateChangedCount++;

        // Act
        sut.ReportError("Title", "Message");

        // Assert
        Assert.Single(sut.ErrorBanners);
        Assert.Equal("Title", sut.ErrorBanners[0].Title);
        Assert.Equal("Message", sut.ErrorBanners[0].Message);
        Assert.Null(sut.ErrorBanners[0].ActionLabel);
        Assert.Null(sut.ErrorBanners[0].Action);
        Assert.NotEqual(Guid.Empty, sut.ErrorBanners[0].Id);
        Assert.Equal(1, stateChangedCount);
    }

    [Fact]
    public void ReportError_ReturnsNewEntryId_MatchesAddedBanner()
    {
        // Arrange
        var sut = new BannerService();

        // Act
        Guid id = sut.ReportError("Title", "Message");

        // Assert
        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal(id, sut.ErrorBanners[0].Id);
    }

    [Fact]
    public void ReportError_TwiceQueuesBoth_FifoOrder()
    {
        // Arrange
        var sut = new BannerService();

        // Act
        sut.ReportError("First Title", "First Message");
        sut.ReportError("Second Title", "Second Message");

        // Assert
        Assert.Equal(2, sut.ErrorBanners.Count);
        Assert.Equal("First Title", sut.ErrorBanners[0].Title);
        Assert.Equal("Second Title", sut.ErrorBanners[1].Title);
    }

    [Fact]
    public void ReportError_WithActionButNoLabel_Throws()
    {
        // Arrange
        var sut = new BannerService();
        string? actionLabel = null;
        Func<Task> action = () => Task.CompletedTask;

        // Act + Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            sut.ReportError("Title", "Message", actionLabel: actionLabel, action: action));
        Assert.Equal(nameof(actionLabel), ex.ParamName);
    }

    [Fact]
    public void ReportError_WithActionLabelAndAction_StoresBothOnEntry()
    {
        // Arrange
        var sut = new BannerService();
        Func<Task> action = () => Task.CompletedTask;

        // Act
        sut.ReportError("Title", "Message", "Resolve", action);

        // Assert
        Assert.Equal("Resolve", sut.ErrorBanners[0].ActionLabel);
        Assert.Same(action, sut.ErrorBanners[0].Action);
    }

    [Fact]
    public void ReportError_WithActionLabelButNoAction_Throws()
    {
        // Arrange
        var sut = new BannerService();
        Func<Task>? action = null;

        // Act + Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            sut.ReportError("Title", "Message", "Resolve", action: action));
        Assert.Equal(nameof(action), ex.ParamName);
    }

    [Fact]
    public void ReportError_WithWhitespaceLabelAndAction_Throws()
    {
        // Arrange — whitespace label is not a renderable button label, so it must be rejected when an action is supplied.
        var sut = new BannerService();
        string actionLabel = "   ";

        // Act + Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            sut.ReportError("Title", "Message", actionLabel: actionLabel, () => Task.CompletedTask));
        Assert.Equal(nameof(actionLabel), ex.ParamName);
    }

    [Fact]
    public void ReportError_WithWhitespaceLabelAndNoAction_AcceptsAndStoresNull()
    {
        // Arrange — both effectively absent: the whitespace label is normalized to null and no action is supplied.
        var sut = new BannerService();

        // Act
        sut.ReportError("Title", "Message", "   ", action: null);

        // Assert
        Assert.Null(sut.ErrorBanners[0].ActionLabel);
        Assert.Null(sut.ErrorBanners[0].Action);
    }

    [Fact]
    public void ReportInfoBanner_AppendsToList_RaisesStateChanged()
    {
        // Arrange
        var sut = new BannerService();
        int stateChangedCount = 0;
        sut.StateChanged += () => stateChangedCount++;

        // Act
        sut.ReportInfoBanner("Title", "Message", BannerSeverity.Warning);

        // Assert
        Assert.Single(sut.InfoBanners);
        Assert.Equal("Title", sut.InfoBanners[0].Title);
        Assert.Equal("Message", sut.InfoBanners[0].Message);
        Assert.Equal(BannerSeverity.Warning, sut.InfoBanners[0].Severity);
        Assert.NotEqual(Guid.Empty, sut.InfoBanners[0].Id);
        Assert.Equal(1, stateChangedCount);
    }

    [Fact]
    public void ReportInfoBanner_TwiceQueuesBoth_FifoOrder()
    {
        // Arrange
        var sut = new BannerService();

        // Act
        sut.ReportInfoBanner("First Title", "First Message", BannerSeverity.Info);
        sut.ReportInfoBanner("Second Title", "Second Message", BannerSeverity.Warning);

        // Assert
        Assert.Equal(2, sut.InfoBanners.Count);
        Assert.Equal("First Title", sut.InfoBanners[0].Title);
        Assert.Equal(BannerSeverity.Info, sut.InfoBanners[0].Severity);
        Assert.Equal("Second Title", sut.InfoBanners[1].Title);
        Assert.Equal(BannerSeverity.Warning, sut.InfoBanners[1].Severity);
    }

    [Fact]
    public async Task TryRecoverAsync_InvokesRegisteredCallback_ThenClearsCritical()
    {
        // Arrange
        var sut = new BannerService();
        sut.ReportCritical(new InvalidOperationException("boom"));
        bool callbackInvoked = false;
        sut.RegisterRecoveryCallback(() => { callbackInvoked = true; return Task.CompletedTask; });

        // Act
        await sut.TryRecoverAsync();

        // Assert
        Assert.True(callbackInvoked);
        Assert.Null(sut.CurrentCritical);
    }

    [Fact]
    public async Task TryRecoverAsync_WhenCallbackThrows_DoesNotClearCritical()
    {
        // Arrange
        var sut = new BannerService();
        var critical = new InvalidOperationException("boom");
        sut.ReportCritical(critical);
        sut.RegisterRecoveryCallback(() => throw new InvalidOperationException("recover failed"));

        // Act + Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.TryRecoverAsync());
        Assert.Same(critical, sut.CurrentCritical);
    }

    [Fact]
    public async Task TryRecoverAsync_WhenNewCriticalReportedDuringCallback_DoesNotClearNewCritical()
    {
        // Arrange — recovery callback awaits a gate; another thread reports a new critical before the gate releases.
        var sut = new BannerService();
        var oldCritical = new InvalidOperationException("old");
        var newCritical = new InvalidOperationException("new");
        sut.ReportCritical(oldCritical);
        var callbackStarted = new TaskCompletionSource();
        var callbackCanFinish = new TaskCompletionSource();
        sut.RegisterRecoveryCallback(async () =>
        {
            callbackStarted.SetResult();
            await callbackCanFinish.Task;
        });

        // Act
        Task recoverTask = sut.TryRecoverAsync();
        await callbackStarted.Task;
        sut.ReportCritical(newCritical);
        callbackCanFinish.SetResult();
        await recoverTask;

        // Assert
        Assert.Same(newCritical, sut.CurrentCritical);
    }

    [Fact]
    public async Task TryRecoverAsync_WhenNewCriticalReportedDuringCallback_DoesNotRaiseExtraStateChanged()
    {
        // Arrange — verify the snapshot-mismatch path does not double-fire StateChanged.
        var sut = new BannerService();
        sut.ReportCritical(new InvalidOperationException("old"));
        var callbackStarted = new TaskCompletionSource();
        var callbackCanFinish = new TaskCompletionSource();
        sut.RegisterRecoveryCallback(async () =>
        {
            callbackStarted.SetResult();
            await callbackCanFinish.Task;
        });

        Task recoverTask = sut.TryRecoverAsync();
        await callbackStarted.Task;
        int stateChangedCount = 0;
        sut.StateChanged += () => stateChangedCount++;
        sut.ReportCritical(new InvalidOperationException("new"));
        callbackCanFinish.SetResult();
        await recoverTask;

        // Assert — only the in-flight ReportCritical raised StateChanged; recovery did NOT add a clear-event.
        Assert.Equal(1, stateChangedCount);
    }

    [Fact]
    public async Task TryRecoverAsync_WithNoRegisteredCallback_StillClearsCritical_DoesNotThrow()
    {
        // Arrange
        var sut = new BannerService();
        sut.ReportCritical(new InvalidOperationException("boom"));

        // Act
        await sut.TryRecoverAsync();

        // Assert
        Assert.Null(sut.CurrentCritical);
    }
}
