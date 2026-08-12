// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;

namespace EventLogExpert.Runtime.Tests.LogTable.OrderedView;

public sealed class OrderedViewScopeBuildTests
{
    private static readonly SortContext s_context = new(ColumnName.RecordId, false, null, false);

    [Fact]
    public void A_B_A_WithTailsForBothLogs_ConvergesExactlyOnce()
    {
        EventLogId logA = EventLogId.Create();
        EventLogId logB = EventLogId.Create();
        var state = new OrderedViewState();

        AdoptScope(state, [logA], new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 30, 0, 0) });
        state.ReconcileLog(logA, Reader(logA, 45, 0, 0));

        AdoptScope(state, [logB], new Dictionary<EventLogId, IEventColumnReader> { [logB] = Reader(logB, 20, 0, 1000) });
        state.ReconcileLog(logB, Reader(logB, 26, 0, 1000));

        AdoptScope(state, [logA], new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 45, 0, 0) });

        Assert.Equal(45, state.Current.Count);
        AssertNoDuplicateLocators(state.Current);
        Assert.Equal(45, state.RowCount);
    }

    [Fact]
    public void A_B_EvictTo_A_WithNoNewAEvents_ShowsARows()
    {
        EventLogId logA = EventLogId.Create();
        EventLogId logB = EventLogId.Create();
        IEventColumnReader readerA = Reader(logA, 24, 0, 0);
        var state = new OrderedViewState();

        AdoptScope(state, [logA], new Dictionary<EventLogId, IEventColumnReader> { [logA] = readerA });
        Assert.Equal(24, state.Current.Count);

        AdoptScope(state, [logB], new Dictionary<EventLogId, IEventColumnReader> { [logB] = Reader(logB, 9, 0, 1000) });
        Assert.Equal(9, state.Current.Count);

        AdoptScope(state, [logA], new Dictionary<EventLogId, IEventColumnReader> { [logA] = readerA });

        Assert.Equal(24, state.Current.Count);
        for (int index = 0; index < 24; index++) { Assert.True(state.Current.Contains(logA, 0, index)); }
    }

    [Fact]
    public void Adopt_DoesNotRegressLiveCoverage_SoNoLocatorIsInsertedTwice()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        state.ReconcileLog(logId, Reader(logId, 50, 0, 0));

        RebuildRequest request = state.BeginRebuild(static (_, _) => true, s_context);

        state.ReconcileLog(logId, Reader(logId, 80, 0, 0));

        Assert.True(state.TryAdoptRebuild(request, OrderedViewState.BuildIndex(request, CancellationToken.None)));

        Assert.Equal(80, state.Current.Count);
        Assert.Equal(80, state.RowCount);
        AssertNoDuplicateLocators(state.Current);
    }

    [Fact]
    public void AfterEviction_StaleLowerGenerationReader_IsStillRejected()
    {
        EventLogId logA = EventLogId.Create();
        EventLogId logB = EventLogId.Create();
        var state = new OrderedViewState();

        AdoptScope(state,
            [logA, logB],
            new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 10, 0, 0), [logB] = Reader(logB, 8, 0, 1000) });

        state.ReconcileLog(logB, Reader(logB, 6, 2, 2000));
        RebuildRequest reset = state.BeginReset(logB, newGeneration: 2);
        Assert.True(state.TryAdoptRebuild(reset, OrderedViewState.BuildIndex(reset, CancellationToken.None)));
        Assert.True(state.Current.Contains(logB, 2, 0));

        AdoptScope(state, [logA], new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 10, 0, 0) });

        AdoptScope(state,
            [logA, logB],
            new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 10, 0, 0), [logB] = Reader(logB, 7, 1, 3000) });

        Assert.Equal(10, state.Current.Count);
        Assert.False(state.Current.Contains(logB, 1, 0));
        Assert.DoesNotContain(new LogGeneration(logB, 1), state.AdoptedInScope);
    }

    [Fact]
    public void AfterEviction_StaleLowerGenerationThatOnlyEverHadAZeroRowGeneration_IsStillRejected()
    {
        EventLogId logA = EventLogId.Create();
        EventLogId logB = EventLogId.Create();
        var state = new OrderedViewState();

        AdoptScope(state,
            [logA, logB],
            new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 6, 0, 0), [logB] = Reader(logB, 8, 2, 1000) });

        AdoptScope(state, [logA], new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 6, 0, 0) });

        AdoptScope(state,
            [logA, logB],
            new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 6, 0, 0), [logB] = Reader(logB, 0, 5, 3000) });

        Assert.Equal(6, state.Current.Count);

        AdoptScope(state, [logA], new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 6, 0, 0) });

        AdoptScope(state,
            [logA, logB],
            new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 6, 0, 0), [logB] = Reader(logB, 7, 3, 4000) });

        Assert.Equal(6, state.Current.Count);
        Assert.False(state.Current.Contains(logB, 3, 0));
        Assert.DoesNotContain(new LogGeneration(logB, 3), state.AdoptedInScope);
    }

    [Fact]
    public void AfterEviction_StaleLowerGeneration_IsRejected_EvenWhenTheHigherGenerationWasOnlyEverAPinnedReader()
    {
        EventLogId logA = EventLogId.Create();
        EventLogId logB = EventLogId.Create();
        var state = new OrderedViewState();

        AdoptScope(state,
            [logA, logB],
            new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 6, 0, 0), [logB] = Reader(logB, 8, 0, 1000) });

        Assert.False(state.ReconcileLog(logB, Reader(logB, 0, 5, 3000)));

        AdoptScope(state, [logA], new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 6, 0, 0) });

        AdoptScope(state,
            [logA, logB],
            new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 6, 0, 0), [logB] = Reader(logB, 7, 3, 4000) });

        Assert.Equal(6, state.Current.Count);
        Assert.False(state.Current.Contains(logB, 3, 0));
        Assert.DoesNotContain(new LogGeneration(logB, 3), state.AdoptedInScope);
    }

    [Fact]
    public void AfterEviction_StaleLowerGeneration_IsRejected_WhenTheHigherGenerationNeverHadAReaderAtAll()
    {
        EventLogId logA = EventLogId.Create();
        EventLogId logB = EventLogId.Create();
        var state = new OrderedViewState();

        AdoptScope(state,
            [logA, logB],
            new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 6, 0, 0), [logB] = Reader(logB, 8, 0, 1000) });

        RebuildRequest reset = state.BeginReset(logB, newGeneration: 5);
        Assert.True(state.TryAdoptRebuild(reset, OrderedViewState.BuildIndex(reset, CancellationToken.None)));

        Assert.Equal(6, state.Current.Count);

        AdoptScope(state, [logA], new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 6, 0, 0) });

        AdoptScope(state,
            [logA, logB],
            new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 6, 0, 0), [logB] = Reader(logB, 7, 3, 4000) });

        Assert.Equal(6, state.Current.Count);
        Assert.False(state.Current.Contains(logB, 3, 0));
        Assert.DoesNotContain(new LogGeneration(logB, 3), state.AdoptedInScope);
    }

    [Fact]
    public void BuildIndex_SkipsClosedGenerationCoverageKeys_UnderAPendingReset()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        state.ReconcileLog(logId, Reader(logId, 12, 0, 0));
        state.ReconcileLog(logId, Reader(logId, 5, 1, 500));

        RebuildRequest reset = state.BeginReset(logId, newGeneration: 1);

        Assert.Equal(17, state.RowCount);

        ChunkedOrderIndex candidate = OrderedViewState.BuildIndex(reset, CancellationToken.None);

        Assert.True(state.TryAdoptRebuild(reset, candidate));
        Assert.Equal(5, state.Current.Count);
        for (int index = 0; index < 5; index++) { Assert.True(state.Current.Contains(logId, 1, index)); }

        Assert.False(state.Current.Contains(logId, 0, 0));
    }

    [Fact]
    public void Clear_ThenLateReconcile_DoesNotResurrectClosedLog()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        state.ReconcileLog(logId, Reader(logId, 15, 0, 0));
        RebuildRequest request = state.BeginRebuild(static (_, _) => true, s_context);
        Assert.True(state.TryAdoptRebuild(request, OrderedViewState.BuildIndex(request, CancellationToken.None)));
        Assert.Equal(15, state.Current.Count);

        state.Clear();

        Assert.False(state.ReconcileLog(logId, Reader(logId, 20, 0, 0)));

        state.Publish();
        Assert.Equal(0, state.Current.Count);
        Assert.Equal(0, state.RowCount);
        Assert.Empty(state.AdoptedInScope);
    }

    [Fact]
    public void ClosedGenerationCoverage_IsEvictedWithItsReader_AfterAnInScopeReload()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        state.ReconcileLog(logId, Reader(logId, 12, 0, 0));
        state.ReconcileLog(logId, Reader(logId, 5, 1, 500));

        Assert.Equal(17, state.RowCount);

        RebuildRequest reset = state.BeginReset(logId, newGeneration: 1);
        Assert.True(state.TryAdoptRebuild(reset, OrderedViewState.BuildIndex(reset, CancellationToken.None)));

        Assert.Equal(5, state.Current.Count);
        Assert.Equal(5, state.RowCount);
        Assert.Equal(1, state.TrackedReaderCount);
    }

    [Fact]
    public void Coverage_NeverExceedsPinnedReaderCount_AcrossEvictAndReturn()
    {
        EventLogId logA = EventLogId.Create();
        EventLogId logB = EventLogId.Create();
        var state = new OrderedViewState();

        AdoptScope(state,
            [logA, logB],
            new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 14, 0, 0), [logB] = Reader(logB, 11, 0, 1000) });

        Assert.Equal(25, state.RowCount);
        Assert.Equal(2, state.TrackedReaderCount);

        AdoptScope(state, [logA], new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 14, 0, 0) });

        Assert.Equal(14, state.RowCount);
        Assert.Equal(1, state.TrackedReaderCount);

        AdoptScope(state,
            [logA, logB],
            new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 14, 0, 0), [logB] = Reader(logB, 11, 0, 1000) });

        Assert.Equal(25, state.RowCount);
        Assert.Equal(2, state.TrackedReaderCount);
        Assert.Equal(25, state.Current.Count);
    }

    [Fact]
    public void Eviction_DropsGenerationMaps_SoOutOfScopeReloadIsNotHeldOutOnReturn()
    {
        EventLogId logA = EventLogId.Create();
        EventLogId logB = EventLogId.Create();
        var state = new OrderedViewState();

        AdoptScope(state,
            [logA, logB],
            new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 10, 0, 0), [logB] = Reader(logB, 8, 0, 1000) });

        AdoptScope(state, [logA], new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 10, 0, 0) });

        Assert.Equal(1, state.TrackedGenerationCount);

        AdoptScope(state,
            [logA, logB],
            new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 10, 0, 0), [logB] = Reader(logB, 7, 3, 2000) });

        Assert.Equal(17, state.Current.Count);
        Assert.Contains(new LogGeneration(logB, 3), state.AdoptedInScope);
        for (int index = 0; index < 7; index++) { Assert.True(state.Current.Contains(logB, 3, index)); }

        Assert.True(state.ReconcileLog(logB, Reader(logB, 12, 3, 2000)));
        state.Publish();
        Assert.Equal(22, state.Current.Count);
    }

    [Fact]
    public void FirstSightOfANewLogDuringAnInFlightBuild_StillReachesTheAdoptedView()
    {
        EventLogId established = EventLogId.Create();
        EventLogId newcomer = EventLogId.Create();
        var state = new OrderedViewState();

        state.ReconcileLog(established, Reader(established, 8, 0, 0));

        RebuildRequest request = state.BeginRebuild(static (_, _) => true, s_context);
        ChunkedOrderIndex candidate = OrderedViewState.BuildIndex(request, CancellationToken.None);

        Assert.Equal(8, candidate.Count);

        state.ReconcileLog(newcomer, Reader(newcomer, 6, 0, 1000));

        Assert.True(state.TryAdoptRebuild(request, candidate));

        Assert.Equal(14, state.Current.Count);
        for (int index = 0; index < 6; index++) { Assert.True(state.Current.Contains(newcomer, 0, index)); }

        for (int i = 0; i < state.Current.Count; i++)
        {
            Assert.True(state.Current.TryGetReader(state.Current.At(i).Locator, out _));
        }

        Assert.True(state.ReconcileLog(newcomer, Reader(newcomer, 10, 0, 1000)));
        state.Publish();
        Assert.Equal(18, state.Current.Count);
    }

    [Fact]
    public void HigherGenerationLiveReader_DoesNotClaimTheDisplayedGeneration_SoAnInFlightBuildCannotRegressIt()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        Assert.False(state.ReconcileLog(logId, Reader(logId, 0, 0, 0)));

        RebuildRequest request = state.BeginRebuild(static (_, _) => true, s_context);
        ChunkedOrderIndex candidate = OrderedViewState.BuildIndex(request, CancellationToken.None);

        Assert.False(state.ReconcileLog(logId, Reader(logId, 5, 1, 500)));

        state.Publish();
        Assert.Equal(0, state.Current.Count);

        Assert.True(state.TryAdoptRebuild(request, candidate));

        Assert.Equal(0, state.Current.Count);
        Assert.Equal(5, state.RowCount);

        RebuildRequest reset = state.BeginReset(logId, newGeneration: 1);
        Assert.True(state.TryAdoptRebuild(reset, OrderedViewState.BuildIndex(reset, CancellationToken.None)));

        Assert.Equal(5, state.Current.Count);
        for (int index = 0; index < 5; index++) { Assert.True(state.Current.Contains(logId, 1, index)); }
    }

    [Fact]
    public void HigherGenerationReconcile_DuringAnInFlightBuild_DoesNotPublishAnUnresolvableMix()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        state.ReconcileLog(logId, Reader(logId, 10, 0, 0));

        RebuildRequest request = state.BeginRebuild(static (_, _) => true, s_context);
        ChunkedOrderIndex candidate = OrderedViewState.BuildIndex(request, CancellationToken.None);

        Assert.Equal(10, candidate.Count);

        state.ReconcileLog(logId, Reader(logId, 5, 1, 500));

        Assert.True(state.TryAdoptRebuild(request, candidate));

        OrderedViewSnapshot snapshot = state.Current;

        Assert.Equal(10, snapshot.Count);

        for (int i = 0; i < snapshot.Count; i++)
        {
            EventLocator locator = snapshot.At(i).Locator;

            Assert.True(snapshot.TryGetReader(locator, out _),
                $"published locator {locator.LogId.Value}/gen{locator.Generation}/#{locator.Index} has no reader in its own snapshot");
        }

        var generations = new HashSet<int>();
        for (int i = 0; i < snapshot.Count; i++) { generations.Add(snapshot.At(i).Locator.Generation); }

        Assert.True(generations.Count <= 1, $"snapshot mixes generations: [{string.Join(", ", generations)}]");
    }

    [Fact]
    public void PendingHigherReset_DoesNotStrandTheStillDisplayedGenerationsLiveTail()
    {
        EventLogId logA = EventLogId.Create();
        EventLogId logB = EventLogId.Create();
        var state = new OrderedViewState();

        AdoptScope(state,
            [logA, logB],
            new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 4, 0, 0), [logB] = Reader(logB, 10, 2, 1000) });

        AdoptScope(state, [logA], new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 4, 0, 0) });

        AdoptScope(state,
            [logA, logB],
            new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 4, 0, 0), [logB] = Reader(logB, 10, 2, 1000) });

        Assert.Equal(14, state.Current.Count);

        state.BeginReset(logB, newGeneration: 5);

        Assert.True(state.ReconcileLog(logB, Reader(logB, 16, 2, 1000)));

        state.Publish();
        Assert.Equal(20, state.Current.Count);
        Assert.True(state.Current.Contains(logB, 2, 15));
    }

    [Fact]
    public void PublishedSnapshotHasNoBitsetForAnEvictedKey()
    {
        EventLogId logA = EventLogId.Create();
        EventLogId logB = EventLogId.Create();
        var state = new OrderedViewState();

        AdoptScope(state,
            [logA, logB],
            new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 6, 0, 0), [logB] = Reader(logB, 5, 0, 1000) });

        OrderedViewSnapshot wide = state.Current;
        Assert.True(wide.Contains(logB, 0, 0));

        AdoptScope(state, [logA], new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 6, 0, 0) });

        Assert.False(state.Current.Contains(logB, 0, 0));
        Assert.Equal(-1, state.Current.RankOf(new OrderKey(new EventLocator(logB, 0, 0))));

        Assert.True(wide.Contains(logB, 0, 0));
    }

    [Fact]
    public void ReconcileCoverage_PredicateFault_LeavesCoverageAtLeastEvaluated()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        RebuildRequest request = state.BeginRebuild(
            static (locator, _) => locator.Index >= 5 ? throw new InvalidOperationException("boom") : true, s_context);
        Assert.True(state.TryAdoptRebuild(request, OrderedViewState.BuildIndex(request, CancellationToken.None)));

        Assert.Throws<InvalidOperationException>(() => state.ReconcileLog(logId, Reader(logId, 10, 0, 0)));

        Assert.Equal(10, state.RowCount);

        RebuildRequest recovery = state.BeginRebuild(static (_, _) => true, s_context);
        Assert.True(state.TryAdoptRebuild(recovery, OrderedViewState.BuildIndex(recovery, CancellationToken.None)));
        Assert.Equal(10, state.Current.Count);
    }

    [Fact]
    public void ScopeAdopt_PrunesOutOfScopeReadersAfterTheIndexSwap()
    {
        EventLogId logA = EventLogId.Create();
        EventLogId logB = EventLogId.Create();
        var state = new OrderedViewState();

        AdoptScope(state,
            [logA, logB],
            new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 7, 0, 0), [logB] = Reader(logB, 4, 0, 1000) });

        Assert.Equal(2, state.Current.PinnedReaderCount);

        AdoptScope(state, [logA], new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 7, 0, 0) });

        Assert.Equal(1, state.TrackedReaderCount);
        Assert.Equal(1, state.Current.PinnedReaderCount);
        Assert.False(state.Current.TryGetReaderByLog(logB, 0, out _));
    }

    [Fact]
    public void ScopeRequest_WithHigherGenerationReader_RebuildsAtTheRequestedGeneration()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        AdoptScope(state, [logId], new Dictionary<EventLogId, IEventColumnReader> { [logId] = Reader(logId, 9, 0, 0) });
        Assert.Equal(9, state.Current.Count);

        AdoptScope(state, [logId], new Dictionary<EventLogId, IEventColumnReader> { [logId] = Reader(logId, 6, 1, 500) });

        Assert.Equal(6, state.Current.Count);
        Assert.Contains(new LogGeneration(logId, 1), state.AdoptedInScope);
        Assert.False(state.Current.Contains(logId, 0, 0));
    }

    [Fact]
    public void ScopeRequest_WithZeroRowHigherGenerationReader_StopsPublishingThePreviousGeneration()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        AdoptScope(state, [logId], new Dictionary<EventLogId, IEventColumnReader> { [logId] = Reader(logId, 9, 0, 0) });
        Assert.Equal(9, state.Current.Count);

        AdoptScope(state, [logId], new Dictionary<EventLogId, IEventColumnReader> { [logId] = Reader(logId, 0, 1, 500) });

        Assert.Equal(0, state.Current.Count);
        Assert.False(state.Current.Contains(logId, 0, 0));

        Assert.Empty(state.AdoptedInScope);
    }

    [Fact]
    public void ScopeSeeding_DoesNotInsertIntoTheOutgoingIndex_EvenWhenTheOutgoingScopeIsStillAdopted()
    {
        EventLogId logA = EventLogId.Create();
        EventLogId logB = EventLogId.Create();
        var state = new OrderedViewState();

        AdoptScope(state, [logA], new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 12, 0, 0) });

        Assert.True(state.TrySetActiveScope([logB], ViewRequests.NextSequence()));
        state.ReconcileScopeReaders(new Dictionary<EventLogId, IEventColumnReader> { [logB] = Reader(logB, 20, 0, 1000) });

        state.Publish();
        Assert.Equal(12, state.Current.Count);
        Assert.False(state.Current.Contains(logB, 0, 0));
        Assert.Equal(32, state.RowCount);

        RebuildRequest reseed = state.CaptureScopeReseed();
        Assert.True(state.TryAdoptRebuild(reseed, OrderedViewState.BuildIndex(reseed, CancellationToken.None)));

        Assert.Equal(20, state.Current.Count);
    }

    [Fact]
    public void ScopeSwitch_OwnerSynchronousWork_IsIndependentOfEnteringLogRowCount()
    {
        EventLogId logA = EventLogId.Create();
        EventLogId logB = EventLogId.Create();
        int predicateCalls = 0;
        var state = new OrderedViewState();

        bool CountingPredicate(EventLocator locator, IEventColumnReader reader)
        {
            Interlocked.Increment(ref predicateCalls);

            return true;
        }

        Assert.True(state.TrySetActiveScope([logA], ViewRequests.NextSequence()));
        state.ReconcileScopeReaders(new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 5, 0, 0) });

        RebuildRequest first = state.BeginRebuild(CountingPredicate, s_context);
        Assert.True(state.TryAdoptRebuild(first, OrderedViewState.BuildIndex(first, CancellationToken.None)));

        int afterAdopt = Volatile.Read(ref predicateCalls);

        Assert.True(state.TrySetActiveScope([logB], ViewRequests.NextSequence()));
        state.ReconcileScopeReaders(new Dictionary<EventLogId, IEventColumnReader> { [logB] = Reader(logB, 20_000, 0, 1000) });

        Assert.Equal(afterAdopt, Volatile.Read(ref predicateCalls));
        Assert.Equal(20_005, state.RowCount);

        RebuildRequest second = state.BeginRebuild(CountingPredicate, s_context);
        Assert.True(state.TryAdoptRebuild(second, OrderedViewState.BuildIndex(second, CancellationToken.None)));

        int afterSwitch = Volatile.Read(ref predicateCalls);

        Assert.True(state.TrySetActiveScope([logB], ViewRequests.NextSequence()));
        state.ReconcileScopeReaders(new Dictionary<EventLogId, IEventColumnReader> { [logB] = Reader(logB, 40_000, 0, 1000) });

        Assert.Equal(afterSwitch, Volatile.Read(ref predicateCalls));
        Assert.Equal(40_000, state.RowCount);
    }

    [Fact]
    public void SupersededBuild_NeverEvictsTheAdoptedScope()
    {
        EventLogId logA = EventLogId.Create();
        EventLogId logB = EventLogId.Create();
        EventLogId logC = EventLogId.Create();
        var state = new OrderedViewState();

        AdoptScope(state, [logA], new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 10, 0, 0) });

        Assert.True(state.TrySetActiveScope([logB], ViewRequests.NextSequence()));
        state.ReconcileScopeReaders(new Dictionary<EventLogId, IEventColumnReader> { [logB] = Reader(logB, 8, 0, 1000) });
        RebuildRequest towardsB = state.CaptureScopeReseed();
        ChunkedOrderIndex indexForB = OrderedViewState.BuildIndex(towardsB, CancellationToken.None);

        Assert.True(state.TrySetActiveScope([logC], ViewRequests.NextSequence()));
        state.ReconcileScopeReaders(new Dictionary<EventLogId, IEventColumnReader> { [logC] = Reader(logC, 6, 0, 2000) });
        RebuildRequest towardsC = state.CaptureScopeReseed();

        Assert.False(state.TryAdoptRebuild(towardsB, indexForB));

        Assert.Equal(24, state.RowCount);
        Assert.Equal(3, state.TrackedReaderCount);

        Assert.True(state.TryAdoptRebuild(towardsC, OrderedViewState.BuildIndex(towardsC, CancellationToken.None)));

        Assert.Equal(6, state.Current.Count);
        Assert.Equal(6, state.RowCount);
        Assert.Equal(1, state.TrackedReaderCount);
    }

    [Fact]
    public void TailReplay_IteratesCurrentCoverage_RecoversDuringBuildTailForEnteringScope()
    {
        EventLogId logA = EventLogId.Create();
        EventLogId logB = EventLogId.Create();
        var state = new OrderedViewState();

        AdoptScope(state, [logA], new Dictionary<EventLogId, IEventColumnReader> { [logA] = Reader(logA, 10, 0, 0) });

        Assert.True(state.TrySetActiveScope([logA, logB], ViewRequests.NextSequence()));
        RebuildRequest reseed = state.CaptureScopeReseed();
        ChunkedOrderIndex candidate = OrderedViewState.BuildIndex(reseed, CancellationToken.None);

        Assert.Equal(10, candidate.Count);

        Assert.False(state.ReconcileLog(logB, Reader(logB, 12, 0, 1000)));

        Assert.True(state.TryAdoptRebuild(reseed, candidate));

        Assert.Equal(22, state.Current.Count);
        for (int index = 0; index < 12; index++) { Assert.True(state.Current.Contains(logB, 0, index)); }

        AssertNoDuplicateLocators(state.Current);
    }

    [Fact]
    public void ZeroRowFirstSight_DoesNotClaimTheDisplayedGeneration_SoAPendingReloadCanStillBecomeDisplayed()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        Assert.False(state.ReconcileLog(logId, Reader(logId, 0, 0, 0)));

        state.BeginReset(logId, newGeneration: 1);

        Assert.True(state.ReconcileLog(logId, Reader(logId, 5, 1, 500)));

        state.Publish();

        Assert.Equal(5, state.Current.Count);
        Assert.Contains(new LogGeneration(logId, 1), state.AdoptedInScope);
    }

    [Fact]
    public void ZeroRowFirstSight_EstablishesNoDisplayedGeneration_SoTheLogStaysUnroutable()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        AdoptScope(state, [logId], new Dictionary<EventLogId, IEventColumnReader> { [logId] = Reader(logId, 0, 0, 0) });

        Assert.Equal(0, state.Current.Count);
        Assert.Empty(state.AdoptedInScope);

        Assert.True(state.ReconcileLog(logId, Reader(logId, 6, 0, 0)));
        state.Publish();

        Assert.Equal(6, state.Current.Count);
        Assert.Contains(new LogGeneration(logId, 0), state.AdoptedInScope);
    }

    private static void AdoptScope(
        OrderedViewState state,
        IReadOnlyCollection<EventLogId> scopeLogs,
        IReadOnlyDictionary<EventLogId, IEventColumnReader> scopeReaders)
    {
        Assert.True(state.TrySetActiveScope(scopeLogs, ViewRequests.NextSequence()));
        state.ReconcileScopeReaders(scopeReaders);

        RebuildRequest request = state.BeginRebuild(static (_, _) => true, s_context);

        Assert.True(state.TryAdoptRebuild(request, OrderedViewState.BuildIndex(request, CancellationToken.None)));
    }

    private static void AssertNoDuplicateLocators(OrderedViewSnapshot snapshot)
    {
        var seen = new HashSet<EventLocator>(snapshot.Count);

        for (int i = 0; i < snapshot.Count; i++)
        {
            Assert.True(seen.Add(snapshot.At(i).Locator), $"duplicate locator at display position {i}");
        }
    }

    private static IEventColumnReader Reader(EventLogId logId, int count, int generation, long recordIdBase)
    {
        var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var events = new List<ResolvedEvent>(count);

        for (int i = 0; i < count; i++)
        {
            events.Add(new ResolvedEvent("Log", LogPathType.Channel)
            {
                RecordId = recordIdBase + i,
                TimeCreated = clock.AddMilliseconds(recordIdBase + i),
                Id = 1000,
                Level = "Information",
                Source = "Provider.A",
                LogName = "Channel"
            });
        }

        return EventColumnStore.Build(events, generation, generation).CreateReader(logId);
    }
}
