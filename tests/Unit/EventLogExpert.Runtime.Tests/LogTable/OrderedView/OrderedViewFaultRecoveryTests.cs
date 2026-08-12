// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using NSubstitute;
using Reducers = EventLogExpert.Runtime.LogTable.Reducers;

namespace EventLogExpert.Runtime.Tests.LogTable.OrderedView;

public sealed class OrderedViewFaultRecoveryTests
{
    [Fact]
    public async Task AFaultAfterRecovering_IsStillAnnounced()
    {
        await using var writer = new OrderedViewWriter(publishIntervalMs: 0);

        int announced = 0;
        writer.FaultRaised += (_, _) => Interlocked.Increment(ref announced);

        var logId = EventLogId.Create();

        writer.EnqueueViewRequest(ViewRequests.For(
            new SortContext(ColumnName.RecordId, false, null, false),
            new Filter(null, []),
            [logId],
            static (_, _) => throw new InvalidOperationException("a predicate that will not run"),
            readers: Readers(logId),
            activeLogId: logId));

        await writer.DrainAsync();

        Assert.NotNull(writer.Faulted);
        Assert.Equal(1, Volatile.Read(ref announced));

        writer.EnqueueViewRequest(ViewRequests.For(
            new SortContext(ColumnName.Source, false, null, false),
            new Filter(null, []),
            [logId],
            static (_, _) => throw new InvalidOperationException("still latched"),
            readers: Readers(logId),
            activeLogId: logId));

        await writer.DrainAsync();

        Assert.Equal(1, Volatile.Read(ref announced));

        writer.EnqueueClearFault();
        await writer.DrainAsync();

        Assert.Null(writer.Faulted);

        writer.EnqueueViewRequest(ViewRequests.For(
            new SortContext(ColumnName.Level, false, null, false),
            new Filter(null, []),
            [logId],
            static (_, _) => throw new InvalidOperationException("after recovery"),
            readers: Readers(logId),
            activeLogId: logId));

        await writer.DrainAsync();

        Assert.Equal(2, Volatile.Read(ref announced));
    }

    [Fact]
    public void AFaultCauseCutMidCharacter_DoesNotLeaveHalfOfOneBehind()
    {
        var logId = EventLogId.Create();

        LogTableState faulted = Reducers.ReduceOrderedViewDisplayFaulted(
            new LogTableState
            {
                ActiveEventLogId = logId, EventTables = [new LogView(logId) { LogName = "Application" }]
            },
            new OrderedViewDisplayFaultedAction(new InvalidOperationException(new string('x', 199) + "\U0001F600 tail")));

        Assert.NotNull(faulted.FaultCause);
        Assert.DoesNotContain(faulted.FaultCause, char.IsSurrogate);
    }

    [Fact]
    public void AFaultCauseFromAnUnboundedMessage_IsKeptShortEnoughToHold()
    {
        var logId = EventLogId.Create();

        LogTableState faulted = Reducers.ReduceOrderedViewDisplayFaulted(
            new LogTableState
            {
                ActiveEventLogId = logId, EventTables = [new LogView(logId) { LogName = "Application" }]
            },
            new OrderedViewDisplayFaultedAction(new InvalidOperationException(new string('x', 5000))));

        Assert.NotNull(faulted.FaultCause);
        Assert.True(faulted.FaultCause.Length < 300, $"cause was {faulted.FaultCause.Length} characters");
    }

    [Fact]
    public void AFaultForARequestTheDisplayHasMovedOnFrom_DoesNotBlankIt()
    {
        var logId = EventLogId.Create();

        var serving = new LogTableState
        {
            ActiveEventLogId = logId, EventTables = [new LogView(logId) { LogName = "Application" }]
        };

        var stale = new OrderedViewDisplayFaultedAction(
            new InvalidOperationException("a question already abandoned"),
            ViewRequests.Identity([logId], logId, ColumnName.Level));

        LogTableState after = Reducers.ReduceOrderedViewDisplayFaulted(serving, stale);

        Assert.True(after.OrderedViewDisplayEnabled);
        Assert.Null(after.FaultCause);
    }

