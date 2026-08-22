// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.EventLog;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.StatusBar;

public sealed class Reducers
{
    [ReducerMethod]
    public static StatusBarState ReduceClearStatus(StatusBarState state, ClearStatusAction action)
    {
        var updatedState = state with { };

        if (state.EventsLoading.ContainsKey(action.ActivityId))
        {
            updatedState = updatedState with { EventsLoading = updatedState.EventsLoading.Remove(action.ActivityId) };
        }

        return updatedState;
    }

    [ReducerMethod(typeof(CloseAllLogsAction))]
    public static StatusBarState ReduceCloseAll(StatusBarState state) => new();

    [ReducerMethod]
    public static StatusBarState ReduceSetEventsLoading(StatusBarState state, SetEventsLoadingAction action)
    {
        var newLoading = CommonLoadingReducer(state.EventsLoading, action.ActivityId, action.Count, action.FailedCount);

        return ReferenceEquals(newLoading, state.EventsLoading) ? state : state with { EventsLoading = newLoading };
    }

    [ReducerMethod]
    public static StatusBarState ReduceSetLoadingTotal(StatusBarState state, SetLoadingTotalAction action)
    {
        // Only annotate an activity that is still loading; a probe that finishes after the load cleared must not
        // resurrect a "Loading" entry. Preserving the same reference when unchanged suppresses a spurious StateChanged.
        if (!state.EventsLoading.TryGetValue(action.ActivityId, out var existing) || existing.Total == action.Total)
        {
            return state;
        }

        return state with
        {
            EventsLoading = state.EventsLoading.SetItem(
                action.ActivityId,
                (existing.Loaded, existing.Failed, action.Total))
        };
    }

    [ReducerMethod]
    public static StatusBarState
        ReduceSetResolverStatus(StatusBarState state, SetResolverStatusAction action) =>
        state with { ResolverStatus = action.ResolverStatus };

    private static ImmutableDictionary<StatusActivityId, (int Loaded, int Failed, long? Total)> CommonLoadingReducer(
        ImmutableDictionary<StatusActivityId, (int Loaded, int Failed, long? Total)> loadingEntries,
        StatusActivityId activityId,
        int count,
        int failedCount)
    {
        if (loadingEntries.TryGetValue(activityId, out var existing))
        {
            if (existing.Loaded == count && existing.Failed == failedCount)
            {
                return loadingEntries;
            }

            // Preserve the probe-published Total when only the running counts change.
            return loadingEntries.SetItem(activityId, (count, failedCount, existing.Total));
        }

        return loadingEntries.SetItem(activityId, (count, failedCount, null));
    }
}
