// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using EventLogExpert.Runtime.Tests.LogTable.TestSupport;

namespace EventLogExpert.Runtime.Tests.LogTable.OrderedView;

public sealed class ReconcileLogStaleOrderTests
{
    private static readonly SortContext s_sourceAscending = new(ColumnName.Source, false, null, false);

    [Fact]
    public void CanRestampAdopted_AfterAppendDuringRebuild_StillRestamps()
    {
        EventLogId logId = EventLogId.Create();
        IEventColumnReader beginRows = Reader(logId, contentVersion: 0, ("AAA", 0), ("BBB", 1));

        var state = new OrderedViewState();
        Assert.True(state.TrySetActiveScope([logId], ViewRequests.NextSequence()));
        state.ReconcileScopeReaders(new Dictionary<EventLogId, IEventColumnReader> { [logId] = beginRows });

        // Capture the rebuild against a non-empty head, then let a plain append (not a replace) grow the log before the
        // build adopts. The append bumps ContentVersion but preserves the head rows, so the adopt must still stamp: a
        // content-version mismatch alone must not refuse an append-only build (that would drop the restamp fast-path).
        RebuildRequest request = state.BeginRebuild(static (_, _) => true, s_sourceAscending);
        IEventColumnReader appendedDuringBuild = Reader(logId, contentVersion: 1, ("AAA", 0), ("BBB", 1), ("CCC", 2));
        Assert.True(state.ReconcileLog(logId, appendedDuringBuild, isReplace: false, out bool requiresRebuild));
        Assert.False(requiresRebuild);

        Assert.True(state.TryAdoptRebuild(request, OrderedViewState.BuildIndex(request, CancellationToken.None)));

        Assert.True(state.CanRestampAdopted(
            [logId], new Dictionary<EventLogId, IEventColumnReader> { [logId] = appendedDuringBuild }));
    }

    [Fact]
    public void CanRestampAdopted_AfterGrowReplace_RefusesUntilARebuildAdopts()
    {
        EventLogId logId = EventLogId.Create();
        IEventColumnReader adopted = Reader(logId, contentVersion: 0, ("AAA", 0), ("BBB", 1));

        var state = new OrderedViewState();
        AdoptSourceView(state, [logId], new Dictionary<EventLogId, IEventColumnReader> { [logId] = adopted });

        // A grow-replace reorders existing rows and adds one: contentReplaced is true, so the stamp must NOT advance.
        IEventColumnReader grownReplace = Reader(logId, contentVersion: 1, ("CCC", 0), ("BBB", 1), ("AAA", 2));
        Assert.True(state.ReconcileLog(logId, grownReplace, isReplace: true, out bool requiresRebuild));
        Assert.True(requiresRebuild);

        Assert.False(state.CanRestampAdopted(
            [logId], new Dictionary<EventLogId, IEventColumnReader> { [logId] = grownReplace }));

        // Only the rebuild that re-orders the index from the replaced content re-stamps it.
        RebuildRequest reseed = state.CaptureScopeReseed();
        Assert.True(state.TryAdoptRebuild(reseed, OrderedViewState.BuildIndex(reseed, CancellationToken.None)));
        Assert.True(state.CanRestampAdopted(
            [logId], new Dictionary<EventLogId, IEventColumnReader> { [logId] = grownReplace }));
    }

    [Fact]
    public void CanRestampAdopted_AfterLiveAppendAtHigherContentVersion_StillRestamps()
    {
        EventLogId logId = EventLogId.Create();
        IEventColumnReader adopted = Reader(logId, contentVersion: 0, ("AAA", 0), ("BBB", 1));

        var state = new OrderedViewState();
        AdoptSourceView(state, [logId], new Dictionary<EventLogId, IEventColumnReader> { [logId] = adopted });

        // A live append grows the count and carries a higher content version but is NOT a replace: the appended rows
        // keep the adopted index coherent, so the content-version stamp advances with them.
        IEventColumnReader appended = Reader(logId, contentVersion: 1, ("AAA", 0), ("BBB", 1), ("CCC", 2));
        Assert.True(state.ReconcileLog(logId, appended, isReplace: false, out bool requiresRebuild));
        Assert.False(requiresRebuild);

        Assert.True(state.CanRestampAdopted(
            [logId], new Dictionary<EventLogId, IEventColumnReader> { [logId] = appended }));
    }

