// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.LogTable;
using NSubstitute;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Runtime.Tests.EventLog;

public sealed class PartialLoadCoordinatorTests
{
    [Fact]
    public void Discard_RemovesBufferAndAllowsReopen()
    {
        var (coordinator, dispatched) = Create();
        var logId = EventLogId.Create();

        coordinator.Enqueue(logId, [NewEvent(1)], 0);
        coordinator.Enqueue(logId, [NewEvent(2)], 0);
        dispatched.Clear();

        coordinator.Discard(logId);
        coordinator.Flush();
        Assert.Empty(dispatched);

        coordinator.Enqueue(logId, [NewEvent(3)], 0);
        Assert.Single(dispatched);
    }

    [Fact]
    public void DiscardAll_ClearsAllBuffers()
    {
        var (coordinator, dispatched) = Create();
        var first = EventLogId.Create();
        var second = EventLogId.Create();

        coordinator.Enqueue(first, [NewEvent(1)], 0);
        coordinator.Enqueue(second, [NewEvent(2)], 0);
        coordinator.Enqueue(first, [NewEvent(3)], 0);
        coordinator.Enqueue(second, [NewEvent(4)], 0);
        dispatched.Clear();

        coordinator.DiscardAll();
        coordinator.Flush();

        Assert.Empty(dispatched);
    }

    [Fact]
    public void Dispose_ThenEnqueueAndFlush_IsSilent()
    {
        var (coordinator, dispatched) = Create();

        coordinator.Dispose();
        coordinator.Enqueue(EventLogId.Create(), [NewEvent(1)], 0);
        coordinator.Flush();

        Assert.Empty(dispatched);
    }

    [Fact]
    public void Enqueue_BufferStraddlingVersions_FlushesMinVersion()
    {
        var (coordinator, dispatched) = Create();
        var logId = EventLogId.Create();

        coordinator.Enqueue(logId, [NewEvent(1)], 5);
        dispatched.Clear();

        coordinator.Enqueue(logId, [NewEvent(2)], 3);
        coordinator.Enqueue(logId, [NewEvent(3)], 8);
        coordinator.Flush();

        var batch = Assert.IsType<AppendTableEventsBatchAction>(Assert.Single(dispatched));
        Assert.Equal(3, batch.VersionByLog[logId]);
    }

    [Fact]
    public void Enqueue_EmptyDelta_DispatchesNothing()
    {
        var (coordinator, dispatched) = Create();

        coordinator.Enqueue(EventLogId.Create(), [], 0);

        Assert.Empty(dispatched);
    }

    [Fact]
    public void Enqueue_FirstDeltaForLog_FlushesImmediately()
    {
        var (coordinator, dispatched) = Create();
        var logId = EventLogId.Create();

        coordinator.Enqueue(logId, [NewEvent(1)], 0);

        var batch = Assert.IsType<AppendTableEventsBatchAction>(Assert.Single(dispatched));
        Assert.True(batch.EventsByLog.ContainsKey(logId));
        Assert.Single(batch.EventsByLog[logId]);
    }

    [Fact]
    public void Enqueue_FirstSeen_EmitsVersionByLog()
    {
        var (coordinator, dispatched) = Create();
        var logId = EventLogId.Create();

        coordinator.Enqueue(logId, [NewEvent(1)], 7);

        var batch = Assert.IsType<AppendTableEventsBatchAction>(Assert.Single(dispatched));
        Assert.Equal(7, batch.VersionByLog[logId]);
    }

    [Fact]
    public void Flush_BuffersAcrossLogs_DispatchesOneBatchForAllLogs()
    {
        var (coordinator, dispatched) = Create();
        var first = EventLogId.Create();
        var second = EventLogId.Create();

        // The first delta per log flushes immediately; subsequent deltas buffer for the next flush.
        coordinator.Enqueue(first, [NewEvent(1)], 0);
        coordinator.Enqueue(second, [NewEvent(2)], 0);
        dispatched.Clear();

        coordinator.Enqueue(first, [NewEvent(3)], 0);
        coordinator.Enqueue(second, [NewEvent(4)], 0);
        coordinator.Flush();

        var batch = Assert.IsType<AppendTableEventsBatchAction>(Assert.Single(dispatched));
        Assert.Equal(2, batch.EventsByLog.Count);
    }

    [Fact]
    public void MarkFinalized_DropsAlreadyBufferedDelta()
    {
        var (coordinator, dispatched) = Create();
        var logId = EventLogId.Create();

        coordinator.Enqueue(logId, [NewEvent(1)], 0); // first-seen: flushes immediately and clears the buffer
        coordinator.Enqueue(logId, [NewEvent(2)], 0); // already seen: buffered, not yet flushed
        dispatched.Clear();

        coordinator.MarkFinalized(logId);
        coordinator.Flush();

        Assert.Empty(dispatched);
    }

    [Fact]
    public void MarkFinalized_ThenLateEnqueue_DropsTheLateEvents()
    {
        var (coordinator, dispatched) = Create();
        var logId = EventLogId.Create();

        coordinator.Enqueue(logId, [NewEvent(1)], 0);
        dispatched.Clear();

        coordinator.MarkFinalized(logId);
        coordinator.Enqueue(logId, [NewEvent(2)], 0);
        coordinator.Flush();

        Assert.Empty(dispatched);
    }

    private static (PartialLoadCoordinator Coordinator, List<object> Dispatched) Create()
    {
        var dispatched = new List<object>();
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.When(d => d.Dispatch(Arg.Any<object>())).Do(call => dispatched.Add(call.Arg<object>()));

        return (new PartialLoadCoordinator(dispatcher, Timeout.InfiniteTimeSpan), dispatched);
    }

    private static ResolvedEvent NewEvent(long recordId) =>
        new("Application", LogPathType.Channel) { RecordId = recordId };
}
