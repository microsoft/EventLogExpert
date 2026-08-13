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
