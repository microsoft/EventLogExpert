// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;

namespace EventLogExpert.Runtime.Tests.LogTable.OrderedView;

public sealed class OrderedViewInvariantTests
{
    private static readonly SortContext s_defaultContext = new(ColumnName.RecordId, false, null, false);

    [Fact]
    public void AdoptFailureDuringTailReplay_LeavesEngineFullyIntact()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        Reconcile(state, logId, count: 100);
        state.Publish();

        RebuildRequest request = state.BeginRebuild(
            static (locator, _) => locator.Index >= 105 ? throw new InvalidOperationException("boom") : true, s_defaultContext);

        Reconcile(state, logId, count: 111);

        ChunkedOrderIndex rebuilt = BuildCandidate(request);
        Assert.Throws<InvalidOperationException>(() => state.TryAdoptRebuild(request, rebuilt));

        Assert.Equal(100, state.Current.Count);

        Reconcile(state, logId, count: 112);
        state.Publish();
        Assert.Equal(112, state.Current.Count);
    }

    [Fact]
    public void BeginReset_IgnoresStaleLowerGeneration_SoNewerReloadIsNotRegressed()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        Reconcile(state, logId, count: 20);
        state.Publish();
        Reconcile(state, logId, count: 15, generation: 2, recordIdBase: 500);

        state.BeginReset(logId, newGeneration: 2);
        RebuildRequest request = state.BeginReset(logId, newGeneration: 1);
        Assert.True(state.TryAdoptRebuild(request, BuildCandidate(request)));

        Assert.Equal(15, state.Current.Count);
        for (int i = 0; i < state.Current.Count; i++) { Assert.Equal(2, state.Current.At(i).Locator.Generation); }
    }

    [Fact]
    public void Clear_SupersedesInFlightRebuild_SoAStaleAdoptCannotResurrectClearedRows()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        Reconcile(state, logId, count: 50);
        state.Publish();

        RebuildRequest inFlight = state.BeginRebuild(static (_, _) => true, s_defaultContext);
        ChunkedOrderIndex rebuilt = BuildCandidate(inFlight);

        state.Clear();
        Assert.Equal(0, state.Current.Count);

        Assert.False(state.TryAdoptRebuild(inFlight, rebuilt));
        Assert.Equal(0, state.Current.Count);
    }

    [Fact]
    public void ClosedGenerationReaders_ArePruned_AfterReload()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        Reconcile(state, logId, count: 30);
        state.Publish();
        Reconcile(state, logId, count: 20, generation: 1, recordIdBase: 500);
        Assert.Equal(2, state.TrackedReaderCount);

        RebuildRequest reset = state.BeginReset(logId, newGeneration: 1);
        Assert.True(state.TryAdoptRebuild(reset, BuildCandidate(reset)));

        Assert.Equal(1, state.TrackedReaderCount);
        Assert.Equal(1, state.Current.PinnedReaderCount);
        Assert.Equal(20, state.Current.Count);
    }

    [Fact]
    public void FrontInserts_UnderDescendingOrder_PreserveOrder()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        Rebuild(state, static (_, _) => true, new SortContext(ColumnName.RecordId, true, null, false));
        Reconcile(state, logId, count: 4000);
        state.Publish();

        OrderedViewSnapshot snap = state.Current;
        Assert.Equal(4000, snap.Count);

        for (int i = 0; i < snap.Count; i++) { Assert.Equal(4000 - 1 - i, snap.At(i).Locator.Index); }
    }

    [Fact]
    public void HeldRebuild_FailingInTailReplay_ClearsHoldSoReconcileResumes()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        Reconcile(state, logId, count: 2);
        state.Publish();

        RebuildRequest held = state.BeginRebuild(
            static (locator, _) => locator.Index >= 5 ? throw new InvalidOperationException("boom") : true, s_defaultContext, hold: true);
        Reconcile(state, logId, count: 7);

        Assert.Throws<InvalidOperationException>(() => state.TryAdoptRebuild(held, BuildCandidate(held)));

        Reconcile(state, logId, count: 8);
        state.Publish();
        Assert.True(state.Current.Contains(logId, 0, 7));
    }

    [Fact]
    public void Hold_ClearsAtAdopt_SoALaterReconcileAdmitsNormally()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        Reconcile(state, logId, count: 2);
        RebuildRequest held = state.BeginRebuild(static (_, _) => true, s_defaultContext, hold: true);
        Assert.True(state.TryAdoptRebuild(held, BuildCandidate(held)));

        Reconcile(state, logId, count: 3);
        state.Publish();
        Assert.Equal(3, state.Current.Count);
        Assert.True(state.Current.Contains(logId, 0, 2));
    }

    [Fact]
    public void Hold_GatesReconcile_ThenAdmitsHeldRowsAtAdopt()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        Reconcile(state, logId, count: 2);
        state.Publish();

        RebuildRequest held = state.BeginRebuild(static (_, _) => true, s_defaultContext, hold: true);

        Reconcile(state, logId, count: 3);
        OrderedViewSnapshot mid = state.Publish();
        Assert.Equal(2, mid.Count);
        Assert.Equal(3, state.RowCount);
        Assert.False(mid.Contains(logId, 0, 2));

        Assert.True(state.TryAdoptRebuild(held, BuildCandidate(held)));
        Assert.Equal(3, state.Current.Count);
        Assert.True(state.Current.Contains(logId, 0, 2));
    }

    [Fact]
    public void Hold_IsSticky_AcrossExplicitStableSupersession_UntilAdopt()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        Reconcile(state, logId, count: 2);
        state.Publish();

        RebuildRequest holding = state.BeginRebuild(static (_, _) => true, s_defaultContext, hold: true);
        RebuildRequest stable = state.BeginRebuild(static (_, _) => true, s_defaultContext, hold: false);

        Assert.False(state.TryAdoptRebuild(holding, BuildCandidate(holding)));

        Reconcile(state, logId, count: 3);
        OrderedViewSnapshot mid = state.Publish();
        Assert.Equal(2, mid.Count);

        Assert.True(state.TryAdoptRebuild(stable, BuildCandidate(stable)));
        Assert.Equal(3, state.Current.Count);
        Assert.True(state.Current.Contains(logId, 0, 2));
    }

    [Fact]
    public void MidRebuildPublish_ShowsConsistentOldFrame_ThenNewAfterAdopt()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        Reconcile(state, logId, count: 5000);
        state.Publish();

        RebuildRequest request = state.BeginRebuild((locator, _) => locator.Index % 2 == 0, s_defaultContext);

        Reconcile(state, logId, count: 6000);
        OrderedViewSnapshot mid = state.Publish();
        Assert.Equal(6000, mid.Count);

        Assert.True(state.TryAdoptRebuild(request, BuildCandidate(request)));
        OrderedViewSnapshot post = state.Current;
        Assert.Equal(3000, post.Count);
        for (int i = 0; i < post.Count; i++) { Assert.Equal(0, post.At(i).Locator.Index % 2); }
    }

    [Fact]
    public void PendingFilter_IsInherited_ByLaterReset()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        Reconcile(state, logId, count: 100);
        state.Publish();

        RebuildRequest filter = state.BeginRebuild((locator, _) => locator.Index % 2 == 0, s_defaultContext);
        RebuildRequest reset = state.BeginReset(logId, newGeneration: 1);

        Assert.False(state.TryAdoptRebuild(filter, BuildCandidate(filter)));

        Reconcile(state, logId, count: 100, generation: 1, recordIdBase: 500);

        Assert.True(state.TryAdoptRebuild(reset, BuildCandidate(reset)));

        OrderedViewSnapshot snap = state.Current;
        Assert.Equal(50, snap.Count);
        for (int i = 0; i < snap.Count; i++)
        {
            Assert.Equal(1, snap.At(i).Locator.Generation);
            Assert.Equal(0, snap.At(i).Locator.Index % 2);
        }
    }

    [Fact]
    public void PendingReset_IsInherited_ByLaterSupersedingRebuild()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        Reconcile(state, logId, count: 100);
        state.Publish();

        RebuildRequest reset = state.BeginReset(logId, newGeneration: 1);
        RebuildRequest filter = state.BeginRebuild((locator, _) => locator.Index < 100, s_defaultContext);

        Assert.False(state.TryAdoptRebuild(reset, BuildCandidate(reset)));

        Reconcile(state, logId, count: 50, generation: 1, recordIdBase: 500);

        Assert.True(state.TryAdoptRebuild(filter, BuildCandidate(filter)));

        OrderedViewSnapshot snap = state.Current;
        Assert.Equal(50, snap.Count);
        Assert.False(snap.Contains(logId, 0, 0));
        Assert.True(snap.Contains(logId, 1, 0));
    }

    [Fact]
    public void RemoveLog_ThenReconcileAnotherLog_KeepsRemovedReaderResolvableUntilAdopt()
    {
        EventLogId removedLog = EventLogId.Create();
        EventLogId keptLog = EventLogId.Create();
        var state = new OrderedViewState();

        Reconcile(state, removedLog, count: 10);
        state.Publish();

        RebuildRequest removeRequest = state.RemoveLog(removedLog);

        Reconcile(state, keptLog, count: 1, recordIdBase: 1000);

        Assert.True(state.TryAdoptRebuild(removeRequest, BuildCandidate(removeRequest)));
        Assert.Equal(1, state.Current.Count);
        Assert.True(state.Current.Contains(keptLog, 0, 0));
        Assert.False(state.Current.Contains(removedLog, 0, 0));
    }

    [Fact]
    public void RetainedSnapshot_IsIsolated_FromLaterInsertsAndPublish()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        Reconcile(state, logId, count: 3000);
        state.Publish();
        OrderedViewSnapshot old = state.Current;

        int oldCount = old.Count;
        var oldFull = new OrderKey[oldCount];
        old.SliceInto(0, oldCount, oldFull);

        Reconcile(state, logId, count: 6000);
        state.Publish();
        OrderedViewSnapshot fresh = state.Current;

        var recheck = new OrderKey[oldCount];
        int written = old.SliceInto(0, oldCount, recheck);

        Assert.Equal(3000, oldCount);
        Assert.Equal(oldCount, written);
        Assert.True(recheck.AsSpan().SequenceEqual(oldFull.AsSpan()));
        Assert.Equal(6000, fresh.Count);
        Assert.True(fresh.Version > old.Version);
    }

    [Fact]
    public void SnapshotVersion_StaysMonotonic_AcrossRebuildAdopt()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        Reconcile(state, logId, count: 400);
        for (int i = 0; i < 5; i++) { state.Publish(); }
        long beforeRebuild = state.Current.Version;

        Rebuild(state, static (_, _) => true, new SortContext(ColumnName.Source, false, null, false));

        Assert.True(state.Current.Version > beforeRebuild, $"version regressed: {beforeRebuild} -> {state.Current.Version}");
    }

    [Fact]
    public void StaleGeneration_ForLateFirstSeenLog_IsRejectedOnReplay()
    {
        EventLogId primary = EventLogId.Create();
        EventLogId late = EventLogId.Create();
        var state = new OrderedViewState();

        Reconcile(state, primary, count: 50);
        state.Publish();

        RebuildRequest request = state.BeginRebuild(static (_, _) => true, s_defaultContext);

        Reconcile(state, late, count: 2, generation: 1, recordIdBase: 500);
        Reconcile(state, late, count: 2, generation: 0, recordIdBase: 700);

        Assert.True(state.TryAdoptRebuild(request, BuildCandidate(request)));

        OrderedViewSnapshot snap = state.Current;
        Assert.True(snap.Contains(late, 1, 0));
        Assert.False(snap.Contains(late, 0, 1));
    }

    [Fact]
    public void SupersededRebuild_IsDiscarded_AndFreshAdopts()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        Reconcile(state, logId, count: 4000);
        state.Publish();

        RebuildRequest stale = state.BeginRebuild((locator, _) => locator.Index < 1000, s_defaultContext);
        ChunkedOrderIndex staleIndex = BuildCandidate(stale);

        RebuildRequest fresh = state.BeginRebuild((locator, _) => locator.Index >= 2000, s_defaultContext);
        ChunkedOrderIndex freshIndex = BuildCandidate(fresh);

        Assert.False(state.TryAdoptRebuild(stale, staleIndex));
        Assert.True(state.TryAdoptRebuild(fresh, freshIndex));

        OrderedViewSnapshot snap = state.Current;
        Assert.Equal(2000, snap.Count);
        Assert.Equal(2000, snap.At(0).Locator.Index);
        Assert.False(snap.Contains(logId, 0, 1999));
        Assert.True(snap.Contains(logId, 0, 2000));
    }

    private static ChunkedOrderIndex BuildCandidate(RebuildRequest request) => OrderedViewState.BuildIndex(request);

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

    private static void Rebuild(OrderedViewState state, Func<EventLocator, IEventColumnReader, bool> predicate, SortContext context)
    {
        RebuildRequest request = state.BeginRebuild(predicate, context);
        Assert.True(state.TryAdoptRebuild(request, BuildCandidate(request)));
    }

    private static void Reconcile(
        OrderedViewState state, EventLogId logId, int count, int generation = 0, long recordIdBase = 0) =>
        state.ReconcileLog(logId, Reader(logId, count, generation, recordIdBase));
}