    [Fact]
    public async Task AFaultNamingARequestTheDisplayHasMovedOnFrom_ClaimsNothingAndAsksNothing()
    {
        await using var harness = new OrderedViewShadowHarness();

        var logId = EventLogId.Create();
        LogTableState superseded = StateWith(logId);
        LogTableState current = StateWith(logId) with { RequestedOrderBy = ColumnName.Level };

        harness.SetState(current, new RawEventStoreState(), new EventLogState());

        await harness.Effects.HandleOrderedViewDisplayFaulted(
            new OrderedViewDisplayFaultedAction(
                new InvalidOperationException("engine blew up"),
                superseded.ViewIdentity),
            harness.Dispatcher);

        harness.Dispatcher.DidNotReceive().Dispatch(Arg.Any<OrderedViewDisplayRecoveredAction>());
        harness.Dispatcher.DidNotReceive().Dispatch(Arg.Any<ViewRequestInvalidatedAction>());

        Assert.True(harness.Issuer.TryBeginRecovery(current.ViewIdentity, current.LastPublishedSnapshotVersion));
    }

    [Fact]
    public void AFaultOnATabWhoseGroupHasVanished_StopsBeingReported_RatherThanStrandingTheDisplay()
    {
        var logId = EventLogId.Create();
        var vanishedGroupId = LogTabGroupId.Create();

        LogTableState faulted = Reducers.ReduceOrderedViewDisplayFaulted(
            new LogTableState
            {
                ActiveEventLogId = logId,
                EventTables = [new LogView(logId) { LogName = "Application", GroupId = vanishedGroupId }],
                Groups = [new LogTabGroup(vanishedGroupId, "Servers", [logId])]
            },
            Faults.Any);

        Assert.Equal(PresentationState.Faulted, faulted.PresentationState);

        LogTableState afterTheGroupWasDeleted = faulted with { Groups = [] };

        Assert.Equal(PresentationState.Current, afterTheGroupWasDeleted.PresentationState);
        Assert.False(afterTheGroupWasDeleted.OrderingIsStale, "the two derivations must agree on a settled tab");
    }

    [Fact]
    public void AFaultOutlivingTheTabItHappenedOn_StopsBeingReported_BecauseNothingIsPendingAnyMore()
    {
        var logId = EventLogId.Create();

        LogTableState faulted = Reducers.ReduceOrderedViewDisplayFaulted(
            new LogTableState
            {
                ActiveEventLogId = logId, EventTables = [new LogView(logId) { LogName = "Application" }]
            },
            Faults.Any);

        Assert.Equal(PresentationState.Faulted, faulted.PresentationState);

        LogTableState afterTheTabClosed = faulted with { ActiveEventLogId = null };

        Assert.Equal(PresentationState.Current, afterTheTabClosed.PresentationState);
    }

    [Fact]
    public async Task AFaultThatNamesNoRequest_IsLeftToTheEngineThatAlreadyRepairsIt()
    {
        await using var harness = new OrderedViewShadowHarness();

        LogTableState state = StateWith(EventLogId.Create());
        harness.SetState(state, new RawEventStoreState(), new EventLogState());

        await harness.Effects.HandleOrderedViewDisplayFaulted(Faults.Any, harness.Dispatcher);

        harness.Dispatcher.DidNotReceive().Dispatch(Arg.Any<OrderedViewDisplayRecoveredAction>());
        Assert.True(harness.Issuer.TryBeginRecovery(state.ViewIdentity, state.LastPublishedSnapshotVersion));
    }

