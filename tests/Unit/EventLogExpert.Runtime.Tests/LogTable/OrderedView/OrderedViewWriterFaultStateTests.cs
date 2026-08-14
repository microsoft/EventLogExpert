// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;

namespace EventLogExpert.Runtime.Tests.LogTable.OrderedView;

public sealed class OrderedViewWriterFaultStateTests
{
    private static readonly SortContext s_context = new(ColumnName.RecordId, false, null, false);

    private static TimeSpan SignalTimeout => TimeSpan.FromSeconds(20);
    private static TimeSpan Timeout => TimeSpan.FromSeconds(30);

    [Fact]
    public async Task ABuildAbandonedWhileAdopting_AlsoLeavesNoTokenBehind()
    {
        EventLogId logId = EventLogId.Create();

        await using var writer = new OrderedViewWriter(publishEvery: 1, publishIntervalMs: 0);

        writer.EnqueueReconcile(logId, Reader(logId, count: 3000));
        writer.EnqueueViewRequest(Request(logId, static (locator, _) => locator.Index >= 3000 ? throw new InvalidOperationException("tail") : true, ColumnName.Source));
        writer.EnqueueReconcile(logId, Reader(logId, count: 3200));
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.NotNull(writer.Faulted);

        int startedAfterAbandon = writer.BuildsStarted;

        writer.EnqueueViewRequest(Request(logId, orderBy: ColumnName.Source));
        OrderedViewSnapshot snapshot = await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.True(writer.BuildsStarted > startedAfterAbandon, "the retry must START a build, not attach to the abandoned one");
        Assert.Equal(3200, snapshot.Count);
    }

    [Fact]
    public async Task ADamagedIndex_IsNeverHandedToTheDisplay_BeforeTheRebuildThatRepairsItLands()
    {
        EventLogId logId = EventLogId.Create();
        int thrown = 0;
        List<int> publishedCounts = [];

        await using var writer = new OrderedViewWriter(publishEvery: 1, publishIntervalMs: 0);

        writer.Updated += update =>
        {
            if (update is OrderedViewReady ready) { lock (publishedCounts) { publishedCounts.Add(ready.View.Count); } }
        };

        writer.EnqueueReconcile(logId, Reader(logId, count: 100));
        writer.EnqueueViewRequest(Request(logId,
            (locator, _) => locator.Index < 100 || Interlocked.Exchange(ref thrown, 1) != 0
                ? true
                : throw new InvalidOperationException("a row that could not be ordered")));

        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        writer.EnqueueReconcile(logId, Reader(logId, count: 150));
        writer.EnqueueReconcile(logId, Reader(logId, count: 180));
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        lock (publishedCounts)
        {
            Assert.DoesNotContain(publishedCounts, count => count is > 100 and < 150);
        }
    }

    [Fact]
    public async Task AFailedBuild_LeavesNoTokenBehind_SoTheSameRequestCanBeBuiltAgain()
    {
        EventLogId logId = EventLogId.Create();

        await using var writer = new OrderedViewWriter(publishEvery: 1, publishIntervalMs: 0);

        writer.EnqueueReconcile(logId, Reader(logId, count: 50));
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        writer.EnqueueViewRequest(Request(logId, static (_, _) => throw new InvalidOperationException("first build")));
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.NotNull(writer.Faulted);

        int startedAfterFailure = writer.BuildsStarted;

        writer.EnqueueViewRequest(Request(logId));
        OrderedViewSnapshot snapshot = await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.True(writer.BuildsStarted > startedAfterFailure, "the retry must START a build, not re-tag the dead one");
        Assert.Equal(50, snapshot.Count);
    }

    [Fact]
    public async Task AFaultFromASupersededBuild_LeavesTheBuildThatReplacedItAlone()
    {
        EventLogId logId = EventLogId.Create();

        using var parked = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);

        CancellationToken token = TestContext.Current.CancellationToken;

        await using var writer = new OrderedViewWriter(publishEvery: 1, publishIntervalMs: 0);

        writer.EnqueueReconcile(logId, Reader(logId, count: 200));