    [Fact]
    public void CanRestampAdopted_AfterSameCountInPlaceReplace_RefusesTheRestampFastPath()
    {
        EventLogId logId = EventLogId.Create();
        IEventColumnReader adopted = Reader(logId, contentVersion: 0, ("AAA", 0), ("BBB", 1));

        var state = new OrderedViewState();
        AdoptSourceView(state, [logId], new Dictionary<EventLogId, IEventColumnReader> { [logId] = adopted });

        Assert.True(state.CanRestampAdopted(
            [logId], new Dictionary<EventLogId, IEventColumnReader> { [logId] = adopted }));

        // An in-place replace bumps ContentVersion at the same generation and count, so the adopted index is now stale.
        IEventColumnReader replaced = Reader(logId, contentVersion: 1, ("BBB", 0), ("AAA", 1));
        Assert.True(state.ReconcileLog(logId, replaced, isReplace: true, out bool requiresRebuild));
        Assert.True(requiresRebuild);

        // Generation and count still match, but the content version no longer does, so the restamp must be refused.
        Assert.False(state.CanRestampAdopted(
            [logId], new Dictionary<EventLogId, IEventColumnReader> { [logId] = replaced }));
    }

    [Fact]
    public void CanRestampAdopted_AppendDoesNotReviveAStampRemovedByAMixedContentAdopt()
    {
        EventLogId logId = EventLogId.Create();
        IEventColumnReader beginContent = Reader(logId, contentVersion: 0, ("AAA", 0), ("BBB", 1));

        var state = new OrderedViewState();
        Assert.True(state.TrySetActiveScope([logId], ViewRequests.NextSequence()));
        state.ReconcileScopeReaders(new Dictionary<EventLogId, IEventColumnReader> { [logId] = beginContent });

        // Capture the rebuild against the begin content, then seed a same-count, higher-content-version reader before
        // the build adopts. The seed path admits it as a re-resolution without growing coverage, so no rebuild runs.
        RebuildRequest request = state.BeginRebuild(static (_, _) => true, s_sourceAscending);
        IEventColumnReader resolvedContent = Reader(logId, contentVersion: 1, ("BBB", 0), ("AAA", 1));
        Assert.False(state.SeedScopeReader(logId, resolvedContent));

        // The adopt lands a head ordered from the pre-seed content while the latest reader is newer, so this mixed
        // adopt refuses the stamp.
        Assert.True(state.TryAdoptRebuild(request, OrderedViewState.BuildIndex(request, CancellationToken.None)));
        Assert.False(state.CanRestampAdopted(
            [logId], new Dictionary<EventLogId, IEventColumnReader> { [logId] = resolvedContent }));

        // A routine append must NOT re-create the removed stamp, so the next request still rebuilds.
        IEventColumnReader appended = Reader(logId, contentVersion: 2, ("BBB", 0), ("AAA", 1), ("CCC", 2));
        Assert.True(state.ReconcileLog(logId, appended, isReplace: false, out bool requiresRebuild));
        Assert.False(requiresRebuild);
        Assert.False(state.CanRestampAdopted(
            [logId], new Dictionary<EventLogId, IEventColumnReader> { [logId] = appended }));
    }

    [Fact]
    public void CanRestampAdopted_ReplaceThenAppendBeforeRebuild_StaysRefused()
    {
        EventLogId logId = EventLogId.Create();
        IEventColumnReader adopted = Reader(logId, contentVersion: 0, ("AAA", 0), ("BBB", 1));

        var state = new OrderedViewState();
        AdoptSourceView(state, [logId], new Dictionary<EventLogId, IEventColumnReader> { [logId] = adopted });

        // A same-count replace arrives through the seed path, which admits it without growing coverage and so schedules
        // no rebuild. It must still invalidate the adopted stamp immediately: the adopted index now orders stale rows.
        IEventColumnReader replaced = Reader(logId, contentVersion: 1, ("BBB", 0), ("AAA", 1));
        Assert.False(state.SeedScopeReader(logId, replaced));
        Assert.False(state.CanRestampAdopted(
            [logId], new Dictionary<EventLogId, IEventColumnReader> { [logId] = replaced }));

        // A later append must NOT revive the stamp the replace invalidated, so the stale head can never be restamped.
        IEventColumnReader appended = Reader(logId, contentVersion: 2, ("BBB", 0), ("AAA", 1), ("CCC", 2));
        Assert.True(state.ReconcileLog(logId, appended, isReplace: false, out _));
        Assert.False(state.CanRestampAdopted(
            [logId], new Dictionary<EventLogId, IEventColumnReader> { [logId] = appended }));
    }