    [Fact]
    public async Task AFault_ForgetsTheFailedRequestSoTheRetryCanActuallyBeIssued()
    {
        await using var harness = new OrderedViewShadowHarness();

        LogTableState state = StateWith(EventLogId.Create());
        harness.SetState(state, new RawEventStoreState(), new EventLogState());

        ViewIdentity identity = state.ViewIdentity;

        Assert.NotNull(harness.Issuer.TryIssue(identity));
        Assert.Null(harness.Issuer.TryIssue(identity));

        await harness.Effects.HandleOrderedViewDisplayFaulted(
            new OrderedViewDisplayFaultedAction(new InvalidOperationException("engine blew up"), identity),
            harness.Dispatcher);

        harness.Dispatcher.Received(1).Dispatch(Arg.Any<ViewRequestInvalidatedAction>());
    }

    [Fact]
    public async Task AFault_ReTrustsTheEngineBeforeAnythingCanBeAdoptedUnderIt()
    {
        await using var harness = new OrderedViewShadowHarness();

        LogTableState state = StateWith(EventLogId.Create());
        harness.SetState(state, new RawEventStoreState(), new EventLogState());

        await harness.Effects.HandleOrderedViewDisplayFaulted(
            new OrderedViewDisplayFaultedAction(new InvalidOperationException("engine blew up"), state.ViewIdentity),
            harness.Dispatcher);

        harness.Dispatcher.Received(1).Dispatch(Arg.Any<OrderedViewDisplayRecoveredAction>());
    }

    [Fact]
    public void AFault_SaysWhatWentWrong_AndTheAnswerThatFollowsClearsIt()
    {
        var logId = EventLogId.Create();

        LogTableState faulted = Reducers.ReduceOrderedViewDisplayFaulted(
            new LogTableState
            {
                ActiveEventLogId = logId, EventTables = [new LogView(logId) { LogName = "Application" }]
            },
            new OrderedViewDisplayFaultedAction(new InvalidOperationException("a predicate that will not compile")));

        Assert.Contains("InvalidOperationException", faulted.FaultCause);
        Assert.Contains("will not compile", faulted.FaultCause);

        LogTableState recovered = Reducers.ReduceOrderedViewUpdated(faulted, ReadyFor(faulted, logId));

        Assert.Null(recovered.FaultCause);
    }

    [Fact]
    public void ARecoveredFrameWithASortStillPending_IsActuallyServed_NotJustTrustedAgain()
    {
        var logId = EventLogId.Create();

        LogTableState faulted = Reducers.ReduceOrderedViewDisplayFaulted(
            new LogTableState
            {
                ActiveEventLogId = logId,
                EventTables = [new LogView(logId) { LogName = "Application" }],
                RequestedOrderBy = ColumnName.Source
            },
            Faults.Any);

        Assert.Equal(PresentationState.Faulted, faulted.PresentationState);
        Assert.True(faulted.HasPendingSortChange);

        LogTableState recovered = Reducers.ReduceOrderedViewUpdated(faulted, ReadyFor(faulted, logId));

        Assert.True(recovered.OrderedViewDisplayEnabled);
        Assert.False(recovered.HasPendingSortChange, "the rescuing frame must settle the ordering it was built under");
        Assert.Equal(PresentationState.Current, recovered.PresentationState);
    }

    [Fact]
    public async Task AnAnswerBetweenTwoFaults_EarnsTheSameRequestAnotherAttempt()
    {
        await using var harness = new OrderedViewShadowHarness();

        var logId = EventLogId.Create();
        LogTableState state = StateWith(logId);
        harness.SetState(state, new RawEventStoreState(), new EventLogState());

        ViewIdentity identity = state.ViewIdentity;
        long liveTailSequence = state.HighestInvalidationSequence;
        var fault = new OrderedViewDisplayFaultedAction(new InvalidOperationException("engine blew up"), identity);

        await harness.Effects.HandleOrderedViewDisplayFaulted(fault, harness.Dispatcher);

        var reIssue = harness.Dispatcher.ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .OfType<ViewRequestInvalidatedAction>()
            .Last();

        state = Reducers.ReduceViewRequestInvalidated(state, reIssue);
        Assert.True(state.HighestInvalidationSequence > liveTailSequence);

        for (var tail = 1; tail <= 4; tail++)
        {
            LogTableState published = Reducers.ReduceOrderedViewUpdated(
                state,
                ReadyAt(state, logId, state.LastPublishedSnapshotVersion + tail, liveTailSequence));

            Assert.Equal(state.LastPublishedSnapshotVersion, published.LastPublishedSnapshotVersion);

            state = published;
            harness.SetState(state, new RawEventStoreState(), new EventLogState());

            await harness.Effects.HandleOrderedViewDisplayFaulted(fault, harness.Dispatcher);
        }

        harness.Dispatcher.Received(1).Dispatch(Arg.Any<OrderedViewDisplayRecoveredAction>());

        state = Reducers.ReduceOrderedViewUpdated(
            state,
            ReadyAt(state, logId, state.LastPublishedSnapshotVersion + 1, state.HighestInvalidationSequence));

        harness.SetState(state, new RawEventStoreState(), new EventLogState());

        await harness.Effects.HandleOrderedViewDisplayFaulted(fault, harness.Dispatcher);

        harness.Dispatcher.Received(2).Dispatch(Arg.Any<OrderedViewDisplayRecoveredAction>());
    }