        writer.EnqueueViewRequest(Request(logId, (locator, _) =>
        {
            if (locator.Index != 0) { return true; }

            entered.Set();
            parked.Wait(SignalTimeout, token);

            throw new InvalidOperationException("superseded, then failed for real");
        }));

        Assert.True(entered.Wait(SignalTimeout, token), "the first build must actually be running");

        ViewRequest replacement = Request(logId, orderBy: ColumnName.Source);
        writer.EnqueueViewRequest(replacement);

        parked.Set();

        OrderedViewUpdate? last = null;
        writer.Updated += update => Volatile.Write(ref last, update);

        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        var ready = Assert.IsType<OrderedViewReady>(Volatile.Read(ref last));

        Assert.Equal(replacement.Identity, ready.Identity);
    }

    [Fact]
    public async Task AFaultRaisedAfterAnAdopt_IsAnnouncedAgain_ButTheRecordOfTheFirstOneSurvivesIt()
    {
        EventLogId logId = EventLogId.Create();

        await using var writer = new OrderedViewWriter(publishEvery: 1, publishIntervalMs: 0);

        int announced = 0;
        writer.FaultRaised += (_, _) => Interlocked.Increment(ref announced);

        writer.EnqueueReconcile(logId, Reader(logId, count: 40));
        writer.EnqueueViewRequest(Request(logId, static (_, _) => throw new InvalidOperationException("first")));
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.Equal(1, Volatile.Read(ref announced));

        writer.EnqueueViewRequest(Request(logId, orderBy: ColumnName.Source));
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.NotNull(writer.Faulted);

        writer.EnqueueViewRequest(Request(logId, static (_, _) => throw new InvalidOperationException("second"), ColumnName.Level));
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.Equal(2, Volatile.Read(ref announced));
    }

    [Fact]
    public async Task AReconcileThatThrowsPartWayThrough_IsHealedWithoutAnyoneAskingForANewView()
    {
        EventLogId logId = EventLogId.Create();
        int thrown = 0;

        await using var writer = new OrderedViewWriter(publishEvery: 1, publishIntervalMs: 0);

        writer.EnqueueReconcile(logId, Reader(logId, count: 100));

        writer.EnqueueViewRequest(Request(logId,
            (locator, _) => locator.Index < 100 || Interlocked.Exchange(ref thrown, 1) != 0
                ? true
                : throw new InvalidOperationException("a row that could not be ordered")));

        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        writer.EnqueueReconcile(logId, Reader(logId, count: 150));
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.Equal(1, Volatile.Read(ref thrown));

        OrderedViewSnapshot snapshot = await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.Equal(150, snapshot.Count);
    }

    [Fact]
    public async Task ARepairStartedWhileARequestIsInFlight_StillAnswersThatRequest()
    {
        EventLogId logId = EventLogId.Create();
        int parkedOnce = 0;

        using var parked = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);

        CancellationToken token = TestContext.Current.CancellationToken;

        await using var writer = new OrderedViewWriter(publishEvery: 1, publishIntervalMs: 0);

        writer.EnqueueReconcile(logId, Reader(logId, count: 100));
        writer.EnqueueViewRequest(Request(logId, static (locator, _) => locator.Index >= 100 ? throw new InvalidOperationException("unordered") : true));
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        ViewRequest inFlight = Request(logId, (locator, _) =>
        {
            if (locator.Index == 0 && Interlocked.Exchange(ref parkedOnce, 1) == 0)
            {
                entered.Set();
                parked.Wait(SignalTimeout, token);
            }

            return true;
        }, ColumnName.Source);

        writer.EnqueueViewRequest(inFlight);

        Assert.True(entered.Wait(SignalTimeout, token), "the request must still be building when the damage lands");

        writer.EnqueueReconcile(logId, Reader(logId, count: 150));
        parked.Set();

        OrderedViewUpdate? last = null;
        writer.Updated += update => Volatile.Write(ref last, update);

        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        var ready = Assert.IsType<OrderedViewReady>(Volatile.Read(ref last));

        Assert.Equal(inFlight.Identity, ready.Identity);
    }

    [Fact]
    public async Task ARetagThatSeedsNothingNew_LeavesAnIntactViewPublishing()
    {
        EventLogId logId = EventLogId.Create();
        int parkedOnce = 0;
        int raised = 0;

        using var parked = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);

        CancellationToken token = TestContext.Current.CancellationToken;

        await using var writer = new OrderedViewWriter(publishEvery: 1, publishIntervalMs: 0);

        IEventColumnReader settled = Reader(logId, count: 100);

        writer.EnqueueReconcile(logId, settled);
        writer.EnqueueViewRequest(Request(logId));
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        writer.EnqueueViewRequest(Request(logId, (locator, _) =>
        {
            if (locator.Index == 0 && Interlocked.Exchange(ref parkedOnce, 1) == 0)
            {
                entered.Set();
                parked.Wait(SignalTimeout, token);

                throw new InvalidOperationException("the ridden build fails");
            }

            return true;
        }, ColumnName.Source));

        Assert.True(entered.Wait(SignalTimeout, token), "the build must be in flight to be retagged");

        int startedAfterAdopt = writer.BuildsStarted;

        writer.EnqueueViewRequest(ViewRequests.For(
            new SortContext(ColumnName.Source, false, null, false),
            ViewRequests.EmptyFilter,
            [logId],
            static (_, _) => true,
            readers: new Dictionary<EventLogId, IEventColumnReader> { [logId] = Reader(logId, count: 100, contentVersion: 500) },
            activeLogId: logId));

        parked.Set();
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.NotNull(writer.Faulted);

        Assert.Equal(startedAfterAdopt, writer.BuildsStarted);

        writer.Updated += _ => Interlocked.Increment(ref raised);

        writer.EnqueueReconcile(logId, Reader(logId, count: 200));
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.True(Volatile.Read(ref raised) > 0, "an intact view must keep publishing after a retag that owed nothing");
    }

    [Fact]
    public async Task ASubscriberThatThrows_IsNotReportedAsAnEngineFailure_AndDoesNotMuteTheNextRealOne()
    {
        EventLogId logId = EventLogId.Create();

        await using var writer = new OrderedViewWriter(publishEvery: 1, publishIntervalMs: 0);

        int announced = 0;
        writer.FaultRaised += (_, _) => Interlocked.Increment(ref announced);
        writer.Updated += _ => throw new InvalidOperationException("a subscriber that cannot cope");

        writer.EnqueueReconcile(logId, Reader(logId, count: 30));
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.Equal(0, Volatile.Read(ref announced));

        writer.EnqueueViewRequest(Request(logId, static (_, _) => throw new InvalidOperationException("a real one")));
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.Equal(1, Volatile.Read(ref announced));
    }

    [Fact]
    public async Task ATailReplayThatThrows_DoesNotSuppressPublishing_BecauseItDamagedNothing()
    {
        EventLogId logId = EventLogId.Create();
        int raised = 0;

        using var buildEntered = new ManualResetEventSlim(false);
        using var releaseBuild = new ManualResetEventSlim(false);
        int gateArmed = 1;

        await using var writer = new OrderedViewWriter(publishEvery: 1, publishIntervalMs: 0);

        writer.Updated += _ => Interlocked.Increment(ref raised);

        writer.EnqueueReconcile(logId, Reader(logId, count: 3000));
        writer.EnqueueViewRequest(Request(logId, Predicate));
        Assert.True(buildEntered.Wait(SignalTimeout, TestContext.Current.CancellationToken));

        writer.EnqueueReconcile(logId, Reader(logId, count: 3200));
        releaseBuild.Set();
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.NotNull(writer.Faulted);

        int before = Volatile.Read(ref raised);

        writer.EnqueueReconcile(logId, Reader(logId, count: 3400));
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        // The recovery publish raises Updated in RaiseUpdateIfAdvanced, which runs AFTER the drain's TrySetResult
        // completes the await, so wait for the raise rather than reading the counter the instant the drain returns.
        Assert.True(
            SpinWait.SpinUntil(() => Volatile.Read(ref raised) > before, SignalTimeout),
            "publishing must continue after a fault that damaged nothing");

        return;

        // Gate the build at its first row so the growing tail (3200) is enqueued BEFORE the build's adopt is queued.
        // FIFO delivery then guarantees the tail is covered when the adopt runs, so the failure lands in the tail
        // REPLAY (which damaged nothing) rather than in a live insert (which would legitimately require a rebuild).
        bool Predicate(EventLocator locator, IEventColumnReader reader)
        {
            if (locator.Index == 0 && Interlocked.Exchange(ref gateArmed, 0) == 1)
            {
                buildEntered.Set();
                releaseBuild.Wait(SignalTimeout);
            }

            return locator.Index >= 3000 ? throw new InvalidOperationException("tail") : true;
        }
    }

    [Fact]
    public async Task ClosingEverythingAfterAFailedRepair_ReportsTheNextFailureAgain()
    {
        EventLogId logId = EventLogId.Create();

        await using var writer = new OrderedViewWriter(publishEvery: 1, publishIntervalMs: 0);

        int announced = 0;
        writer.FaultRaised += (_, _) => Interlocked.Increment(ref announced);

        writer.EnqueueReconcile(logId, Reader(logId, count: 100));
        writer.EnqueueViewRequest(Request(logId, static (locator, _) => locator.Index >= 100 ? throw new InvalidOperationException("a row that can never be ordered") : true));
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        writer.EnqueueReconcile(logId, Reader(logId, count: 150));
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.Equal(1, Volatile.Read(ref announced));

        writer.EnqueueClear(ViewRequests.Identity(), ViewRequests.NextSequence());
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        EventLogId reopened = EventLogId.Create();
        IEventColumnReader reopenedReader = Reader(reopened, count: 20);

        writer.EnqueueReconcile(reopened, reopenedReader);

        writer.EnqueueViewRequest(ViewRequests.For(
            s_context,
            ViewRequests.EmptyFilter,
            [reopened],
            static (_, _) => throw new InvalidOperationException("the next one"),
            readers: new Dictionary<EventLogId, IEventColumnReader> { [reopened] = reopenedReader },
            activeLogId: reopened));

        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.Equal(2, Volatile.Read(ref announced));
    }

    [Fact]
    public async Task ClosingEverything_ReleasesADisplayStrandedByARepairThatCouldNotSucceed()
    {
        EventLogId logId = EventLogId.Create();

        await using var writer = new OrderedViewWriter(publishEvery: 1, publishIntervalMs: 0);

        writer.EnqueueReconcile(logId, Reader(logId, count: 100));
        writer.EnqueueViewRequest(Request(logId, static (locator, _) => locator.Index >= 100 ? throw new InvalidOperationException("a row that can never be ordered") : true));
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        writer.EnqueueReconcile(logId, Reader(logId, count: 150));
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        OrderedViewUpdate? afterClose = null;
        writer.Updated += update => Volatile.Write(ref afterClose, update);

        writer.EnqueueClear(ViewRequests.Identity(), ViewRequests.NextSequence());
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.NotNull(Volatile.Read(ref afterClose));
    }

    [Fact]
    public async Task OnceTheOwedRowsArePlaced_AViewTheEngineAlreadyHolds_IsRestampedAgain()
    {
        EventLogId logId = EventLogId.Create();
        int parkedOnce = 0;

        using var parked = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);

        CancellationToken token = TestContext.Current.CancellationToken;

        await using var writer = new OrderedViewWriter(publishEvery: 1, publishIntervalMs: 0);

        writer.EnqueueReconcile(logId, Reader(logId, count: 100));
        writer.EnqueueViewRequest(Request(logId));
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        IEventColumnReader grown = Reader(logId, count: 150);

        writer.EnqueueViewRequest(Request(logId, (locator, _) =>
        {
            if (locator.Index == 0 && Interlocked.Exchange(ref parkedOnce, 1) == 0)
            {
                entered.Set();
                parked.Wait(SignalTimeout, token);
            }

            return true;
        }, ColumnName.Source));

        Assert.True(entered.Wait(SignalTimeout, token), "the build must be in flight to be retagged");

        writer.EnqueueViewRequest(ViewRequests.For(
            new SortContext(ColumnName.Source, false, null, false),
            ViewRequests.EmptyFilter,
            [logId],
            static (_, _) => true,
            readers: new Dictionary<EventLogId, IEventColumnReader> { [logId] = grown },
            activeLogId: logId));

        parked.Set();
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        int startedBeforeRestamp = writer.BuildsStarted;

        writer.EnqueueViewRequest(ViewRequests.For(
            new SortContext(ColumnName.Source, false, null, false),
            ViewRequests.EmptyFilter,
            [logId],
            static (_, _) => true,
            readers: new Dictionary<EventLogId, IEventColumnReader> { [logId] = grown },
            activeLogId: logId,
            sequence: ViewRequests.NextSequence()));

        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.Equal(startedBeforeRestamp, writer.BuildsStarted);
    }

    [Fact]
    public async Task RepeatedDamage_StartsOneRepairPerEpisode_NotOnePerFailure()
    {
        EventLogId logId = EventLogId.Create();
        int thrown = 0;

        await using var writer = new OrderedViewWriter(publishEvery: 1, publishIntervalMs: 0);

        writer.EnqueueReconcile(logId, Reader(logId, count: 100));

        writer.EnqueueViewRequest(Request(logId,
            (locator, _) => locator.Index < 100 || Interlocked.Increment(ref thrown) > 3
                ? true
                : throw new InvalidOperationException("a row that could not be ordered")));

        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        int startedBeforeDamage = writer.BuildsStarted;

        writer.EnqueueReconcile(logId, Reader(logId, count: 150));
        writer.EnqueueReconcile(logId, Reader(logId, count: 200));
        writer.EnqueueReconcile(logId, Reader(logId, count: 250));
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.Equal(1, writer.BuildsStarted - startedBeforeDamage);
    }

    [Fact]
    public async Task SeedingTwoGrownLogsInOneRetag_TakesRowsFromBothOfThem()
    {
        EventLogId first = EventLogId.Create();
        EventLogId second = EventLogId.Create();

        using var parked = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);

        CancellationToken token = TestContext.Current.CancellationToken;

        await using var writer = new OrderedViewWriter(publishEvery: 1, publishIntervalMs: 0);

        var startingReaders = new Dictionary<EventLogId, IEventColumnReader>
        {
            [first] = Reader(first, count: 50), [second] = Reader(second, count: 50)
        };

        writer.EnqueueViewRequest(ViewRequests.For(
            s_context, ViewRequests.EmptyFilter, [first, second], static (_, _) => true, readers: startingReaders));

        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        writer.EnqueueViewRequest(ViewRequests.For(
            new SortContext(ColumnName.Source, false, null, false),
            ViewRequests.EmptyFilter,
            [first, second],
            (_, _) =>
            {
                entered.Set();
                parked.Wait(SignalTimeout, token);

                return true;
            },
            readers: startingReaders));

        Assert.True(entered.Wait(SignalTimeout, token), "the build must be in flight to be retagged");

        writer.EnqueueViewRequest(ViewRequests.For(
            new SortContext(ColumnName.Source, false, null, false),
            ViewRequests.EmptyFilter,
            [first, second],
            static (_, _) => true,
            readers: new Dictionary<EventLogId, IEventColumnReader>
            {
                [first] = Reader(first, count: 80), [second] = Reader(second, count: 80)
            }));

        parked.Set();
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.Equal(160, writer.Current.Count);
    }

    [Fact]
    public async Task WhileARepairIsOutstanding_AskingForTheSameViewAgain_DoesNotRepublishTheDamagedIndex()
    {
        EventLogId logId = EventLogId.Create();
        int raised = 0;

        await using var writer = new OrderedViewWriter(publishEvery: 1, publishIntervalMs: 0);

        writer.EnqueueReconcile(logId, Reader(logId, count: 100));
        writer.EnqueueViewRequest(Request(logId, static (locator, _) => locator.Index >= 100 ? throw new InvalidOperationException("a row that can never be ordered") : true));
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        writer.EnqueueReconcile(logId, Reader(logId, count: 150));
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        writer.Updated += _ => Interlocked.Increment(ref raised);

        long publishedBefore = writer.Current.Version;

        writer.EnqueueViewRequest(Request(logId, static (locator, _) => locator.Index >= 100 ? throw new InvalidOperationException("still unordered") : true));
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.Equal(0, Volatile.Read(ref raised));
        Assert.Equal(1, writer.Current.Version - publishedBefore);
    }

    [Fact]
    public async Task WhileSeededRowsAreOwed_AViewTheEngineAlreadyHolds_IsRebuiltRatherThanRestamped()
    {
        EventLogId logId = EventLogId.Create();
        int parkedOnce = 0;

        using var parked = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);

        CancellationToken token = TestContext.Current.CancellationToken;

        await using var writer = new OrderedViewWriter(publishEvery: 1, publishIntervalMs: 0);

        writer.EnqueueReconcile(logId, Reader(logId, count: 100));
        writer.EnqueueViewRequest(Request(logId));
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        writer.EnqueueViewRequest(Request(logId, (locator, _) =>
        {
            if (locator.Index == 0 && Interlocked.Exchange(ref parkedOnce, 1) == 0)
            {
                entered.Set();
                parked.Wait(SignalTimeout, token);
            }

            return true;
        }, ColumnName.Source));

        Assert.True(entered.Wait(SignalTimeout, token), "the second view must still be building");

        IEventColumnReader grown = Reader(logId, count: 150);

        writer.EnqueueViewRequest(ViewRequests.For(
            new SortContext(ColumnName.Source, false, null, false),
            ViewRequests.EmptyFilter,
            [logId],
            static (_, _) => true,
            readers: new Dictionary<EventLogId, IEventColumnReader> { [logId] = grown },
            activeLogId: logId));

        int startedBeforeRestamp = writer.BuildsStarted;

        List<int> publishedForRestamp = [];

        ViewRequest restamp = ViewRequests.For(
            s_context,
            ViewRequests.EmptyFilter,
            [logId],
            static (_, _) => true,
            readers: new Dictionary<EventLogId, IEventColumnReader> { [logId] = grown },
            activeLogId: logId);

        writer.Updated += update =>
        {
            if (update is OrderedViewReady ready && ready.Sequence == restamp.Sequence)
            {
                lock (publishedForRestamp) { publishedForRestamp.Add(ready.View.Count); }
            }
        };

        writer.EnqueueViewRequest(restamp);

        parked.Set();
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);
        await writer.DrainAsync().WaitAsync(Timeout, TestContext.Current.CancellationToken);

        lock (publishedForRestamp)
        {
            Assert.DoesNotContain(publishedForRestamp, count => count < 150);
        }
    }

    private static IEventColumnReader Reader(EventLogId logId, int count, int contentVersion = -1)
    {
        var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var events = new List<ResolvedEvent>(count);

        for (int index = 0; index < count; index++)
        {
            events.Add(new ResolvedEvent("Application", LogPathType.Channel)
            {
                RecordId = index,
                TimeCreated = clock.AddSeconds(index),
                Id = 1000 + (index % 7),
                Level = "Information",
                Source = "Provider.A",
                LogName = "Application"
            });
        }

        return EventColumnStore.Build(events, generation: 0, contentVersion: contentVersion < 0 ? count : contentVersion)
            .CreateReader(logId);
    }

    private static ViewRequest Request(
        EventLogId logId,
        Func<EventLocator, IEventColumnReader, bool>? predicate = null,
        ColumnName orderBy = ColumnName.RecordId) =>
        ViewRequests.For(
            orderBy == ColumnName.RecordId ? s_context : new SortContext(orderBy, false, null, false),
            ViewRequests.EmptyFilter,
            [logId],
            predicate,
            activeLogId: logId);
}
