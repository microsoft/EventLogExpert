// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using EventLogExpert.Runtime.Tests.LogTable.TestSupport;

namespace EventLogExpert.Runtime.Tests.LogTable.OrderedView;

public sealed class OrderedViewBuildSchedulerTests
{
    private static readonly Filter s_emptyFilter = new(null, []);

    [Fact]
    public async Task Clear_DropsDesiredBuild_SoNoFurtherBuilderStarts()
    {
        EventLogId logId = EventLogId.Create();
        var events = MakeEvents(200);
        IEventColumnReader reader = EventColumnStore.Build(events, generation: 0, contentVersion: 0).CreateReader(logId);

        using var blocking = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);
        int gateArmed = 1;

        await using var writer = new OrderedViewWriter(publishEvery: 16, publishIntervalMs: 4);

        writer.EnqueueReconcile(logId, reader);

        writer.EnqueueViewRequest(ViewRequests.For(
            new SortContext(ColumnName.Level, false, null, false), s_emptyFilter, [logId], Predicate));

        Assert.True(entered.Wait(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken));

        writer.EnqueueViewRequest(ViewRequests.For(
            new SortContext(ColumnName.Source, true, null, false), s_emptyFilter, [logId]));

        writer.EnqueueClear(ViewRequests.Identity(), ViewRequests.NextSequence());

        blocking.Set();

        OrderedViewSnapshot snapshot = await writer.DrainAsync();

        Assert.Null(writer.Faulted);
        Assert.Equal(0, snapshot.Count);

        Assert.Equal(1, writer.BuildsStarted);
        return;

