// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using EventLogExpert.UI.Services;

namespace EventLogExpert.UI.Tests.Services;

public sealed class BannerServiceTests
{
    [Fact]
    public void ClearError_RaisesStateChanged_AndNullsUnhandledError()
    {
        // Arrange
        var sut = new BannerService();
        sut.ReportError(new InvalidOperationException("boom"));
        int stateChangedCount = 0;
        sut.StateChanged += () => stateChangedCount++;

        // Act
        sut.ClearError();

        // Assert
        Assert.Null(sut.UnhandledError);
        Assert.Equal(1, stateChangedCount);
    }

    [Fact]
    public void DismissCritical_RemovesByGuid_RaisesStateChanged()
    {
        // Arrange
        var sut = new BannerService();
        sut.ReportCritical("First Title", "First Message");
        sut.ReportCritical("Second Title", "Second Message");
        Guid firstId = sut.CriticalAlerts[0].Id;
        int stateChangedCount = 0;
        sut.StateChanged += () => stateChangedCount++;

        // Act
        sut.DismissCritical(firstId);

        // Assert
        Assert.Single(sut.CriticalAlerts);
        Assert.Equal("Second Title", sut.CriticalAlerts[0].Title);
        Assert.Equal(1, stateChangedCount);
    }

    [Fact]
    public void DismissCritical_WithUnknownId_NoOp()
    {
        // Arrange
        var sut = new BannerService();
        sut.ReportCritical("Title", "Message");
        int stateChangedCount = 0;
        sut.StateChanged += () => stateChangedCount++;

        // Act
        sut.DismissCritical(Guid.NewGuid());

        // Assert
        Assert.Single(sut.CriticalAlerts);
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
    public void ReportCritical_AppendsToList_RaisesStateChanged()
    {
        // Arrange
        var sut = new BannerService();
        int stateChangedCount = 0;
        sut.StateChanged += () => stateChangedCount++;

        // Act
        sut.ReportCritical("Title", "Message");

        // Assert
        Assert.Single(sut.CriticalAlerts);
        Assert.Equal("Title", sut.CriticalAlerts[0].Title);
        Assert.Equal("Message", sut.CriticalAlerts[0].Message);
        Assert.NotEqual(Guid.Empty, sut.CriticalAlerts[0].Id);
        Assert.Equal(1, stateChangedCount);
    }

    [Fact]
    public void ReportCritical_TwiceQueuesBoth_FifoOrder()
    {
        // Arrange
        var sut = new BannerService();

        // Act
        sut.ReportCritical("First Title", "First Message");
        sut.ReportCritical("Second Title", "Second Message");

        // Assert
        Assert.Equal(2, sut.CriticalAlerts.Count);
        Assert.Equal("First Title", sut.CriticalAlerts[0].Title);
        Assert.Equal("Second Title", sut.CriticalAlerts[1].Title);
    }

    [Fact]
    public void ReportError_RaisesStateChanged_AndPopulatesUnhandledError()
    {
        // Arrange
        var sut = new BannerService();
        int stateChangedCount = 0;
        sut.StateChanged += () => stateChangedCount++;
        var error = new InvalidOperationException("boom");

        // Act
        sut.ReportError(error);

        // Assert
        Assert.Same(error, sut.UnhandledError);
        Assert.Equal(1, stateChangedCount);
    }

    [Fact]
    public void ReportError_TwiceReplacesPrior_RaisesStateChangedTwice()
    {
        // Arrange
        var sut = new BannerService();
        int stateChangedCount = 0;
        sut.StateChanged += () => stateChangedCount++;
        var first = new InvalidOperationException("first");
        var second = new InvalidOperationException("second");

        // Act
        sut.ReportError(first);
        sut.ReportError(second);

        // Assert
        Assert.Same(second, sut.UnhandledError);
        Assert.Equal(2, stateChangedCount);
    }

    [Fact]
    public void ReportError_WithNull_Throws()
    {
        // Arrange
        var sut = new BannerService();

        // Act + Assert
        Assert.Throws<ArgumentNullException>(() => sut.ReportError(null!));
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
    public async Task SetRecoveryCallback_OverwritesPriorCallback()
    {
        // Arrange
        var sut = new BannerService();
        int firstInvokeCount = 0;
        int secondInvokeCount = 0;
        sut.SetRecoveryCallback(() => { firstInvokeCount++; return Task.CompletedTask; });
        sut.SetRecoveryCallback(() => { secondInvokeCount++; return Task.CompletedTask; });

        // Act
        await sut.TryRecoverAsync();

        // Assert
        Assert.Equal(0, firstInvokeCount);
        Assert.Equal(1, secondInvokeCount);
    }

    [Fact]
    public async Task TryRecoverAsync_InvokesRegisteredCallback_ThenClearsError()
    {
        // Arrange
        var sut = new BannerService();
        sut.ReportError(new InvalidOperationException("boom"));
        bool callbackInvoked = false;
        sut.SetRecoveryCallback(() => { callbackInvoked = true; return Task.CompletedTask; });

        // Act
        await sut.TryRecoverAsync();

        // Assert
        Assert.True(callbackInvoked);
        Assert.Null(sut.UnhandledError);
    }

    [Fact]
    public async Task TryRecoverAsync_WhenCallbackThrows_DoesNotClearError()
    {
        // Arrange
        var sut = new BannerService();
        var error = new InvalidOperationException("boom");
        sut.ReportError(error);
        sut.SetRecoveryCallback(() => throw new InvalidOperationException("recover failed"));

        // Act + Assert — callback exception propagates; error remains so user can retry or see persistent state.
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.TryRecoverAsync());
        Assert.Same(error, sut.UnhandledError);
    }

    [Fact]
    public async Task TryRecoverAsync_WhenNewErrorReportedDuringCallback_DoesNotClearNewError()
    {
        // Arrange — recovery callback awaits a gate; a different thread reports a new error before the gate releases.
        var sut = new BannerService();
        var oldError = new InvalidOperationException("old");
        var newError = new InvalidOperationException("new");
        sut.ReportError(oldError);
        var callbackStarted = new TaskCompletionSource();
        var callbackCanFinish = new TaskCompletionSource();
        sut.SetRecoveryCallback(async () =>
        {
            callbackStarted.SetResult();
            await callbackCanFinish.Task;
        });

        // Act
        Task recoverTask = sut.TryRecoverAsync();
        await callbackStarted.Task;
        sut.ReportError(newError);
        callbackCanFinish.SetResult();
        await recoverTask;

        // Assert — the newer error survives the recovery completion.
        Assert.Same(newError, sut.UnhandledError);
    }

    [Fact]
    public async Task TryRecoverAsync_WhenNewErrorReportedDuringCallback_DoesNotRaiseExtraStateChanged()
    {
        // Arrange — verify we do not double-fire StateChanged when the snapshot mismatch path is taken.
        var sut = new BannerService();
        sut.ReportError(new InvalidOperationException("old"));
        var callbackStarted = new TaskCompletionSource();
        var callbackCanFinish = new TaskCompletionSource();
        sut.SetRecoveryCallback(async () =>
        {
            callbackStarted.SetResult();
            await callbackCanFinish.Task;
        });

        Task recoverTask = sut.TryRecoverAsync();
        await callbackStarted.Task;
        int stateChangedCount = 0;
        sut.StateChanged += () => stateChangedCount++;
        sut.ReportError(new InvalidOperationException("new"));
        callbackCanFinish.SetResult();
        await recoverTask;

        // Assert — only the in-flight ReportError raised StateChanged; the recovery did NOT add a clear-event.
        Assert.Equal(1, stateChangedCount);
    }

    [Fact]
    public async Task TryRecoverAsync_WithNoRegisteredCallback_StillClearsError_DoesNotThrow()
    {
        // Arrange
        var sut = new BannerService();
        sut.ReportError(new InvalidOperationException("boom"));

        // Act
        await sut.TryRecoverAsync();

        // Assert
        Assert.Null(sut.UnhandledError);
    }
}