    [Fact]
    public void CanRestampAdopted_StampsALogWhoseRowsAllArriveDuringTheRebuild()
    {
        EventLogId early = EventLogId.Create();
        EventLogId late = EventLogId.Create();
        IEventColumnReader earlyRows = Reader(early, contentVersion: 0, ("AAA", 0), ("BBB", 1));

        var state = new OrderedViewState();
        Assert.True(state.TrySetActiveScope([early, late], ViewRequests.NextSequence()));
        state.ReconcileScopeReaders(new Dictionary<EventLogId, IEventColumnReader> { [early] = earlyRows });

        // Capture the rebuild while the late log is still empty (its head coverage is zero), then let its rows arrive
        // before the build adopts. Its whole range enters through the tail replay from the commit reader.
        RebuildRequest request = state.BeginRebuild(static (_, _) => true, s_sourceAscending);
        IEventColumnReader lateRows = Reader(late, contentVersion: 0, ("CCC", 0), ("DDD", 1));
        Assert.True(state.ReconcileLog(late, lateRows, isReplace: false, out _));

        Assert.True(state.TryAdoptRebuild(request, OrderedViewState.BuildIndex(request, CancellationToken.None)));

        // The late log must still restamp: a zero-coverage head is stamped from the commit reader, not excluded.
        Assert.True(state.CanRestampAdopted(
            [early, late],
            new Dictionary<EventLogId, IEventColumnReader> { [early] = earlyRows, [late] = lateRows }));
    }

    [Fact]
    public void ReconcileLog_GrowAppendHigherContentVersion_DoesNotSignalRebuild()
    {
        EventLogId logId = EventLogId.Create();
        IEventColumnReader original = Reader(logId, contentVersion: 0, ("AAA", 0), ("BBB", 1));
        IEventColumnReader grownAppend = Reader(logId, contentVersion: 1, ("AAA", 0), ("BBB", 1), ("CCC", 2));

        var state = new OrderedViewState();
        AdoptSourceView(state, [logId], new Dictionary<EventLogId, IEventColumnReader> { [logId] = original });

        state.ReconcileLog(logId, grownAppend, isReplace: false, out bool requiresRebuild);
        Assert.False(requiresRebuild);
    }

    [Fact]
    public void ReconcileLog_GrowReplaceHigherContentVersion_SignalsRebuildAndReorders()
    {
        EventLogId logId = EventLogId.Create();
        IEventColumnReader original = Reader(logId, contentVersion: 0, ("AAA", 0), ("BBB", 1));
        IEventColumnReader grownReplace = Reader(logId, contentVersion: 1, ("CCC", 0), ("BBB", 1), ("AAA", 2));

        var state = new OrderedViewState();
        AdoptSourceView(state, [logId], new Dictionary<EventLogId, IEventColumnReader> { [logId] = original });
        Assert.Equal(0, state.Current.At(0).Locator.Index);

        Assert.True(state.ReconcileLog(logId, grownReplace, isReplace: true, out bool requiresRebuild));
        Assert.True(requiresRebuild);

        RebuildRequest reseed = state.CaptureScopeReseed();
        Assert.True(state.TryAdoptRebuild(reseed, OrderedViewState.BuildIndex(reseed, CancellationToken.None)));
        Assert.Equal(3, state.Current.Count);
        Assert.Equal(2, state.Current.At(0).Locator.Index);
        Assert.Equal(1, state.Current.At(1).Locator.Index);
        Assert.Equal(0, state.Current.At(2).Locator.Index);
    }

    [Fact]
    public void ReconcileLog_HigherContentVersionWithMoreRows_DoesNotSignalRebuild()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        Assert.True(state.ReconcileLog(logId, Reader(logId, contentVersion: 0, ("AAA", 0), ("BBB", 1))));

