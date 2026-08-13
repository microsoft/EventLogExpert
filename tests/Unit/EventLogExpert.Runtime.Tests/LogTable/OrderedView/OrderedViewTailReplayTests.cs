// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using EventLogExpert.Runtime.Tests.LogTable.TestSupport;

namespace EventLogExpert.Runtime.Tests.LogTable.OrderedView;

public sealed class OrderedViewTailReplayTests
{
    private static readonly SortContext s_context = new(ColumnName.RecordId, false, null, false);
    private static readonly Filter s_emptyFilter = new(null, []);

    [Fact]
    public void TryAdoptRebuild_AbandonHeldTail_ReleasesHold_SoLiveIngestResumes()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        state.ReconcileLog(logId, Reader(logId, 50));
        RebuildRequest first = state.BeginRebuild(static (_, _) => true, s_context);
        Assert.True(state.TryAdoptRebuild(first, OrderedViewState.BuildIndex(first, CancellationToken.None)));
        Assert.Equal(50, state.Current.Count);

        RebuildRequest held = state.BeginRebuild(static (_, _) => true, s_context, hold: true);
        state.ReconcileLog(logId, Reader(logId, 400));

        AdoptOutcome outcome = state.TryAdoptRebuild(
            held, OrderedViewState.BuildIndex(held, CancellationToken.None), tailBudget: 5, allowAbandon: true);

        Assert.Equal(AdoptOutcome.AbandonedTail, outcome);

        state.ReconcileLog(logId, Reader(logId, 500));
        OrderedViewSnapshot resumed = state.Publish();

        Assert.True(resumed.Count > 50, "abandoning a held tail must release the hold so live ingest resumes");
    }

    [Fact]
    public void TryAdoptRebuild_AbandonsOverBudgetTail_WithoutAdopting_ThenForcedReplayAdoptsFullTail()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        state.ReconcileLog(logId, Reader(logId, 50));
        RebuildRequest request = state.BeginRebuild(static (_, _) => true, s_context);
        state.ReconcileLog(logId, Reader(logId, 200));

        AdoptOutcome abandoned = state.TryAdoptRebuild(
            request, OrderedViewState.BuildIndex(request, CancellationToken.None), tailBudget: 10, allowAbandon: true);

        Assert.Equal(AdoptOutcome.AbandonedTail, abandoned);
        Assert.Equal(0, state.Current.Count);

        AdoptOutcome adopted = state.TryAdoptRebuild(
            request, OrderedViewState.BuildIndex(request, CancellationToken.None), tailBudget: 10, allowAbandon: false);

        Assert.Equal(AdoptOutcome.Adopted, adopted);
        Assert.Equal(200, state.Current.Count);
    }

    [Fact]
    public void TryAdoptRebuild_ReplaysTail_WhenWithinBudget_EvenWhenAbandonAllowed()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        state.ReconcileLog(logId, Reader(logId, 50));
        RebuildRequest request = state.BeginRebuild(static (_, _) => true, s_context);
        state.ReconcileLog(logId, Reader(logId, 60));

        AdoptOutcome outcome = state.TryAdoptRebuild(
            request, OrderedViewState.BuildIndex(request, CancellationToken.None), tailBudget: 50, allowAbandon: true);

        Assert.Equal(AdoptOutcome.Adopted, outcome);
        Assert.Equal(60, state.Current.Count);
    }

    [Fact]
    public void TryAdoptRebuild_StaleGeneration_DropsWithoutAbandoning()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        state.ReconcileLog(logId, Reader(logId, 50));
        RebuildRequest request = state.BeginRebuild(static (_, _) => true, s_context);
        ChunkedOrderIndex candidate = OrderedViewState.BuildIndex(request, CancellationToken.None);

        state.ReconcileLog(logId, Reader(logId, 400));
        state.BeginRebuild(static (_, _) => true, s_context);

        AdoptOutcome outcome = state.TryAdoptRebuild(request, candidate, tailBudget: 10, allowAbandon: true);

        Assert.Equal(AdoptOutcome.DroppedStale, outcome);
        Assert.Equal(0, state.Current.Count);
    }

    [Fact]
    public async Task Writer_OverBudgetTail_AbandonsAndReschedules_ThenConvergesToFullCount()
    {
        EventLogId logId = EventLogId.Create();
        IEventColumnReader initialReader = Reader(logId, 50);
        IEventColumnReader grownReader = Reader(logId, 400);

        using var entered = new ManualResetEventSlim(false);
        using var blocking = new ManualResetEventSlim(false);
        int gateArmed = 1;

        await using var writer = new OrderedViewWriter(
            publishEvery: 16, publishIntervalMs: 0, tailReplayBudget: 5, tailBreachLimit: 3);

        writer.EnqueueReconcile(logId, initialReader);
        writer.EnqueueViewRequest(ViewRequests.For(s_context, s_emptyFilter, [logId], Predicate));

        Assert.True(entered.Wait(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken));

        writer.EnqueueReconcile(logId, grownReader);
        blocking.Set();

        OrderedViewSnapshot snapshot = await writer.DrainAsync();

        Assert.Null(writer.Faulted);
        Assert.Equal(400, snapshot.Count);
        Assert.True(writer.BuildsStarted >= 2, $"expected a reschedule after the abandon, saw {writer.BuildsStarted} builds");
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
    public async Task Writer_SustainedTail_ReachesForcedReplayAfterBreachLimit_AndConverges()
    {
        EventLogId logId = EventLogId.Create();
        using var buildEntered = new SemaphoreSlim(0);
        using var buildRelease = new SemaphoreSlim(0);

        await using var writer = new OrderedViewWriter(
            publishEvery: 16, publishIntervalMs: 0, tailReplayBudget: 5, tailBreachLimit: 2);

        writer.EnqueueReconcile(logId, Reader(logId, 50));
        writer.EnqueueViewRequest(ViewRequests.For(s_context, s_emptyFilter, [logId], Predicate));

        Assert.True(buildEntered.Wait(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken));
        writer.EnqueueReconcile(logId, Reader(logId, 200));
        buildRelease.Release();

        Assert.True(buildEntered.Wait(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken));
        writer.EnqueueReconcile(logId, Reader(logId, 400));
        buildRelease.Release();

        Assert.True(buildEntered.Wait(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken));
        writer.EnqueueReconcile(logId, Reader(logId, 600));
        buildRelease.Release();

        OrderedViewSnapshot snapshot = await writer.DrainAsync();

        Assert.Null(writer.Faulted);
        Assert.Equal(600, snapshot.Count);
        Assert.Equal(3, writer.BuildsStarted);
        return;

        bool Predicate(EventLocator locator, IEventColumnReader columnReader)
        {
            if (locator.Index == 0)
            {
                buildEntered.Release();
                buildRelease.Wait(OrderedViewTestTimeouts.Default);
            }

            return true;
        }
    }

    private static IEventColumnReader Reader(EventLogId logId, int count)
    {
        var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var events = new List<ResolvedEvent>(count);

        for (int index = 0; index < count; index++)
        {
            events.Add(new ResolvedEvent("Log", LogPathType.Channel)
            {
                RecordId = index,
                TimeCreated = clock.AddMilliseconds(index),
                Id = 1000,
                Level = "Information",
                Source = "Provider.A",
                LogName = "Channel"
            });
        }

        return EventColumnStore.Build(events, generation: 0, contentVersion: 0).CreateReader(logId);
    }
}
