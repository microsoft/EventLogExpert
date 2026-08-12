// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using EventLogExpert.Runtime.Tests.LogTable.TestSupport;

namespace EventLogExpert.Runtime.Tests.LogTable.OrderedView;

public sealed class ViewRequestProtocolTests
{
    private static readonly Filter s_emptyFilter = new(null, []);

    [Fact]
    public async Task Clear_CancelsAndReleasesInFlightScopeBuild()
    {
        const int SampleSize = 40_000;
        const int MaxPollAttempts = 300;
        const int PollDelayMilliseconds = 10;

        EventLogId logId = EventLogId.Create();
        IEventColumnReader reader = LargeReader(logId, SampleSize);

        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        int examined = 0;
        int gateArmed = 1;

        await using var writer = new OrderedViewWriter(publishEvery: 5000, publishIntervalMs: 0);

        writer.EnqueueReconcile(logId, reader);
        writer.EnqueueViewRequest(ViewRequests.For(
            new SortContext(ColumnName.RecordId, false, null, false), s_emptyFilter, [logId], Predicate));

        Assert.True(entered.Wait(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken));

        writer.EnqueueClear(ViewRequests.Identity(), ViewRequests.NextSequence());

        bool cleared = false;
        for (int attempt = 0; attempt < MaxPollAttempts && !cleared; attempt++)
        {
            if (writer.Current.Count == 0 && writer.Current.Version > 0) { cleared = true; }
            else { await Task.Delay(PollDelayMilliseconds, TestContext.Current.CancellationToken); }
        }

        Assert.True(cleared, "the clear was never consumed, so the cancel below would be untested");

        release.Set();

        OrderedViewSnapshot snapshot =
            await writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken);

        Assert.Null(writer.Faulted);
        Assert.Equal(0, snapshot.Count);
        Assert.True(Volatile.Read(ref examined) < SampleSize,
            $"the build ran to completion after the clear instead of being cancelled: {examined} of {SampleSize} rows");

        Assert.Null(writer.CurrentBuildTask);
        return;

