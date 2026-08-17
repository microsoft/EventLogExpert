// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.Memory;
using System.Collections.Immutable;
using CloseLogAction = EventLogExpert.Runtime.LogTable.CloseLogAction;

namespace EventLogExpert.Runtime.Tests.Memory;

public sealed class MemoryGovernorReducerTests
{
    private static readonly EventLogId LogA = EventLogId.Create();
    private static readonly EventLogId LogB = EventLogId.Create();
    private static readonly EventLogData LogDataA = new("A", LogPathType.Channel) { Id = LogA };

    [Fact]
    public void MarkThenUnflaggedLoadEvents_LeavesLogUnmarked()
    {
        var afterMark = MemoryGovernorReducer.ReduceMarkPartiallyLoaded(
            new MemoryGovernorState(),
            new MarkPartiallyLoadedForMemoryAction(LogA));

        var afterReload = MemoryGovernorReducer.ReduceLoadEvents(afterMark, new LoadEventsAction(LogDataA, []));

        Assert.DoesNotContain(LogA, afterReload.PartiallyLoadedForMemory);
    }

    [Fact]
    public void ReduceCloseAll_ClearsSet()
    {
        var state = new MemoryGovernorState { PartiallyLoadedForMemory = ImmutableHashSet.Create(LogA, LogB) };

        Assert.Empty(MemoryGovernorReducer.ReduceCloseAll(state).PartiallyLoadedForMemory);
    }

    [Fact]
    public void ReduceCloseLog_RemovesMarkedLog()
    {
        var state = new MemoryGovernorState { PartiallyLoadedForMemory = ImmutableHashSet.Create(LogA, LogB) };

        var next = MemoryGovernorReducer.ReduceCloseLog(state, new CloseLogAction(LogA));

        Assert.DoesNotContain(LogA, next.PartiallyLoadedForMemory);
        Assert.Contains(LogB, next.PartiallyLoadedForMemory);
    }

    [Fact]
    public void ReduceInitialized_SetsBaselineBudgetAndSeedsCurrentToBaseline()
    {
        var next = MemoryGovernorReducer.ReduceInitialized(
            new MemoryGovernorState(),
            new MemoryGovernorInitializedAction(BaselineBytes: 500, BudgetBytes: 900));

        Assert.Equal(500, next.BaselineBytes);
        Assert.Equal(900, next.BudgetBytes);
        Assert.Equal(500, next.CurrentBytes);
        Assert.Equal(MemoryPressureLevel.Normal, next.Level);
    }

    [Fact]
    public void ReduceLoadEvents_RemovesMarkedLog_FlagAgnostic()
    {
        var state = new MemoryGovernorState { PartiallyLoadedForMemory = ImmutableHashSet.Create(LogA) };

        var next = MemoryGovernorReducer.ReduceLoadEvents(state, new LoadEventsAction(LogDataA, [], StoreAlreadyBuilt: false));

        Assert.DoesNotContain(LogA, next.PartiallyLoadedForMemory);
    }

    [Fact]
    public void ReduceMarkPartiallyLoaded_AddsLogId()
    {
        var next = MemoryGovernorReducer.ReduceMarkPartiallyLoaded(
            new MemoryGovernorState(),
            new MarkPartiallyLoadedForMemoryAction(LogA));

        Assert.Contains(LogA, next.PartiallyLoadedForMemory);
    }

    [Fact]
    public void ReduceMarkPartiallyLoaded_AlreadyMarked_ReturnsSameInstance()
    {
        var state = new MemoryGovernorState { PartiallyLoadedForMemory = ImmutableHashSet.Create(LogA) };

        Assert.Same(state, MemoryGovernorReducer.ReduceMarkPartiallyLoaded(state, new MarkPartiallyLoadedForMemoryAction(LogA)));
    }

    [Fact]
    public void ReduceRecomputed_AppliesLevelBytesAndRemovesStaleIds()
    {
        var next = MemoryGovernorReducer.ReduceRecomputed(
            new MemoryGovernorState { PartiallyLoadedForMemory = ImmutableHashSet.Create(LogA, LogB) },
            new MemoryGovernorRecomputedAction(
                MemoryPressureLevel.Paused,
                CurrentBytes: 800,
                BudgetBytes: 900,
                ImmutableHashSet.Create(LogB)));

        Assert.Equal(MemoryPressureLevel.Paused, next.Level);
        Assert.Equal(800, next.CurrentBytes);
        Assert.Equal(900, next.BudgetBytes);
        Assert.Contains(LogA, next.PartiallyLoadedForMemory);
        Assert.DoesNotContain(LogB, next.PartiallyLoadedForMemory);
    }

    [Fact]
    public void ReduceRecomputed_WithNoStaleIds_PreservesConcurrentlyAddedMarker()
    {
        var state = new MemoryGovernorState { PartiallyLoadedForMemory = ImmutableHashSet.Create(LogA) };

        var next = MemoryGovernorReducer.ReduceRecomputed(
            state,
            new MemoryGovernorRecomputedAction(
                MemoryPressureLevel.Normal,
                CurrentBytes: 1,
                BudgetBytes: 2,
                ImmutableHashSet<EventLogId>.Empty));

        Assert.Contains(LogA, next.PartiallyLoadedForMemory);
    }
}