        state.ReconcileLog(
            logId,
            Reader(logId, contentVersion: 1, ("AAA", 0), ("BBB", 1), ("CCC", 2)),
            out bool requiresRebuild);
        Assert.False(requiresRebuild);
    }

    [Fact]
    public void ReconcileLog_LowerCountHigherContentVersion_IsDroppedAndDoesNotSignalRebuild()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        Assert.True(state.ReconcileLog(logId, Reader(logId, contentVersion: 0, ("AAA", 0), ("BBB", 1), ("CCC", 2))));

        Assert.False(state.ReconcileLog(
            logId,
            Reader(logId, contentVersion: 1, ("BBB", 0), ("AAA", 1)),
            out bool requiresRebuild));
        Assert.False(requiresRebuild);
    }

    [Fact]
    public void ReconcileLog_SameCountHigherContentVersion_SignalsRebuildAndReorders()
    {
        EventLogId logId = EventLogId.Create();
        IEventColumnReader original = Reader(logId, contentVersion: 0, ("AAA", 0), ("BBB", 1));
        IEventColumnReader reresolved = Reader(logId, contentVersion: 1, ("BBB", 0), ("AAA", 1));

        var state = new OrderedViewState();
        AdoptSourceView(state, [logId], new Dictionary<EventLogId, IEventColumnReader> { [logId] = original });
        Assert.Equal(0, state.Current.At(0).Locator.Index);

        Assert.True(state.ReconcileLog(logId, reresolved, out bool requiresRebuild));
        Assert.True(requiresRebuild);

        RebuildRequest reseed = state.CaptureScopeReseed();
        Assert.True(state.TryAdoptRebuild(reseed, OrderedViewState.BuildIndex(reseed, CancellationToken.None)));
        Assert.Equal(1, state.Current.At(0).Locator.Index);
    }

    [Fact]
    public void ReconcileLog_SameCountReplaceInDefaultOpenScope_SignalsRebuild()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        Assert.True(state.ReconcileLog(logId, Reader(logId, contentVersion: 0, ("AAA", 0), ("BBB", 1))));

        Assert.True(state.ReconcileLog(
            logId,
            Reader(logId, contentVersion: 1, ("BBB", 0), ("AAA", 1)),
            out bool requiresRebuild));
        Assert.True(requiresRebuild);
    }

    [Fact]
    public void ReconcileLog_SameCountReplaceOfLiveInsertedLogNotYetPublished_SignalsRebuild()
    {
        EventLogId visible = EventLogId.Create();
        EventLogId lateLoader = EventLogId.Create();

        var state = new OrderedViewState();
        AdoptSourceView(
            state,
            [visible, lateLoader],
            new Dictionary<EventLogId, IEventColumnReader>
            {
                [visible] = Reader(visible, contentVersion: 0, ("MMM", 0), ("NNN", 1)),
                [lateLoader] = Reader(lateLoader, contentVersion: 0)
            });

        state.ReconcileLog(lateLoader, Reader(lateLoader, contentVersion: 0, ("AAA", 0), ("BBB", 1)));

        Assert.True(state.ReconcileLog(
            lateLoader,
            Reader(lateLoader, contentVersion: 1, ("BBB", 0), ("AAA", 1)),
            out bool requiresRebuild));
        Assert.True(requiresRebuild);
    }

    [Fact]
    public void ReconcileLog_SameCountReplaceOfLogEnteringScope_SignalsRebuild()
    {
        EventLogId adopted = EventLogId.Create();
        EventLogId entering = EventLogId.Create();

        var state = new OrderedViewState();
        AdoptSourceView(
            state,
            [adopted],
            new Dictionary<EventLogId, IEventColumnReader> { [adopted] = Reader(adopted, contentVersion: 0, ("AAA", 0), ("BBB", 1)) });

        Assert.True(state.TrySetActiveScope([adopted, entering], ViewRequests.NextSequence()));
        state.ReconcileLog(entering, Reader(entering, contentVersion: 0, ("CCC", 0), ("DDD", 1)));

        state.ReconcileLog(
            entering,
            Reader(entering, contentVersion: 1, ("DDD", 0), ("CCC", 1)),
            out bool requiresRebuild);
        Assert.True(requiresRebuild);
    }

    [Fact]
    public void ReconcileLog_ShrinkReplace_InvalidatesSignalsRebuildAndReorders()
    {
        EventLogId logId = EventLogId.Create();
        IEventColumnReader original = Reader(logId, contentVersion: 0, ("AAA", 0), ("BBB", 1), ("CCC", 2));
        IEventColumnReader shrunkReplace = Reader(logId, contentVersion: 1, ("BBB", 0), ("AAA", 1));

        var state = new OrderedViewState();
        AdoptSourceView(state, [logId], new Dictionary<EventLogId, IEventColumnReader> { [logId] = original });
        Assert.Equal(3, state.Current.Count);

        Assert.True(state.ReconcileLog(logId, shrunkReplace, isReplace: true, out bool requiresRebuild));
        Assert.True(requiresRebuild);
        Assert.True(state.LiveIndexInvalidated);

        RebuildRequest reseed = state.CaptureScopeReseed();
        Assert.True(state.TryAdoptRebuild(reseed, OrderedViewState.BuildIndex(reseed, CancellationToken.None)));
        Assert.False(state.LiveIndexInvalidated);
        Assert.Equal(2, state.Current.Count);
        Assert.Equal(1, state.Current.At(0).Locator.Index);
        Assert.Equal(0, state.Current.At(1).Locator.Index);
    }

    [Fact]
    public void ReconcileLog_ShrinkToZeroReplace_InvalidatesSignalsRebuildAndEmpties()
    {
        EventLogId logId = EventLogId.Create();

        var state = new OrderedViewState();
        AdoptSourceView(
            state,
            [logId],
            new Dictionary<EventLogId, IEventColumnReader> { [logId] = Reader(logId, contentVersion: 0, ("AAA", 0), ("BBB", 1)) });
        Assert.Equal(2, state.Current.Count);

        state.ReconcileLog(logId, Reader(logId, contentVersion: 1), isReplace: true, out bool requiresRebuild);
        Assert.True(requiresRebuild);
        Assert.True(state.LiveIndexInvalidated);

        RebuildRequest reseed = state.CaptureScopeReseed();
        Assert.True(state.TryAdoptRebuild(reseed, OrderedViewState.BuildIndex(reseed, CancellationToken.None)));
        Assert.False(state.LiveIndexInvalidated);
        Assert.Equal(0, state.Current.Count);
    }

    [Fact]
    public async Task Writer_CoalescedReorderReplace_RepublishesFullySortedOrder()
    {
        EventLogId logId = EventLogId.Create();

        using var parked = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);
        CancellationToken token = TestContext.Current.CancellationToken;

        await using var writer = new OrderedViewWriter(publishEvery: 1, publishIntervalMs: 0);

        var original = new Dictionary<EventLogId, IEventColumnReader>
        {
            [logId] = Reader(logId, contentVersion: 0, ("AAA", 0), ("BBB", 1))
        };

        // Adopt a first view (RecordId sort) to establish the log's readers and coverage.
        writer.EnqueueViewRequest(ViewRequests.For(
            new SortContext(ColumnName.RecordId, false, null, false),
            ViewRequests.EmptyFilter,
            [logId],
            static (_, _) => true,
            readers: original));
        await writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default, token);

        // Re-sort by Source with a gated predicate so THAT build (a different view) parks in flight, then coalesce a
        // reordered grow-REPLACE onto it. The seed path admits the replacement as an append; the tail-replay leaves
        // the prior rows stale, so the corrective rebuild is the only thing that re-sorts them by the replaced content.
        writer.EnqueueViewRequest(ViewRequests.For(
            s_sourceAscending,
            ViewRequests.EmptyFilter,
            [logId],
            (_, _) =>
            {
                entered.Set();
                parked.Wait(OrderedViewTestTimeouts.Default, token);

                return true;
            },
            readers: original));

        Assert.True(entered.Wait(OrderedViewTestTimeouts.Default, token));

        var replaced = new Dictionary<EventLogId, IEventColumnReader>
        {
            [logId] = Reader(logId, contentVersion: 1, ("CCC", 0), ("BBB", 1), ("AAA", 2))
        };
        writer.EnqueueViewRequest(
            ViewRequests.For(s_sourceAscending, ViewRequests.EmptyFilter, [logId], static (_, _) => true, readers: replaced));

        parked.Set();
        await writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default, token);
        await writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default, token);

        Assert.True(
            SpinWait.SpinUntil(
                () => writer.Current.Count == 3 &&
                    writer.Current.At(0).Locator.Index == 2 &&
                    writer.Current.At(1).Locator.Index == 1 &&
                    writer.Current.At(2).Locator.Index == 0,
                OrderedViewTestTimeouts.Default),
            "the corrective rebuild must republish the fully sorted replaced order");
        Assert.Null(writer.Faulted);
    }

    [Fact]
    public async Task Writer_GrowReplaceHigherContentVersion_RepublishesReorderedSnapshot()
    {
        EventLogId logId = EventLogId.Create();
        IEventColumnReader original = Reader(logId, contentVersion: 0, ("AAA", 0), ("BBB", 1));
        IEventColumnReader grownReplace = Reader(logId, contentVersion: 1, ("CCC", 0), ("BBB", 1), ("AAA", 2));

        await using var writer = new OrderedViewWriter(publishEvery: 5000);

        writer.EnqueueReconcile(logId, original);
        writer.EnqueueViewRequest(ViewRequests.For(s_sourceAscending, ViewRequests.EmptyFilter, [logId]));
        OrderedViewSnapshot adopted =
            await writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken);
        Assert.Equal(2, adopted.Count);
        Assert.Equal(0, adopted.At(0).Locator.Index);

        writer.EnqueueReconcile(logId, grownReplace, isReplace: true);
        OrderedViewSnapshot republished =
            await writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken);

        Assert.Equal(3, republished.Count);
        Assert.Equal(2, republished.At(0).Locator.Index);
        Assert.Equal(1, republished.At(1).Locator.Index);
        Assert.Equal(0, republished.At(2).Locator.Index);
        Assert.Null(writer.Faulted);
    }

    [Fact]
    public async Task Writer_SameCountHigherContentVersion_RepublishesReorderedSnapshot()
    {
        EventLogId logId = EventLogId.Create();
        IEventColumnReader original = Reader(logId, contentVersion: 0, ("AAA", 0), ("BBB", 1));
        IEventColumnReader reresolved = Reader(logId, contentVersion: 1, ("BBB", 0), ("AAA", 1));

        await using var writer = new OrderedViewWriter(publishEvery: 5000);

        writer.EnqueueReconcile(logId, original);
        writer.EnqueueViewRequest(ViewRequests.For(s_sourceAscending, ViewRequests.EmptyFilter, [logId]));
        OrderedViewSnapshot adopted =
            await writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken);
        Assert.Equal(0, adopted.At(0).Locator.Index);

        writer.EnqueueReconcile(logId, reresolved);
        OrderedViewSnapshot republished =
            await writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken);

        Assert.Equal(1, republished.At(0).Locator.Index);
        Assert.Null(writer.Faulted);
    }

    [Fact]
    public async Task Writer_SecondReplaceDuringInFlightRebuild_FinalOrderReflectsLatestReader()
    {
        EventLogId logId = EventLogId.Create();

        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        int gateArmed = 0;

        bool Predicate(EventLocator locator, IEventColumnReader reader)
        {
            if (Volatile.Read(ref gateArmed) == 1 && Interlocked.Exchange(ref gateArmed, 0) == 1)
            {
                entered.Set();
                release.Wait(OrderedViewTestTimeouts.Default);
            }

            return true;
        }

        await using var writer = new OrderedViewWriter(publishEvery: 5000);

        writer.EnqueueReconcile(logId, Reader(logId, contentVersion: 0, ("AAA", 0), ("BBB", 1), ("CCC", 2)));
        writer.EnqueueViewRequest(ViewRequests.For(s_sourceAscending, ViewRequests.EmptyFilter, [logId], Predicate));
        OrderedViewSnapshot adopted =
            await writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken);
        Assert.Equal(0, adopted.At(0).Locator.Index);

        Volatile.Write(ref gateArmed, 1);
        writer.EnqueueReconcile(logId, Reader(logId, contentVersion: 1, ("CCC", 0), ("BBB", 1), ("AAA", 2)));
        Assert.True(entered.Wait(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken));

        writer.EnqueueReconcile(logId, Reader(logId, contentVersion: 2, ("BBB", 0), ("AAA", 1), ("CCC", 2)));
        release.Set();

        OrderedViewSnapshot final =
            await writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken);

        Assert.Equal(1, final.At(0).Locator.Index);
        Assert.Null(writer.Faulted);
    }

    [Fact]
    public async Task Writer_ShrinkReplace_PublishAndConcurrentAppendDuringRebuildDoNotFault()
    {
        EventLogId logId = EventLogId.Create();
        EventLogId other = EventLogId.Create();

        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        int gateArmed = 0;

        bool Predicate(EventLocator locator, IEventColumnReader reader)
        {
            if (Volatile.Read(ref gateArmed) == 1 && Interlocked.Exchange(ref gateArmed, 0) == 1)
            {
                entered.Set();
                release.Wait(OrderedViewTestTimeouts.Default);
            }

            return true;
        }

        await using var writer = new OrderedViewWriter(publishEvery: 5000);

        writer.EnqueueReconcile(logId, Reader(logId, contentVersion: 0, ("AAA", 0), ("BBB", 1), ("CCC", 2)));
        writer.EnqueueReconcile(other, Reader(other, contentVersion: 0, ("DDD", 0), ("EEE", 1)));
        writer.EnqueueViewRequest(ViewRequests.For(s_sourceAscending, ViewRequests.EmptyFilter, [logId, other], Predicate));
        OrderedViewSnapshot adopted =
            await writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken);
        Assert.Equal(5, adopted.Count);

        Volatile.Write(ref gateArmed, 1);
        writer.EnqueueReconcile(logId, Reader(logId, contentVersion: 1, ("AAA", 0)), isReplace: true);
        Assert.True(entered.Wait(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken));

        writer.EnqueueFlush();
        writer.EnqueueReconcile(other, Reader(other, contentVersion: 1, ("DDD", 0), ("EEE", 1), ("FFF", 2)));

        release.Set();

        OrderedViewSnapshot final =
            await writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken);

        Assert.Null(writer.Faulted);
        Assert.Equal(4, final.Count);
    }

    [Fact]
    public async Task Writer_ShrinkReplace_RepublishesShrunkSnapshotWithoutFault()
    {
        EventLogId logId = EventLogId.Create();

        await using var writer = new OrderedViewWriter(publishEvery: 5000);

        writer.EnqueueReconcile(logId, Reader(logId, contentVersion: 0, ("AAA", 0), ("BBB", 1), ("CCC", 2)));
        writer.EnqueueViewRequest(ViewRequests.For(s_sourceAscending, ViewRequests.EmptyFilter, [logId]));
        OrderedViewSnapshot adopted =
            await writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken);
        Assert.Equal(3, adopted.Count);

        writer.EnqueueReconcile(logId, Reader(logId, contentVersion: 1, ("BBB", 0), ("AAA", 1)), isReplace: true);
        OrderedViewSnapshot shrunk =
            await writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken);

        Assert.Equal(2, shrunk.Count);
        Assert.Equal(1, shrunk.At(0).Locator.Index);
        Assert.Equal(0, shrunk.At(1).Locator.Index);
        Assert.Null(writer.Faulted);
    }

    private static void AdoptSourceView(
        OrderedViewState state,
        IReadOnlyCollection<EventLogId> scopeLogs,
        IReadOnlyDictionary<EventLogId, IEventColumnReader> scopeReaders)
    {
        Assert.True(state.TrySetActiveScope(scopeLogs, ViewRequests.NextSequence()));
        state.ReconcileScopeReaders(scopeReaders);

        RebuildRequest request = state.BeginRebuild(static (_, _) => true, s_sourceAscending);

        Assert.True(state.TryAdoptRebuild(request, OrderedViewState.BuildIndex(request, CancellationToken.None)));
    }

    private static IEventColumnReader Reader(EventLogId logId, int contentVersion, params (string Source, long RecordId)[] rows)
    {
        var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var events = new List<ResolvedEvent>(rows.Length);

        foreach ((string source, long recordId) in rows)
        {
            events.Add(new ResolvedEvent("Log", LogPathType.Channel)
            {
                RecordId = recordId,
                TimeCreated = clock.AddMilliseconds(recordId),
                Id = 1000,
                Level = "Information",
                Source = source,
                LogName = "Channel"
            });
        }

        return EventColumnStore.Build(events, generation: 0, contentVersion: contentVersion).CreateReader(logId);
    }
}