        bool Predicate(EventLocator locator, IEventColumnReader columnReader)
        {
            Interlocked.Increment(ref examined);

            if (Interlocked.Exchange(ref gateArmed, 0) == 1)
            {
                entered.Set();
                release.Wait(OrderedViewTestTimeouts.Default);
            }

            return true;
        }
    }

    [Fact]
    public async Task Clear_ReportsNoAnswerRatherThanAnEmptyOne()
    {
        var sample = new OrderedViewSample(seed: 712, logCount: 1);
        sample.SeedInterleaved(24);
        EventLogId logId = sample.LogId(0);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await using var writer = new OrderedViewWriter(publishIntervalMs: 0);
        var captured = new List<OrderedViewUpdate>();
        Lock gate = new();

        writer.Updated += update =>
        {
            lock (gate) { captured.Add(update); }
        };

        writer.EnqueueViewRequest(RequestFor(logId, ReaderOver(sample, logId, 24)));
        await DrainTwiceAsync(writer, cancellationToken);

        lock (gate) { Assert.IsType<OrderedViewReady>(captured[^1]); }

        writer.EnqueueClear(ViewRequests.Identity(), ViewRequests.NextSequence());
        await DrainTwiceAsync(writer, cancellationToken);

        lock (gate) { Assert.IsType<OrderedViewCleared>(captured[^1]); }

        Assert.Null(writer.Faulted);
    }

    [Fact]
    public async Task ContentCommands_NeverPromoteTheIdentity_OnlyThePublicationVersion()
    {
        var sample = new OrderedViewSample(seed: 701, logCount: 1);
        sample.SeedInterleaved(60);
        EventLogId logId = sample.LogId(0);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await using var writer = new OrderedViewWriter(publishIntervalMs: 0);
        var captured = new List<OrderedViewUpdate>();
        Lock gate = new();

        writer.Updated += update =>
        {
            lock (gate) { captured.Add(update); }
        };

        IEventColumnReader partial = ReaderOver(sample, logId, 30);
        ViewRequest request = RequestFor(logId, partial);

        writer.EnqueueViewRequest(request);
        await DrainTwiceAsync(writer, cancellationToken);

        long afterAdopt = LatestVersion(captured, gate);

        writer.EnqueueReconcile(logId, ReaderOver(sample, logId, 60));
        await DrainTwiceAsync(writer, cancellationToken);

        OrderedViewUpdate[] after;

        lock (gate) { after = [.. captured.Where(update => update.SnapshotVersion > afterAdopt)]; }

        Assert.NotEmpty(after);
        Assert.All(after, update => Assert.Equal(request.Identity, update.Identity));
    }

    [Fact]
    public async Task DelayedLowerSequenceRequest_IsDroppedByTheHighWatermark()
    {
        var sample = new OrderedViewSample(seed: 702, logCount: 1);
        sample.SeedInterleaved(40);
        EventLogId logId = sample.LogId(0);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await using var writer = new OrderedViewWriter(publishIntervalMs: 0);
        var captured = new List<OrderedViewUpdate>();
        Lock gate = new();

        writer.Updated += update =>
        {
            lock (gate) { captured.Add(update); }
        };

        IEventColumnReader reader = ReaderOver(sample, logId, 40);
        ViewRequest current = RequestFor(logId, reader, ColumnName.Source, sequence: 100);

        writer.EnqueueViewRequest(current);
        await DrainTwiceAsync(writer, cancellationToken);

        ViewRequest straggler = RequestFor(logId, reader, ColumnName.Level, sequence: 99);

        writer.EnqueueViewRequest(straggler);
        await DrainTwiceAsync(writer, cancellationToken);

        lock (gate)
        {
            Assert.DoesNotContain(captured, update => update.Identity == straggler.Identity);
            Assert.Contains(captured, update => update.Identity == current.Identity);
        }
    }

    [Fact]
    public void Key_DifferentScope_DiffersEvenWhenEverythingElseMatches()
    {
        EventLogId logA = EventLogId.Create(), logB = EventLogId.Create();

        Assert.NotEqual(ViewRequests.Identity([logA]), ViewRequests.Identity([logA, logB]));
    }

    [Fact]
    public void Key_IsRecomputedForEveryWithProducedState_NotCopiedFromThePredecessor()
    {
        EventLogId logId = EventLogId.Create();

        var state = new LogTableState { ActiveEventLogId = logId, EventTables = [new LogView(logId)] };

        ViewIdentity before = state.ViewIdentity;

        LogTableState resorted = state with { RequestedOrderBy = ColumnName.Level };

        Assert.NotEqual(before, resorted.ViewIdentity);
        Assert.Equal(before, state.ViewIdentity);
    }

    [Fact]
    public void Key_OrderingsThatNormalizeToTheSameSortContext_AreStillDifferentIdentities()
    {
        var normalized = new SortContext(null, false, ColumnName.Source, false);
        var explicitly = new SortContext(ColumnName.DateAndTime, false, ColumnName.Source, false);

        Assert.Equal(normalized, explicitly);
        Assert.NotEqual(
            ViewRequests.Identity(orderBy: null, groupBy: ColumnName.Source),
            ViewRequests.Identity(orderBy: ColumnName.DateAndTime, groupBy: ColumnName.Source));
    }

    [Fact]
    public void Key_ScopeOrderIsCanonical_SoEnumerationOrderCannotChangeIdentity()
    {
        EventLogId logA = EventLogId.Create(), logB = EventLogId.Create();

        Assert.Equal(ViewRequests.Identity([logA, logB]), ViewRequests.Identity([logB, logA]));
    }

    [Fact]
    public void Key_SemanticallyEqualFiltersBuiltApart_AreTheSameIdentity()
    {
        Assert.Equal(ViewRequests.Identity(filter: LevelErrorFilter()), ViewRequests.Identity(filter: LevelErrorFilter()));
        Assert.NotEqual(ViewRequests.Identity(filter: LevelErrorFilter()), ViewRequests.Identity(filter: s_emptyFilter));
    }

    [Fact]
    public void Key_TwoIndependentlyBuiltEqualStates_ProduceEqualKeys()
    {
        EventLogId logA = EventLogId.Create(), logB = EventLogId.Create();

        ViewIdentity left = ViewRequests.Identity([logA, logB], logA, ColumnName.Source, true, filter: LevelErrorFilter());
        ViewIdentity right = ViewRequests.Identity([logA, logB], logA, ColumnName.Source, true, filter: LevelErrorFilter());

        Assert.NotSame(left, right);
        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void Key_UngroupedTabAndAOneMemberGroupOverTheSameLog_AreDifferentIdentities()
    {
        EventLogId logId = EventLogId.Create();
        var groupId = LogTabGroupId.Create();
        var header = new LogView(EventLogId.Create()) { GroupId = groupId };

        var ungrouped = new LogTableState { ActiveEventLogId = logId, EventTables = [new LogView(logId)] };

        var grouped = new LogTableState
        {
            ActiveEventLogId = header.Id,
            EventTables = [header, new LogView(logId)],
            Groups = [new LogTabGroup(groupId, "Group", [logId])]
        };

        Assert.NotEqual(ungrouped.ViewIdentity, grouped.ViewIdentity);
        Assert.True(ungrouped.ViewIdentity.CoversSameViewAs(grouped.ViewIdentity));
    }

    [Fact]
    public async Task ReissuingTheAdoptedCoverageUnderANewIdentity_RepublishesTheSameRows()
    {
        var sample = new OrderedViewSample(seed: 703, logCount: 1);
        sample.SeedInterleaved(50);
        EventLogId logId = sample.LogId(0);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await using var writer = new OrderedViewWriter(publishIntervalMs: 0);
        var captured = new List<OrderedViewUpdate>();
        Lock gate = new();

        writer.Updated += update =>
        {
            lock (gate) { captured.Add(update); }
        };

        IEventColumnReader reader = ReaderOver(sample, logId, 50);
        ViewRequest first = RequestFor(logId, reader);

        writer.EnqueueViewRequest(first);
        await DrainTwiceAsync(writer, cancellationToken);

        int rows = writer.Current.Count;
        long versionAfterFirst = LatestVersion(captured, gate);

        ViewRequest again = RequestFor(logId, reader, activeLogId: EventLogId.Create());

        Assert.True(again.Identity.CoversSameViewAs(first.Identity));
        Assert.NotEqual(first.Identity, again.Identity);

        writer.EnqueueViewRequest(again);
        await DrainTwiceAsync(writer, cancellationToken);

        OrderedViewReady[] restamped;

        lock (gate)
        {
            restamped = [.. captured.OfType<OrderedViewReady>().Where(update => update.Identity == again.Identity)];
        }

        Assert.NotEmpty(restamped);
        Assert.Equal(rows, writer.Current.Count);
        Assert.True(restamped[^1].SnapshotVersion > versionAfterFirst);
    }

    [Fact]
    public async Task ReloadedToEmptyLog_ReportsTheSameFinalEmptyAnswerAsAnInitiallyEmptyLog()
    {
        var sample = new OrderedViewSample(seed: 710, logCount: 1);
        sample.SeedInterleaved(40);
        EventLogId logId = sample.LogId(0);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await using var writer = new OrderedViewWriter(publishIntervalMs: 0);
        var captured = new List<OrderedViewUpdate>();
        Lock gate = new();

        writer.Updated += update =>
        {
            lock (gate) { captured.Add(update); }
        };

        writer.EnqueueViewRequest(RequestFor(logId, ReaderOver(sample, logId, 40)));
        await DrainTwiceAsync(writer, cancellationToken);

        lock (gate) { Assert.IsType<OrderedViewReady>(captured[^1]); }

        writer.EnqueueViewRequest(RequestFor(logId, GenerationReaderOver(sample, logId, 0, generation: 1)));
        await DrainTwiceAsync(writer, cancellationToken);

        lock (gate)
        {
            var ready = Assert.IsType<OrderedViewReady>(captured[^1]);
            Assert.Equal(0, ready.View.Count);
            Assert.Empty(ready.InScope);
        }

        Assert.Null(writer.Faulted);
    }

    [Fact]
    public void Restamp_IsRefusedWhileACarriedReaderHasUnreconciledRows()
    {
        var sample = new OrderedViewSample(seed: 706, logCount: 1);
        sample.SeedInterleaved(60);
        EventLogId logId = sample.LogId(0);

        var state = new OrderedViewState();

        Assert.True(state.TrySetActiveScope([logId], 1));
        state.ReconcileLog(logId, ReaderOver(sample, logId, 30));

        RebuildRequest request = state.BeginRebuild(static (_, _) => true, new SortContext(ColumnName.RecordId, false, null, false));
        Assert.True(state.TryAdoptRebuild(request, OrderedViewState.BuildIndex(request, CancellationToken.None)));

        Assert.True(state.CanRestampAdopted([logId], new Dictionary<EventLogId, IEventColumnReader>
        {
            [logId] = ReaderOver(sample, logId, 30)
        }));

        Assert.False(state.CanRestampAdopted([logId], new Dictionary<EventLogId, IEventColumnReader>
        {
            [logId] = ReaderOver(sample, logId, 60)
        }));
    }

    [Fact]
    public void Restamp_IsRefusedWhileATransitionHoldIsInForce()
    {
        var sample = new OrderedViewSample(seed: 1000, logCount: 1);
        sample.SeedInterleaved(40);
        EventLogId logId = sample.LogId(0);

        var state = new OrderedViewState();

        Assert.True(state.TrySetActiveScope([logId], 1));
        state.ReconcileLog(logId, ReaderOver(sample, logId, 30));

        RebuildRequest adoptedRequest = state.BeginRebuild(
            static (_, _) => true, new SortContext(ColumnName.RecordId, false, null, false));

        Assert.True(state.TryAdoptRebuild(adoptedRequest, OrderedViewState.BuildIndex(adoptedRequest, CancellationToken.None)));

        var readers = new Dictionary<EventLogId, IEventColumnReader> { [logId] = ReaderOver(sample, logId, 30) };

        Assert.True(state.CanRestampAdopted([logId], readers));

        state.BeginRebuild(static (_, _) => true, new SortContext(ColumnName.Level, false, null, false), hold: true);

        Assert.False(state.CanRestampAdopted([logId], readers));
    }

    [Fact]
    public void Restamp_IsRefusedWhileTheLiveScopeHasMovedOn()
    {
        var sample = new OrderedViewSample(seed: 705, logCount: 2);
        sample.SeedInterleaved(40);
        EventLogId logA = sample.LogId(0), logB = sample.LogId(1);

        var state = new OrderedViewState();
        IEventColumnReader readerA = sample.Reader(0);

        Assert.True(state.TrySetActiveScope([logA], 1));

        state.ReconcileLog(logA, readerA);

        RebuildRequest request = state.BeginRebuild(static (_, _) => true, new SortContext(ColumnName.RecordId, false, null, false));
        Assert.True(state.TryAdoptRebuild(request, OrderedViewState.BuildIndex(request, CancellationToken.None)));

        var readers = new Dictionary<EventLogId, IEventColumnReader> { [logA] = readerA };

        Assert.True(state.CanRestampAdopted([logA], readers));

        Assert.True(state.TrySetActiveScope([logB], 2));
        Assert.False(state.CanRestampAdopted([logA], readers));
    }

    [Fact]
    public void Restamp_RestoresTheRequestedConfig_SoALaterLifecycleRebuildCannotInheritTheAbandonedOne()
    {
        var sample = new OrderedViewSample(seed: 704, logCount: 2);
        sample.SeedInterleaved(40);
        EventLogId logA = sample.LogId(0), logB = sample.LogId(1);

        var state = new OrderedViewState();
        IEventColumnReader readerA = sample.Reader(0);

        state.ReconcileLog(logA, readerA);

        Assert.True(state.TrySetActiveScope([logA, logB], 1));

        var adopted = new SortContext(ColumnName.RecordId, false, null, false);
        RebuildRequest adoptedRequest = state.BeginRebuild(static (_, _) => true, adopted);
        Assert.True(state.TryAdoptRebuild(adoptedRequest, OrderedViewState.BuildIndex(adoptedRequest, CancellationToken.None)));

        var abandoned = new SortContext(ColumnName.Level, true, null, false);
        state.BeginRebuild(static (_, _) => true, abandoned);
        state.SupersedeInFlight();

        state.RestoreRequestedFromAdopted();

        RebuildRequest afterClose = state.RemoveLog(logB);

        Assert.Equal(adopted, afterClose.Context);
    }

    [Fact]
    public async Task Retag_AfterAHigherGenerationReconcile_SupersedesInsteadOfRidingTheStaleBuild()
    {
        var sample = new OrderedViewSample(seed: 712, logCount: 1);
        sample.SeedInterleaved(60);
        EventLogId logId = sample.LogId(0);

        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        int gateArmed = 1;

        bool Predicate(EventLocator locator, IEventColumnReader reader)
        {
            if (Interlocked.Exchange(ref gateArmed, 0) == 1)
            {
                entered.Set();
                release.Wait(OrderedViewTestTimeouts.Default);
            }

            return true;
        }

        await using var writer = new OrderedViewWriter(publishIntervalMs: 0);

        var context = new SortContext(ColumnName.RecordId, false, null, false);

        writer.EnqueueViewRequest(ViewRequests.For(
            context, s_emptyFilter, [logId], Predicate,
            readers: new Dictionary<EventLogId, IEventColumnReader> { [logId] = ReaderOver(sample, logId, 60) },
            activeLogId: logId));

        Assert.True(entered.Wait(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken));

        writer.EnqueueReconcile(logId, GenerationReaderOver(sample, logId, 25, generation: 1));

        writer.EnqueueViewRequest(ViewRequests.For(
            context, s_emptyFilter, [logId],
            readers: new Dictionary<EventLogId, IEventColumnReader> { [logId] = GenerationReaderOver(sample, logId, 25, generation: 1) },
            activeLogId: logId));

        release.Set();

        OrderedViewSnapshot snapshot = await DrainUntilStableAsync(writer, TestContext.Current.CancellationToken);

        Assert.Null(writer.Faulted);

        Assert.Equal(25, snapshot.Count);

        for (int i = 0; i < snapshot.Count; i++)
        {
            EventLocator locator = snapshot.At(i).Locator;

            Assert.Equal(1, locator.Generation);
            Assert.True(snapshot.TryGetReader(locator, out _));
        }
    }

    [Fact]
    public async Task Retag_HigherGenerationReader_SupersedesInsteadOfMixingGenerations()
    {
        var sample = new OrderedViewSample(seed: 708, logCount: 1);
        sample.SeedInterleaved(60);
        EventLogId logId = sample.LogId(0);

        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        int gateArmed = 1;

        bool Predicate(EventLocator locator, IEventColumnReader reader)
        {
            if (Interlocked.Exchange(ref gateArmed, 0) == 1)
            {
                entered.Set();
                release.Wait(OrderedViewTestTimeouts.Default);
            }

            return true;
        }

        await using var writer = new OrderedViewWriter(publishIntervalMs: 0);

        var context = new SortContext(ColumnName.RecordId, false, null, false);
        IEventColumnReader generation0 = ReaderOver(sample, logId, 60);
        IEventColumnReader generation1 = GenerationReaderOver(sample, logId, 25, generation: 1);

        writer.EnqueueViewRequest(ViewRequests.For(
            context, s_emptyFilter, [logId], Predicate,
            readers: new Dictionary<EventLogId, IEventColumnReader> { [logId] = generation0 },
            activeLogId: logId));

        Assert.True(entered.Wait(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken));

        writer.EnqueueViewRequest(ViewRequests.For(
            context, s_emptyFilter, [logId],
            readers: new Dictionary<EventLogId, IEventColumnReader> { [logId] = generation1 },
            activeLogId: logId));

        release.Set();

        OrderedViewSnapshot snapshot = await DrainUntilStableAsync(writer, TestContext.Current.CancellationToken);

        Assert.Null(writer.Faulted);

        Assert.Equal(25, snapshot.Count);
        for (int i = 0; i < snapshot.Count; i++) { Assert.Equal(1, snapshot.At(i).Locator.Generation); }
    }

    [Fact]
    public async Task Retag_NewerReaderMap_IncludesItsSuffixAtAdoption()
    {
        var sample = new OrderedViewSample(seed: 709, logCount: 1);
        sample.SeedInterleaved(80);
        EventLogId logId = sample.LogId(0);

        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        int gateArmed = 1;

        bool Predicate(EventLocator locator, IEventColumnReader reader)
        {
            if (Interlocked.Exchange(ref gateArmed, 0) == 1)
            {
                entered.Set();
                release.Wait(OrderedViewTestTimeouts.Default);
            }

            return true;
        }

        await using var writer = new OrderedViewWriter(publishIntervalMs: 0);

        var context = new SortContext(ColumnName.RecordId, false, null, false);

        writer.EnqueueViewRequest(ViewRequests.For(
            context, s_emptyFilter, [logId], Predicate,
            readers: new Dictionary<EventLogId, IEventColumnReader> { [logId] = ReaderOver(sample, logId, 30) },
            activeLogId: logId));

        Assert.True(entered.Wait(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken));

        writer.EnqueueViewRequest(ViewRequests.For(
            context, s_emptyFilter, [logId],
            readers: new Dictionary<EventLogId, IEventColumnReader> { [logId] = ReaderOver(sample, logId, 80) },
            activeLogId: null));

        release.Set();

        OrderedViewSnapshot snapshot = await DrainUntilStableAsync(writer, TestContext.Current.CancellationToken);

        Assert.Null(writer.Faulted);
        Assert.Equal(80, snapshot.Count);
    }

    [Fact]
    public async Task SingleLogZeroSurvivor_RoutesAsReady()
    {
        var sample = new OrderedViewSample(seed: 711, logCount: 1);
        sample.SeedInterleaved(40);
        EventLogId logId = sample.LogId(0);

        await using var writer = new OrderedViewWriter(publishIntervalMs: 0);
        var captured = new List<OrderedViewUpdate>();
        Lock gate = new();

        writer.Updated += update =>
        {
            lock (gate) { captured.Add(update); }
        };

        writer.EnqueueViewRequest(ViewRequests.For(
            new SortContext(ColumnName.RecordId, false, null, false),
            s_emptyFilter,
            [logId],
            static (_, _) => false,
            readers: new Dictionary<EventLogId, IEventColumnReader> { [logId] = ReaderOver(sample, logId, 40) },
            activeLogId: logId));

        await DrainTwiceAsync(writer, TestContext.Current.CancellationToken);

        lock (gate)
        {
            var ready = Assert.IsType<OrderedViewReady>(captured[^1]);
            Assert.Equal(0, ready.View.Count);
        }

        Assert.Null(writer.Faulted);
    }

    private static async Task DrainTwiceAsync(OrderedViewWriter writer, CancellationToken cancellationToken)
    {
        await writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default, cancellationToken);
        await writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default, cancellationToken);
    }

    private static async Task<OrderedViewSnapshot> DrainUntilStableAsync(OrderedViewWriter writer, CancellationToken cancellationToken)
    {
        await writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default, cancellationToken);
        await writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default, cancellationToken);

        return await writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default, cancellationToken);
    }

    private static IEventColumnReader GenerationReaderOver(OrderedViewSample sample, EventLogId logId, int count, int generation) =>
        EventColumnStore.Build([.. sample.Events(0).Take(count)], generation, generation).CreateReader(logId);

    private static IEventColumnReader LargeReader(EventLogId logId, int count)
    {
        var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var events = new List<ResolvedEvent>(count);

        for (int i = 0; i < count; i++)
        {
            events.Add(new ResolvedEvent("Log", LogPathType.Channel)
            {
                RecordId = i,
                TimeCreated = clock.AddMilliseconds(i),
                Id = 1000,
                Level = "Information",
                Source = "Provider.A",
                LogName = "Channel"
            });
        }

        return EventColumnStore.Build(events, generation: 0, contentVersion: 0).CreateReader(logId);
    }

    private static long LatestVersion(List<OrderedViewUpdate> captured, Lock gate)
    {
        lock (gate) { return captured.Count == 0 ? -1 : captured[^1].SnapshotVersion; }
    }

    private static Filter LevelErrorFilter()
    {
        SavedFilter levelError = SavedFilter.TryCreate("Level == \"Error\"") ??
            throw new InvalidOperationException("Level filter failed to compile.");

        return new Filter(null, [levelError]);
    }

    private static IEventColumnReader ReaderOver(OrderedViewSample sample, EventLogId logId, int count) =>
        EventColumnStore.Build([.. sample.Events(0).Take(count)], 0, 0).CreateReader(logId);

    private static ViewRequest RequestFor(
        EventLogId logId,
        IEventColumnReader reader,
        ColumnName orderBy = ColumnName.RecordId,
        long? sequence = null,
        EventLogId? activeLogId = null) =>
        ViewRequests.For(
            new SortContext(orderBy, false, null, false),
            s_emptyFilter,
            [logId],
            readers: new Dictionary<EventLogId, IEventColumnReader> { [logId] = reader },
            sequence: sequence,
            activeLogId: activeLogId ?? logId);
}