        bool Predicate(EventLocator locator, IEventColumnReader columnReader)
        {
            if (Interlocked.Exchange(ref gateArmed, 0) == 1)
            {
                entered.Set();
                blocking.Wait(OrderedViewTestTimeouts.Default);
            }

            return true;
        }
    }

    [Fact]
    public async Task DeferredBuild_RetaggedByALaterRequest_AdoptsTheLatestIdentityAndSequence()
    {
        EventLogId logA = EventLogId.Create();
        EventLogId logB = EventLogId.Create();
        var events = MakeEvents(200);
        IEventColumnReader reader = EventColumnStore.Build(events, generation: 0, contentVersion: 0).CreateReader(logA);

        using var blocking = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);
        int gateArmed = 1;

        await using var writer = new OrderedViewWriter(publishEvery: 16, publishIntervalMs: 4);

        ViewRequest initial = ViewRequests.For(
            new SortContext(ColumnName.Level, false, null, false), s_emptyFilter, [logA, logB], Predicate, activeLogId: logA);

        var deferredContext = new SortContext(ColumnName.Source, true, null, false);
        ViewRequest deferred = ViewRequests.For(deferredContext, s_emptyFilter, [logA, logB], activeLogId: logA);

        ViewRequest retag = ViewRequests.For(deferredContext, s_emptyFilter, [logA, logB], activeLogId: logB);

        var matched = new TaskCompletionSource<OrderedViewUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);

        writer.Updated += update =>
        {
            if (update is OrderedViewReady && update.Sequence == retag.Sequence) { matched.TrySetResult(update); }
        };

        writer.EnqueueReconcile(logA, reader);

        writer.EnqueueViewRequest(initial);

        Assert.True(entered.Wait(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken));

        writer.EnqueueViewRequest(deferred);
        writer.EnqueueViewRequest(retag);

        blocking.Set();

        await writer.DrainAsync();

        Assert.Null(writer.Faulted);

        Assert.Equal(2, writer.BuildsStarted);

        OrderedViewUpdate published = await matched.Task.WaitAsync(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken);

        Assert.Equal(retag.Identity, published.Identity);
        Assert.Equal(retag.Sequence, published.Sequence);
        return;

        bool Predicate(EventLocator locator, IEventColumnReader columnReader)
        {
            if (Interlocked.Exchange(ref gateArmed, 0) == 1)
            {
                entered.Set();
                blocking.Wait(OrderedViewTestTimeouts.Default);
            }

            return true;
        }
    }

    [Fact]
    public async Task Dispose_WithABufferedCompletionAndADeferredRequest_NeverHandsOffTheDeferredBuild()
    {
        EventLogId logId = EventLogId.Create();
        var events = MakeEvents(120);
        IEventColumnReader reader = EventColumnStore.Build(events, generation: 0, contentVersion: 0).CreateReader(logId);

        using var buildEntered = new ManualResetEventSlim(false);
        using var releaseBuild = new ManualResetEventSlim(false);
        using var ownerParked = new ManualResetEventSlim(false);
        using var releaseOwner = new ManualResetEventSlim(false);
        int buildGate = 1;
        int ownerGate = 0;

        var writer = new OrderedViewWriter(publishEvery: 8, publishIntervalMs: 0);

        writer.Updated += _ =>
        {
            if (Interlocked.Exchange(ref ownerGate, 0) == 1)
            {
                ownerParked.Set();
                releaseOwner.Wait(OrderedViewTestTimeouts.Default);
            }
        };

        EventColumnStore store = EnqueueGrowingReconciles(writer, logId, EventColumnStore.Empty, events, 0, 64, chunkSize: 64);

        writer.EnqueueViewRequest(ViewRequests.For(
            new SortContext(ColumnName.Level, false, null, false), s_emptyFilter, [logId], Predicate));

        Assert.True(buildEntered.Wait(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken));

        writer.EnqueueViewRequest(ViewRequests.For(
            new SortContext(ColumnName.Source, true, null, false), s_emptyFilter, [logId]));

        Interlocked.Exchange(ref ownerGate, 1);

        EnqueueGrowingReconciles(writer, logId, store, events, 64, events.Count, chunkSize: 1);

        Assert.True(ownerParked.Wait(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken),
            "The owner never parked, so the completion below would not be buffered at disposal.");

        Task? build = writer.CurrentBuildTask;

        Assert.NotNull(build);

        releaseBuild.Set();
        await build.WaitAsync(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken);

        ValueTask disposal = writer.DisposeAsync();

        releaseOwner.Set();

        await disposal;

        Assert.Equal(1, writer.BuildsStarted);
        return;

        bool Predicate(EventLocator locator, IEventColumnReader columnReader)
        {
            if (Interlocked.Exchange(ref buildGate, 0) == 1)
            {
                buildEntered.Set();
                releaseBuild.Wait(OrderedViewTestTimeouts.Default);
            }

            return true;
        }
    }

    [Fact]
    public async Task Dispose_WithARunningBuildAndADeferredRequest_TerminatesWithoutStartingMore()
    {
        EventLogId logId = EventLogId.Create();
        var events = MakeEvents(200);
        IEventColumnReader reader = EventColumnStore.Build(events, generation: 0, contentVersion: 0).CreateReader(logId);

        using var blocking = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);
        int gateArmed = 1;

        var writer = new OrderedViewWriter(publishEvery: 16, publishIntervalMs: 4);

        writer.EnqueueReconcile(logId, reader);

        writer.EnqueueViewRequest(ViewRequests.For(
            new SortContext(ColumnName.Level, false, null, false), s_emptyFilter, [logId], Predicate));

        Assert.True(entered.Wait(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken));

        writer.EnqueueViewRequest(ViewRequests.For(
            new SortContext(ColumnName.Source, true, null, false), s_emptyFilter, [logId]));

        ValueTask disposal = writer.DisposeAsync();

        blocking.Set();

        await disposal;

        Assert.Equal(1, writer.BuildsStarted);
        return;

        bool Predicate(EventLocator locator, IEventColumnReader columnReader)
        {
            if (Interlocked.Exchange(ref gateArmed, 0) == 1)
            {
                entered.Set();
                blocking.Wait(OrderedViewTestTimeouts.Default);
            }

            return true;
        }
    }

    [Fact]
    public async Task Dispose_WithBufferedUnconsumedRequests_NeverStartsOrRetainsBuild()
    {
        EventLogId logId = EventLogId.Create();
        var events = MakeEvents(64);
        IEventColumnReader reader = EventColumnStore.Build(events, generation: 0, contentVersion: 0).CreateReader(logId);

        using var ownerParked = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        int gateArmed = 1;

        var writer = new OrderedViewWriter(publishEvery: 8, publishIntervalMs: 4);

        writer.Updated += _ =>
        {
            if (Interlocked.Exchange(ref gateArmed, 0) == 1)
            {
                ownerParked.Set();
                release.Wait(OrderedViewTestTimeouts.Default);
            }
        };

        EnqueueGrowingReconciles(writer, logId, EventColumnStore.Empty, events, 0, events.Count, chunkSize: 1);

        Assert.True(ownerParked.Wait(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken),
            "The owner never parked, so the commands below would not still be buffered at disposal.");

        writer.EnqueueViewRequest(ViewRequests.For(
            new SortContext(ColumnName.Source, true, null, false), s_emptyFilter, [logId]));

        writer.EnqueueRemoveLog(EventLogId.Create());
        writer.EnqueueResetLog(logId, newGeneration: 1);

        ValueTask disposal = writer.DisposeAsync();

        release.Set();

        await disposal;

        Assert.Equal(0, writer.BuildsStarted);
    }

    [Fact]
    public async Task DrainAsync_WaitsForADeferredBuild_AndDoesNotHang()
    {
        EventLogId logId = EventLogId.Create();
        var events = MakeEvents(200);
        IEventColumnReader reader = EventColumnStore.Build(events, generation: 0, contentVersion: 0).CreateReader(logId);

        using var blocking = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);
        int gateArmed = 1;

        await using var writer = new OrderedViewWriter(publishEvery: 16, publishIntervalMs: 4);

        writer.EnqueueReconcile(logId, reader);

        writer.EnqueueViewRequest(ViewRequests.For(
            new SortContext(ColumnName.Level, false, null, false), s_emptyFilter, [logId], Predicate));

        Assert.True(entered.Wait(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken));

        writer.EnqueueViewRequest(ViewRequests.For(
            new SortContext(ColumnName.EventId, false, null, false), s_emptyFilter, [logId]));

        writer.EnqueueViewRequest(ViewRequests.For(
            new SortContext(ColumnName.DateAndTime, true, null, false), s_emptyFilter, [logId]));

        var finalContext = new SortContext(ColumnName.Source, true, null, false);
        writer.EnqueueViewRequest(ViewRequests.For(finalContext, s_emptyFilter, [logId]));

        Task<OrderedViewSnapshot> drain = writer.DrainAsync();

        blocking.Set();

        OrderedViewSnapshot snapshot = await drain.WaitAsync(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken);

        Assert.Null(writer.Faulted);
        Assert.Equal(2, writer.BuildsStarted);

        CombinedViewParityAsserts.AssertSnapshotOrderMatchesReference(snapshot, logId, events, finalContext);
        return;

        bool Predicate(EventLocator locator, IEventColumnReader columnReader)
        {
            if (Interlocked.Exchange(ref gateArmed, 0) == 1)
            {
                entered.Set();
                blocking.Wait(OrderedViewTestTimeouts.Default);
            }

            return true;
        }
    }

    [Fact]
    public async Task RemoveLog_WithADeferredRequest_ReplacesItAndDropsTheClosedLogsRows()
    {
        EventLogId logA = EventLogId.Create();
        EventLogId logB = EventLogId.Create();
        var events = MakeEvents(120);
        IEventColumnReader readerA = EventColumnStore.Build(events, generation: 0, contentVersion: 0).CreateReader(logA);
        IEventColumnReader readerB = EventColumnStore.Build(events, generation: 0, contentVersion: 0).CreateReader(logB);

        using var blocking = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);
        int gateArmed = 1;

        await using var writer = new OrderedViewWriter(publishEvery: 16, publishIntervalMs: 4);

        writer.EnqueueReconcile(logA, readerA);
        writer.EnqueueReconcile(logB, readerB);

        writer.EnqueueViewRequest(ViewRequests.For(
            new SortContext(ColumnName.Level, false, null, false), s_emptyFilter, [logA, logB], Predicate));

        Assert.True(entered.Wait(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken));

        writer.EnqueueViewRequest(ViewRequests.For(
            new SortContext(ColumnName.Source, true, null, false), s_emptyFilter, [logA, logB]));

        writer.EnqueueRemoveLog(logB);

        blocking.Set();

        OrderedViewSnapshot snapshot = await writer.DrainAsync();

        Assert.Null(writer.Faulted);
        Assert.Equal(2, writer.BuildsStarted);

        Assert.Equal(events.Count, snapshot.Count);

        for (int i = 0; i < snapshot.Count; i++) { Assert.Equal(logA, snapshot.At(i).Locator.LogId); }

        return;

        bool Predicate(EventLocator locator, IEventColumnReader columnReader)
        {
            if (Interlocked.Exchange(ref gateArmed, 0) == 1)
            {
                entered.Set();
                blocking.Wait(OrderedViewTestTimeouts.Default);
            }

            return true;
        }
    }

    [Fact]
    public async Task ScopeSwitchStorm_BoundsRunningAndQueuedBuilds_WhileAdoptingLatest()
    {
        EventLogId logId = EventLogId.Create();
        var events = MakeEvents(200);
        IEventColumnReader reader = EventColumnStore.Build(events, generation: 0, contentVersion: 0).CreateReader(logId);

        using var blocking = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);
        int gateArmed = 1;

        await using var writer = new OrderedViewWriter(publishEvery: 16, publishIntervalMs: 4);

        writer.EnqueueReconcile(logId, reader);

        writer.EnqueueViewRequest(ViewRequests.For(
            new SortContext(ColumnName.Level, false, null, false), s_emptyFilter, [logId], Predicate));

        Assert.True(entered.Wait(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken),
            "The first build never started, so the storm below would not exercise the bound.");

        ColumnName[] storm = [ColumnName.EventId, ColumnName.Source, ColumnName.DateAndTime, ColumnName.Level, ColumnName.Source];
        var finalContext = new SortContext(ColumnName.Source, true, null, false);

        for (int i = 0; i < storm.Length - 1; i++)
        {
            writer.EnqueueViewRequest(ViewRequests.For(new SortContext(storm[i], i % 2 == 0, null, false), s_emptyFilter, [logId]));
        }

        writer.EnqueueViewRequest(ViewRequests.For(finalContext, s_emptyFilter, [logId]));

        blocking.Set();

        OrderedViewSnapshot snapshot = await writer.DrainAsync();

        Assert.Null(writer.Faulted);

        Assert.Equal(2, writer.BuildsStarted);
        CombinedViewParityAsserts.AssertSnapshotOrderMatchesReference(snapshot, logId, events, finalContext);
        return;

        bool Predicate(EventLocator locator, IEventColumnReader columnReader)
        {
            if (Interlocked.Exchange(ref gateArmed, 0) == 1)
            {
                entered.Set();
                blocking.Wait(OrderedViewTestTimeouts.Default);
            }

            return true;
        }
    }

    [Fact]
    public async Task UncancelledOperationCanceledException_IsSurfacedAsAFault()
    {
        EventLogId logId = EventLogId.Create();
        var events = MakeEvents(50);
        IEventColumnReader reader = EventColumnStore.Build(events, generation: 0, contentVersion: 0).CreateReader(logId);

        await using var writer = new OrderedViewWriter(publishEvery: 16, publishIntervalMs: 4);

        writer.EnqueueReconcile(logId, reader);

        writer.EnqueueViewRequest(ViewRequests.For(
            new SortContext(ColumnName.Level, false, null, false),
            s_emptyFilter,
            [logId],
            static (_, _) => throw new OperationCanceledException("predicate threw OCE without any cancellation")));

        await writer.DrainAsync();

        Assert.NotNull(writer.Faulted);
        Assert.IsType<OperationCanceledException>(writer.Faulted);
    }

    private static EventColumnStore EnqueueGrowingReconciles(
        OrderedViewWriter writer,
        EventLogId logId,
        EventColumnStore store,
        List<ResolvedEvent> events,
        int fromIndex,
        int toIndex,
        int chunkSize)
    {
        for (int from = fromIndex; from < toIndex; from += chunkSize)
        {
            store = store.Append(events.GetRange(from, Math.Min(chunkSize, toIndex - from)));
            writer.EnqueueReconcile(logId, store.CreateReader(logId));
        }

        return store;
    }

    private static List<ResolvedEvent> MakeEvents(int count)
    {
        var rng = new Random(20260728);
        var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        string[] sources = ["Provider.A", "Provider.B", "Provider.C", "Provider.D"];
        string[] levels = ["Information", "Warning", "Error", "Critical"];
        var events = new List<ResolvedEvent>(count);

        for (int i = 0; i < count; i++)
        {
            clock = clock.AddMilliseconds(1 + rng.Next(10));

            events.Add(new ResolvedEvent("Log", LogPathType.Channel)
            {
                RecordId = i + 1,
                TimeCreated = clock,
                Id = 1000 + rng.Next(5),
                Level = levels[rng.Next(levels.Length)],
                Source = sources[rng.Next(sources.Length)],
                LogName = "Channel"
            });
        }

        return events;
    }
}