    [Fact]
    public async Task ChangingTheDisplayAfterAFault_EarnsAFreshAttempt()
    {
        await using var harness = new OrderedViewShadowHarness();

        var logId = EventLogId.Create();
        LogTableState first = StateWith(logId);
        harness.SetState(first, new RawEventStoreState(), new EventLogState());

        await harness.Effects.HandleOrderedViewDisplayFaulted(
            new OrderedViewDisplayFaultedAction(new InvalidOperationException("engine blew up"), first.ViewIdentity),
            harness.Dispatcher);

        LogTableState second = StateWith(logId) with { RequestedOrderBy = ColumnName.Level };
        harness.SetState(second, new RawEventStoreState(), new EventLogState());

        await harness.Effects.HandleOrderedViewDisplayFaulted(
            new OrderedViewDisplayFaultedAction(new InvalidOperationException("engine blew up"), second.ViewIdentity),
            harness.Dispatcher);

        harness.Dispatcher.Received(2).Dispatch(Arg.Any<OrderedViewDisplayRecoveredAction>());
    }

    [Fact]
    public void ClosingEverything_RetiresTheClaimSoAReopenedDisplayCanFailOnItsOwnTerms()
    {
        var issuer = new ViewRequestIssuer();
        ViewIdentity identity = StateWith(EventLogId.Create()).ViewIdentity;

        Assert.True(issuer.TryBeginRecovery(identity, servedWatermark: 0));
        Assert.False(issuer.TryBeginRecovery(identity, servedWatermark: 0));

        issuer.ResetForCloseAll();

        Assert.True(issuer.TryBeginRecovery(identity, servedWatermark: 0));
    }

    [Fact]
    public void ForgettingTheIssuedRequestToRetryIt_DoesNotAlsoForgetThatItWasTried()
    {
        var issuer = new ViewRequestIssuer();
        ViewIdentity identity = StateWith(EventLogId.Create()).ViewIdentity;

        Assert.True(issuer.TryBeginRecovery(identity, servedWatermark: 0));

        issuer.ResetForClear();

        Assert.False(issuer.TryBeginRecovery(identity, servedWatermark: 0));
    }

    [Fact]
    public void ManyThreadsClaimingTheSameRecovery_ProduceExactlyOneAttempt()
    {
        ViewIdentity identity = StateWith(EventLogId.Create()).ViewIdentity;
        var contenders = Math.Max(Environment.ProcessorCount, 4);

        for (var round = 0; round < 200; round++)
        {
            var issuer = new ViewRequestIssuer();
            using var release = new Barrier(contenders);
            var claims = 0;

            Parallel.For(0, contenders, _ =>
            {
                release.SignalAndWait();

                if (issuer.TryBeginRecovery(identity, servedWatermark: 0)) { Interlocked.Increment(ref claims); }
            });

            Assert.Equal(1, claims);
        }
    }

