// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Services;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Services;

public sealed class BannerServiceTests
{
    [Fact]
    public void BannerProgressEntry_Cancel_InvokesUnderlyingEventArgsCancel()
    {
        // Arrange — cancel button on the banner card must propagate to the in-flight batch's CTS via
        // the args.Cancel() shim; verify the captured delegate calls through.
        var databaseService = Substitute.For<IDatabaseService>();
        var sut = new BannerService(databaseService, Substitute.For<ITraceLogger>());
        var batchId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        databaseService.UpgradeBatchStarted += Raise.EventWith(
            databaseService,
            new UpgradeBatchStartedEventArgs(batchId, UpgradeProgressScope.Background, 1, cts));

        // Act
        sut.BackgroundProgress!.Cancel();

        // Assert
        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public void ClearCritical_RaisesStateChanged_AndNullsCurrentCritical()
    {
        // Arrange
        var sut = new BannerService(Substitute.For<IDatabaseService>(), Substitute.For<ITraceLogger>());
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
        var sut = new BannerService(Substitute.For<IDatabaseService>(), Substitute.For<ITraceLogger>());
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
        var sut = new BannerService(Substitute.For<IDatabaseService>(), Substitute.For<ITraceLogger>());
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
        var sut = new BannerService(Substitute.For<IDatabaseService>(), Substitute.For<ITraceLogger>());
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
        var sut = new BannerService(Substitute.For<IDatabaseService>(), Substitute.For<ITraceLogger>());
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
    public void RaiseStateChanged_WhenSubscriberThrows_StillFiresLaterSubscribersAndLogs()
    {
        // Arrange — multicast safety: a throwing subscriber must not block siblings, and the throw
        // must be logged. Mirrors the c15 SafeRaise fix in DatabaseService for the same bug class.
        var databaseService = Substitute.For<IDatabaseService>();
        var traceLogger = Substitute.For<ITraceLogger>();
        var sut = new BannerService(databaseService, traceLogger);
        bool secondCalled = false;
        sut.StateChanged += () => throw new InvalidOperationException("boom");
        sut.StateChanged += () => secondCalled = true;

        // Act — any path that raises StateChanged works; ReportError is the simplest.
        sut.ReportError("title", "message");

        // Assert
        Assert.True(secondCalled);
        traceLogger.Received(1).Warn(Arg.Any<WarnLogHandler>());
    }

    [Fact]
    public async Task RegisterRecoveryCallback_DisposingActiveHandle_UnregistersCallback()
    {
        // Arrange
        var sut = new BannerService(Substitute.For<IDatabaseService>(), Substitute.For<ITraceLogger>());
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
        var sut = new BannerService(Substitute.For<IDatabaseService>(), Substitute.For<ITraceLogger>());
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
        var sut = new BannerService(Substitute.For<IDatabaseService>(), Substitute.For<ITraceLogger>());
        IDisposable registration = sut.RegisterRecoveryCallback(() => Task.CompletedTask);

        // Act + Assert — second dispose must not throw.
        registration.Dispose();
        registration.Dispose();
    }

    [Fact]
    public void RegisterRecoveryCallback_WhenCallbackIsNull_Throws()
    {
        // Arrange
        var sut = new BannerService(Substitute.For<IDatabaseService>(), Substitute.For<ITraceLogger>());

        // Act + Assert
        Assert.Throws<ArgumentNullException>(() => sut.RegisterRecoveryCallback(null!));
    }

    [Fact]
    public async Task RegisterRecoveryCallback_WhenCalledTwice_OverwritesPriorCallback()
    {
        // Arrange
        var sut = new BannerService(Substitute.For<IDatabaseService>(), Substitute.For<ITraceLogger>());
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
        var sut = new BannerService(Substitute.For<IDatabaseService>(), Substitute.For<ITraceLogger>());
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
        var sut = new BannerService(Substitute.For<IDatabaseService>(), Substitute.For<ITraceLogger>());
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
        var sut = new BannerService(Substitute.For<IDatabaseService>(), Substitute.For<ITraceLogger>());

        // Act + Assert
        Assert.Throws<ArgumentNullException>(() => sut.ReportCritical(null!));
    }

    [Fact]
    public void ReportError_AppendsToList_RaisesStateChanged()
    {
        // Arrange
        var sut = new BannerService(Substitute.For<IDatabaseService>(), Substitute.For<ITraceLogger>());
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
        var sut = new BannerService(Substitute.For<IDatabaseService>(), Substitute.For<ITraceLogger>());

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
        var sut = new BannerService(Substitute.For<IDatabaseService>(), Substitute.For<ITraceLogger>());

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
        var sut = new BannerService(Substitute.For<IDatabaseService>(), Substitute.For<ITraceLogger>());
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
        var sut = new BannerService(Substitute.For<IDatabaseService>(), Substitute.For<ITraceLogger>());
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
        var sut = new BannerService(Substitute.For<IDatabaseService>(), Substitute.For<ITraceLogger>());
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
        var sut = new BannerService(Substitute.For<IDatabaseService>(), Substitute.For<ITraceLogger>());
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
        var sut = new BannerService(Substitute.For<IDatabaseService>(), Substitute.For<ITraceLogger>());

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
        var sut = new BannerService(Substitute.For<IDatabaseService>(), Substitute.For<ITraceLogger>());
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
        var sut = new BannerService(Substitute.For<IDatabaseService>(), Substitute.For<ITraceLogger>());

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
        var sut = new BannerService(Substitute.For<IDatabaseService>(), Substitute.For<ITraceLogger>());
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
        var sut = new BannerService(Substitute.For<IDatabaseService>(), Substitute.For<ITraceLogger>());
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
        var sut = new BannerService(Substitute.For<IDatabaseService>(), Substitute.For<ITraceLogger>());
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
        var sut = new BannerService(Substitute.For<IDatabaseService>(), Substitute.For<ITraceLogger>());
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
        var sut = new BannerService(Substitute.For<IDatabaseService>(), Substitute.For<ITraceLogger>());
        sut.ReportCritical(new InvalidOperationException("boom"));

        // Act
        await sut.TryRecoverAsync();

        // Assert
        Assert.Null(sut.CurrentCritical);
    }

    [Fact]
    public void UpgradeBatchCompleted_ClearsScopeProgress_RaisesStateChanged()
    {
        // Arrange
        var databaseService = Substitute.For<IDatabaseService>();
        var sut = new BannerService(databaseService, Substitute.For<ITraceLogger>());
        var batchId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        databaseService.UpgradeBatchStarted += Raise.EventWith(
            databaseService,
            new UpgradeBatchStartedEventArgs(batchId, UpgradeProgressScope.Background, 1, cts));
        int stateChangedCount = 0;
        sut.StateChanged += () => stateChangedCount++;

        // Act
        databaseService.UpgradeBatchCompleted += Raise.EventWith(
            databaseService,
            new UpgradeBatchCompletedEventArgs(batchId, new UpgradeBatchResult([], [], []), wasCancelled: false));

        // Assert
        Assert.Null(sut.BackgroundProgress);
        Assert.Equal(1, stateChangedCount);
    }

    [Fact]
    public void UpgradeBatchCompleted_WithStaleBatchId_NoOp_NoStateChanged()
    {
        // Arrange — a Completed event for a batch the slot doesn't hold must not clobber the active
        // slot or raise a spurious StateChanged.
        var databaseService = Substitute.For<IDatabaseService>();
        var sut = new BannerService(databaseService, Substitute.For<ITraceLogger>());
        var currentId = Guid.NewGuid();
        var staleId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        databaseService.UpgradeBatchStarted += Raise.EventWith(
            databaseService,
            new UpgradeBatchStartedEventArgs(currentId, UpgradeProgressScope.Background, 1, cts));
        BannerProgressEntry? snapshotBefore = sut.BackgroundProgress;
        int stateChangedCount = 0;
        sut.StateChanged += () => stateChangedCount++;

        // Act
        databaseService.UpgradeBatchCompleted += Raise.EventWith(
            databaseService,
            new UpgradeBatchCompletedEventArgs(staleId, new UpgradeBatchResult([], [], []), wasCancelled: false));

        // Assert
        Assert.Same(snapshotBefore, sut.BackgroundProgress);
        Assert.Equal(0, stateChangedCount);
    }

    [Fact]
    public void UpgradeBatchProgress_RefreshesQueuedBatchesAfterFromDatabaseService()
    {
        // Arrange — queue depth observed at Started time may stale by the next Progress event because
        // additional batches may be enqueued or consumed between phases. Verify each Progress event
        // re-reads QueuedBatchCount from the database service.
        var databaseService = Substitute.For<IDatabaseService>();
        databaseService.QueuedBatchCount.Returns(0);
        var sut = new BannerService(databaseService, Substitute.For<ITraceLogger>());
        var batchId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        databaseService.UpgradeBatchStarted += Raise.EventWith(
            databaseService,
            new UpgradeBatchStartedEventArgs(batchId, UpgradeProgressScope.Background, 1, cts));
        Assert.Equal(0, sut.BackgroundProgress!.QueuedBatchesAfter);

        // Act — another batch enqueues; Progress event picks up the new count.
        databaseService.QueuedBatchCount.Returns(3);
        databaseService.UpgradeBatchProgress += Raise.EventWith(
            databaseService,
            new UpgradeBatchProgressEventArgs(batchId, 1, "first.db", UpgradePhase.BackingUp));

        // Assert
        Assert.Equal(3, sut.BackgroundProgress.QueuedBatchesAfter);
    }

    [Fact]
    public void UpgradeBatchProgress_UpdatesPositionEntryNameAndPhase_RaisesStateChanged()
    {
        // Arrange
        var databaseService = Substitute.For<IDatabaseService>();
        var sut = new BannerService(databaseService, Substitute.For<ITraceLogger>());
        var batchId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        databaseService.UpgradeBatchStarted += Raise.EventWith(
            databaseService,
            new UpgradeBatchStartedEventArgs(batchId, UpgradeProgressScope.Background, 3, cts));
        int stateChangedCount = 0;
        sut.StateChanged += () => stateChangedCount++;

        // Act
        databaseService.UpgradeBatchProgress += Raise.EventWith(
            databaseService,
            new UpgradeBatchProgressEventArgs(batchId, position: 2, "second.db", UpgradePhase.MigratingSchema));

        // Assert
        Assert.NotNull(sut.BackgroundProgress);
        Assert.Equal(2, sut.BackgroundProgress.CurrentBatchPosition);
        Assert.Equal("second.db", sut.BackgroundProgress.CurrentEntryName);
        Assert.Equal(UpgradePhase.MigratingSchema, sut.BackgroundProgress.CurrentPhase);
        Assert.Equal(3, sut.BackgroundProgress.CurrentBatchSize);
        Assert.Equal(batchId, sut.BackgroundProgress.BatchId);
        Assert.Equal(1, stateChangedCount);
    }

    [Fact]
    public void UpgradeBatchProgress_WithStaleBatchId_NoOp_NoStateChanged()
    {
        // Arrange — a Progress event arriving for a batch the slot doesn't hold is ignored defensively.
        var databaseService = Substitute.For<IDatabaseService>();
        var sut = new BannerService(databaseService, Substitute.For<ITraceLogger>());
        var currentId = Guid.NewGuid();
        var staleId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        databaseService.UpgradeBatchStarted += Raise.EventWith(
            databaseService,
            new UpgradeBatchStartedEventArgs(currentId, UpgradeProgressScope.Background, 1, cts));
        BannerProgressEntry? snapshotBefore = sut.BackgroundProgress;
        int stateChangedCount = 0;
        sut.StateChanged += () => stateChangedCount++;

        // Act — stale-id Progress event must not mutate the slot.
        databaseService.UpgradeBatchProgress += Raise.EventWith(
            databaseService,
            new UpgradeBatchProgressEventArgs(staleId, 99, "ghost.db", UpgradePhase.Verifying));

        // Assert
        Assert.Same(snapshotBefore, sut.BackgroundProgress);
        Assert.Equal(0, stateChangedCount);
    }

    [Fact]
    public void UpgradeBatchStarted_FollowedByCompletedWithNoProgress_HandledCleanly()
    {
        // Arrange — when every entry is rejected during the in-flight precheck (e.g., TOCTOU
        // .upgrade.bak appears), DatabaseService raises Started then Completed without any Progress
        // events. BannerService must still clear the slot and raise StateChanged for both events.
        var databaseService = Substitute.For<IDatabaseService>();
        var sut = new BannerService(databaseService, Substitute.For<ITraceLogger>());
        int stateChangedCount = 0;
        sut.StateChanged += () => stateChangedCount++;
        var batchId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();

        // Act
        databaseService.UpgradeBatchStarted += Raise.EventWith(
            databaseService,
            new UpgradeBatchStartedEventArgs(batchId, UpgradeProgressScope.Background, 1, cts));
        databaseService.UpgradeBatchCompleted += Raise.EventWith(
            databaseService,
            new UpgradeBatchCompletedEventArgs(batchId, new UpgradeBatchResult([], [], []), wasCancelled: false));

        // Assert
        Assert.Null(sut.BackgroundProgress);
        Assert.Equal(2, stateChangedCount);
    }

    [Fact]
    public void UpgradeBatchStarted_PopulatesBackgroundProgress_RaisesStateChanged()
    {
        // Arrange
        var databaseService = Substitute.For<IDatabaseService>();
        databaseService.QueuedBatchCount.Returns(2);
        var sut = new BannerService(databaseService, Substitute.For<ITraceLogger>());
        int stateChangedCount = 0;
        sut.StateChanged += () => stateChangedCount++;
        var batchId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        var args = new UpgradeBatchStartedEventArgs(batchId, UpgradeProgressScope.Background, batchSize: 5, cts);

        // Act
        databaseService.UpgradeBatchStarted += Raise.EventWith(databaseService, args);

        // Assert
        Assert.NotNull(sut.BackgroundProgress);
        Assert.Equal(batchId, sut.BackgroundProgress.BatchId);
        Assert.Equal(UpgradeProgressScope.Background, sut.BackgroundProgress.Scope);
        Assert.Equal(0, sut.BackgroundProgress.CurrentBatchPosition);
        Assert.Equal(5, sut.BackgroundProgress.CurrentBatchSize);
        Assert.Equal(string.Empty, sut.BackgroundProgress.CurrentEntryName);
        Assert.Equal(UpgradePhase.BackingUp, sut.BackgroundProgress.CurrentPhase);
        Assert.Equal(2, sut.BackgroundProgress.QueuedBatchesAfter);
        Assert.Null(sut.SettingsProgress);
        Assert.Equal(1, stateChangedCount);
    }

    [Fact]
    public void UpgradeBatchStarted_TwoBatchesDifferentScopes_OnlyOneSlotPopulatedAtATime()
    {
        // Arrange — DatabaseService's FIFO consumer guarantees that at most one batch is in-flight at any
        // time, so Background and Settings slots should never both be non-null. Verifies the runtime
        // invariant: a Started must be paired with a Completed before the next Started fires.
        var databaseService = Substitute.For<IDatabaseService>();
        var sut = new BannerService(databaseService, Substitute.For<ITraceLogger>());
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        using var firstCts = new CancellationTokenSource();
        using var secondCts = new CancellationTokenSource();

        // Act + Assert — Background started.
        databaseService.UpgradeBatchStarted += Raise.EventWith(
            databaseService,
            new UpgradeBatchStartedEventArgs(firstId, UpgradeProgressScope.Background, 1, firstCts));
        Assert.NotNull(sut.BackgroundProgress);
        Assert.Null(sut.SettingsProgress);

        // Background completed before Settings started.
        databaseService.UpgradeBatchCompleted += Raise.EventWith(
            databaseService,
            new UpgradeBatchCompletedEventArgs(firstId, new UpgradeBatchResult([], [], []), wasCancelled: false));
        Assert.Null(sut.BackgroundProgress);
        Assert.Null(sut.SettingsProgress);

        // Settings started.
        databaseService.UpgradeBatchStarted += Raise.EventWith(
            databaseService,
            new UpgradeBatchStartedEventArgs(secondId, UpgradeProgressScope.SettingsTriggered, 1, secondCts));
        Assert.Null(sut.BackgroundProgress);
        Assert.NotNull(sut.SettingsProgress);
    }

    [Fact]
    public void UpgradeBatchStarted_WithSettingsScope_PopulatesSettingsProgress_NotBackground()
    {
        // Arrange
        var databaseService = Substitute.For<IDatabaseService>();
        var sut = new BannerService(databaseService, Substitute.For<ITraceLogger>());
        var batchId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        var args = new UpgradeBatchStartedEventArgs(batchId, UpgradeProgressScope.SettingsTriggered, batchSize: 1, cts);

        // Act
        databaseService.UpgradeBatchStarted += Raise.EventWith(databaseService, args);

        // Assert
        Assert.Null(sut.BackgroundProgress);
        Assert.NotNull(sut.SettingsProgress);
        Assert.Equal(batchId, sut.SettingsProgress.BatchId);
        Assert.Equal(UpgradeProgressScope.SettingsTriggered, sut.SettingsProgress.Scope);
    }
}
