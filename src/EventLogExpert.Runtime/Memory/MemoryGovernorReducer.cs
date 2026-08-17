// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Runtime.EventLog;
using Fluxor;
using System.Collections.Immutable;
using CloseLogAction = EventLogExpert.Runtime.LogTable.CloseLogAction;

namespace EventLogExpert.Runtime.Memory;

internal sealed class MemoryGovernorReducer
{
    [ReducerMethod(typeof(CloseAllLogsAction))]
    public static MemoryGovernorState ReduceCloseAll(MemoryGovernorState state) =>
        state.PartiallyLoadedForMemory.IsEmpty ?
            state :
            state with { PartiallyLoadedForMemory = ImmutableHashSet<EventLogId>.Empty };

    [ReducerMethod]
    public static MemoryGovernorState ReduceCloseLog(MemoryGovernorState state, CloseLogAction action) =>
        state.PartiallyLoadedForMemory.Contains(action.LogId) ?
            state with { PartiallyLoadedForMemory = state.PartiallyLoadedForMemory.Remove(action.LogId) } :
            state;

    [ReducerMethod]
    public static MemoryGovernorState ReduceInitialized(
        MemoryGovernorState state,
        MemoryGovernorInitializedAction action) =>
        state with
        {
            BaselineBytes = action.BaselineBytes,
            BudgetBytes = action.BudgetBytes,
            CurrentBytes = action.BaselineBytes,
            Level = MemoryPressureLevel.Normal
        };

    [ReducerMethod]
    public static MemoryGovernorState ReduceLoadEvents(MemoryGovernorState state, LoadEventsAction action) =>
        state.PartiallyLoadedForMemory.Contains(action.LogData.Id) ?
            state with { PartiallyLoadedForMemory = state.PartiallyLoadedForMemory.Remove(action.LogData.Id) } :
            state;

    [ReducerMethod]
    public static MemoryGovernorState ReduceMarkPartiallyLoaded(
        MemoryGovernorState state,
        MarkPartiallyLoadedForMemoryAction action) =>
        state.PartiallyLoadedForMemory.Contains(action.LogId) ?
            state :
            state with { PartiallyLoadedForMemory = state.PartiallyLoadedForMemory.Add(action.LogId) };

    [ReducerMethod]
    public static MemoryGovernorState ReduceRecomputed(
        MemoryGovernorState state,
        MemoryGovernorRecomputedAction action) =>
        state with
        {
            Level = action.Level,
            CurrentBytes = action.CurrentBytes,
            BudgetBytes = action.BudgetBytes,
            PartiallyLoadedForMemory = action.StalePartialLogIds.IsEmpty ?
                state.PartiallyLoadedForMemory :
                state.PartiallyLoadedForMemory.Except(action.StalePartialLogIds)
        };
}
