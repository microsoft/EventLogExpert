// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.EventLog;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Runtime.Tests.EventLog;

public sealed class DatabaseCoordinationEffectsReloadTests
{
    private readonly LogCloseCoordinator _closeCoordinator = new();
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();
    private readonly IEventLogCommands _eventLogCommands = Substitute.For<IEventLogCommands>();
    private readonly IState<EventLogState> _eventLogState = Substitute.For<IState<EventLogState>>();
    private readonly ITraceLogger _logger = Substitute.For<ITraceLogger>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void HasActiveLogs_NoActiveLogs_ReturnsFalse()
    {
        _eventLogState.Value.Returns(new EventLogState { ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty });

        var sut = CreateSut();

        Assert.False(sut.HasActiveLogs);
    }

    [Fact]
    public void HasActiveLogs_OneActiveLog_ReturnsTrue()
    {
        var log = new EventLogData("Application", LogPathType.Channel, []);
        _eventLogState.Value.Returns(new EventLogState
        {
            ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty.Add("Application", log),
        });

        var sut = CreateSut();

        Assert.True(sut.HasActiveLogs);
    }

    [Fact]
    public async Task PrepareForDatabaseRemovalAsync_CancelledBeforeEntry_ThrowsAndDoesNotMutateSnapshot()
    {
        var logA = new EventLogData("Application", LogPathType.Channel, []);
        _eventLogState.Value.Returns(new EventLogState
        {
            ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty.Add("Application", logA),
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var snapshot = new LogReopenSnapshot();
        var sut = CreateSut();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            sut.PrepareForDatabaseRemovalAsync(snapshot, cts.Token));

        Assert.Empty(snapshot.Items);
        _dispatcher.DidNotReceiveWithAnyArgs().Dispatch(null!);
    }

    [Fact]
    public async Task PrepareForDatabaseRemovalAsync_CloseFailureMidwayDispatchesAllAndSnapshotsAllDispatchedLogsForCallerReopen()
    {
        var logA = new EventLogData("Application", LogPathType.Channel, []);
        var logB = new EventLogData("Security", LogPathType.Channel, []);
        var logC = new EventLogData("System", LogPathType.Channel, []);
        _eventLogState.Value.Returns(new EventLogState
        {
            ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty
                .Add("Application", logA)
                .Add("Security", logB)
                .Add("System", logC),
        });

        using var cts = new CancellationTokenSource();
        _dispatcher.When(d => d.Dispatch(Arg.Any<CloseLogAction>())).Do(call =>
        {
            var action = (CloseLogAction)call.Args()[0];

            if (action.LogName == "Application")
            {
                _closeCoordinator.CompleteCloseFor(action.LogId);

                return;
            }

            if (action.LogName == "Security") { cts.Cancel(); }
        });

        var snapshot = new LogReopenSnapshot();
        var sut = CreateSut();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sut.PrepareForDatabaseRemovalAsync(snapshot, cts.Token));

        var snapshotNames = snapshot.Items.Select(i => i.Name).ToHashSet();
        Assert.Contains("Application", snapshotNames);
        Assert.Contains("Security", snapshotNames);
        Assert.Contains("System", snapshotNames);
        Assert.Equal(3, snapshot.Items.Count);
    }