    [Fact]
    public void Recovering_MovesTheDisplayFromFailedToWaiting_NotStraightBackToCurrent()
    {
        var logId = EventLogId.Create();

        LogTableState faulted = Reducers.ReduceOrderedViewDisplayFaulted(
            new LogTableState
            {
                ActiveEventLogId = logId,
                EventTables = [new LogView(logId) { LogName = "Application" }]
            }, Faults.Any);

        Assert.Equal(PresentationState.Faulted, faulted.PresentationState);

        LogTableState recovered = Reducers.ReduceOrderedViewDisplayRecovered(faulted);

        Assert.True(recovered.OrderedViewDisplayEnabled);
        Assert.Null(recovered.ActiveOrderedView);
        Assert.Equal(PresentationState.Updating, recovered.PresentationState);
    }

    [Fact]
    public void Recovering_TakesTheReasonWithIt_SoWaitingNeverCarriesAStaleOne()
    {
        var logId = EventLogId.Create();

        LogTableState faulted = Reducers.ReduceOrderedViewDisplayFaulted(
            new LogTableState
            {
                ActiveEventLogId = logId, EventTables = [new LogView(logId) { LogName = "Application" }]
            },
            Faults.Any);

        Assert.NotNull(faulted.FaultCause);

        LogTableState recovered = Reducers.ReduceOrderedViewDisplayRecovered(faulted);

        Assert.Equal(PresentationState.Updating, recovered.PresentationState);
        Assert.Null(recovered.FaultCause);
    }

    [Fact]
    public async Task TheSameFailingRequest_IsRetriedOnce_NotForever()
    {
        await using var harness = new OrderedViewShadowHarness();

        LogTableState state = StateWith(EventLogId.Create());
        harness.SetState(state, new RawEventStoreState(), new EventLogState());

        var fault = new OrderedViewDisplayFaultedAction(
            new InvalidOperationException("engine blew up"),
            state.ViewIdentity);

        await harness.Effects.HandleOrderedViewDisplayFaulted(fault, harness.Dispatcher);
        await harness.Effects.HandleOrderedViewDisplayFaulted(fault, harness.Dispatcher);
        await harness.Effects.HandleOrderedViewDisplayFaulted(fault, harness.Dispatcher);

        harness.Dispatcher.Received(1).Dispatch(Arg.Any<OrderedViewDisplayRecoveredAction>());
    }

    private static IReadOnlyDictionary<EventLogId, IEventColumnReader> Readers(EventLogId logId)
    {
        List<ResolvedEvent> events =
        [
            new("Application", LogPathType.Channel)
            {
                RecordId = 1,
                TimeCreated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Id = 1000,
                Level = "Information",
                Source = "Provider.A",
                LogName = "Application"
            }
        ];

        return new Dictionary<EventLogId, IEventColumnReader>
        {
            [logId] = EventColumnStore.Build(events, generation: 0, contentVersion: 0).CreateReader(logId)
        };
    }

    private static OrderedViewUpdatedAction ReadyAt(
        LogTableState state,
        EventLogId logId,
        long snapshotVersion,
        long sequence) =>
        new(new OrderedViewReady(
            SnapshotVersion: snapshotVersion,
            Identity: state.ViewIdentity,
            Sequence: sequence,
            SingleLogId: logId,
            InScope: [new LogGeneration(logId, 0)],
            View: EmptyColumnView.Instance,
            Config: state.SortContext,
            Filter: state.AppliedFilter));

    private static OrderedViewUpdatedAction ReadyFor(LogTableState state, EventLogId logId) =>
        new(new OrderedViewReady(
            SnapshotVersion: state.LastPublishedSnapshotVersion + 1,
            Identity: state.ViewIdentity,
            Sequence: state.HighestInvalidationSequence,
            SingleLogId: logId,
            InScope: [new LogGeneration(logId, 0)],
            View: EmptyColumnView.Instance,
            Config: state.SortContext,
            Filter: state.AppliedFilter));

    private static LogTableState StateWith(EventLogId logId) =>
        new()
        {
            ActiveEventLogId = logId,
            EventTables = [new LogView(logId) { LogName = "Application" }]
        };
}