    [Fact]
    public async Task ReloadAllActiveLogsAsync_CalledSequentiallyTwice_CoordinatorLockReleasedBetweenCallsAndBothComplete()
    {
        var logA = new EventLogData("Application", LogPathType.Channel, []);
        _eventLogState.Value.Returns(new EventLogState
        {
            ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty.Add("Application", logA),
        });

        _dispatcher.When(d => d.Dispatch(Arg.Any<CloseLogAction>())).Do(call =>
        {
            var action = (CloseLogAction)call.Args()[0];
            _closeCoordinator.CompleteCloseFor(action.LogId);
        });

        var sut = CreateSut();
        await sut.ReloadAllActiveLogsAsync(Ct);
        await sut.ReloadAllActiveLogsAsync(Ct).WaitAsync(TimeSpan.FromSeconds(5), Ct);

        _dispatcher.Received(2).Dispatch(Arg.Any<CloseLogAction>());
        _eventLogCommands.Received(2).OpenLog("Application", LogPathType.Channel, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReloadAllActiveLogsAsync_CancelledBeforeEntry_ThrowsAndDoesNotMutateLogState()
    {
        var logA = new EventLogData("Application", LogPathType.Channel, []);
        _eventLogState.Value.Returns(new EventLogState
        {
            ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty.Add("Application", logA),
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sut = CreateSut();

        await Assert.ThrowsAsync<OperationCanceledException>(() => sut.ReloadAllActiveLogsAsync(cts.Token));

        _dispatcher.DidNotReceiveWithAnyArgs().Dispatch(null!);
        _eventLogCommands.DidNotReceiveWithAnyArgs().OpenLog(null!, default, Ct);
    }

    [Fact]
    public async Task ReloadAllActiveLogsAsync_CloseCompletionNeverSignaledAndTokenCancelled_ThrowsAfterBestEffortReopen()
    {
        var logA = new EventLogData("Application", LogPathType.Channel, []);
        _eventLogState.Value.Returns(new EventLogState
        {
            ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty.Add("Application", logA),
        });

        using var cts = new CancellationTokenSource();
        _dispatcher.When(d => d.Dispatch(Arg.Any<CloseLogAction>())).Do(_ => cts.Cancel());

        var sut = CreateSut();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.ReloadAllActiveLogsAsync(cts.Token));

        _eventLogCommands.Received(1).OpenLog("Application", LogPathType.Channel, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReloadAllActiveLogsAsync_CloseThrowsThenSubsequentCallProceeds_LockReleasedOnFailurePath()
    {
        var logA = new EventLogData("Application", LogPathType.Channel, []);
        _eventLogState.Value.Returns(new EventLogState
        {
            ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty.Add("Application", logA),
        });

        var signalCompletionOnDispatch = false;
        CancellationTokenSource? firstCallCts = null;

        _dispatcher.When(d => d.Dispatch(Arg.Any<CloseLogAction>())).Do(call =>
        {
            if (!signalCompletionOnDispatch)
            {
                // Cancel deterministically at the close-await point (not via a wall-clock timer) so the first
                // reload always fails at the same place and the Received(2) assertion can't race CI scheduling.
                firstCallCts!.Cancel();

                return;
            }

            var action = (CloseLogAction)call.Args()[0];
            _closeCoordinator.CompleteCloseFor(action.LogId);
        });

        var sut = CreateSut();

        using (firstCallCts = new CancellationTokenSource())
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.ReloadAllActiveLogsAsync(firstCallCts.Token));
        }

        signalCompletionOnDispatch = true;
        await sut.ReloadAllActiveLogsAsync(Ct).WaitAsync(TimeSpan.FromSeconds(5), Ct);

        _eventLogCommands.Received(2).OpenLog("Application", LogPathType.Channel, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReloadAllActiveLogsAsync_NoActiveLogs_DoesNotDispatchOrOpen()
    {
        _eventLogState.Value.Returns(new EventLogState { ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty });

        var sut = CreateSut();
        await sut.ReloadAllActiveLogsAsync(Ct);

        _dispatcher.DidNotReceiveWithAnyArgs().Dispatch(null!);
        _eventLogCommands.DidNotReceiveWithAnyArgs().OpenLog(null!, default, Ct);
    }

    [Fact]
    public async Task ReloadAllActiveLogsAsync_TwoActiveLogs_DispatchesPerLogCloseAndAwaitsCompletionBeforeOpening()
    {
        var logA = new EventLogData("Application", LogPathType.Channel, []);
        var logB = new EventLogData("C:/path/security.evtx", LogPathType.File, []);
        _eventLogState.Value.Returns(new EventLogState
        {
            ActiveLogs = ImmutableDictionary<string, EventLogData>.Empty
                .Add("Application", logA)
                .Add("C:/path/security.evtx", logB),
        });

        _dispatcher.When(d => d.Dispatch(Arg.Any<CloseLogAction>())).Do(call =>
        {
            var action = (CloseLogAction)call.Args()[0];
            _closeCoordinator.CompleteCloseFor(action.LogId);
        });

        var sut = CreateSut();
        await sut.ReloadAllActiveLogsAsync(Ct);

        _dispatcher.Received(1).Dispatch(Arg.Is<CloseLogAction>(a => a.LogName == "Application"));
        _dispatcher.Received(1).Dispatch(Arg.Is<CloseLogAction>(a => a.LogName == "C:/path/security.evtx"));
        _dispatcher.DidNotReceive().Dispatch(Arg.Any<CloseAllLogsAction>());
        _eventLogCommands.Received(1).OpenLog("Application", LogPathType.Channel, Arg.Any<CancellationToken>());
        _eventLogCommands.Received(1).OpenLog("C:/path/security.evtx", LogPathType.File, Arg.Any<CancellationToken>());
    }

    private DatabaseCoordinationEffects CreateSut() =>
        new(_eventLogState, _logger, _closeCoordinator, _dispatcher, _eventLogCommands);
}

