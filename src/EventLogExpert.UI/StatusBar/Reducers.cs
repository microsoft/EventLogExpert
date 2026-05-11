// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.StatusBar;

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

    [ReducerMethod(typeof(CloseAllAction))]
    public static StatusBarState ReduceCloseAll(StatusBarState state) => new();

    [ReducerMethod]
    public static StatusBarState ReduceSetEventsLoading(StatusBarState state, SetEventsLoadingAction action)
    {
        var newLoading = CommonLoadingReducer(state.EventsLoading, action.ActivityId, action.Count, action.FailedCount);

        return ReferenceEquals(newLoading, state.EventsLoading) ? state : state with { EventsLoading = newLoading };
    }

    [ReducerMethod]
    public static StatusBarState
        ReduceSetResolverStatus(StatusBarState state, SetResolverStatusAction action) =>
        new() { ResolverStatus = action.ResolverStatus };

    private static ImmutableDictionary<StatusActivityId, (int, int)> CommonLoadingReducer(
        ImmutableDictionary<StatusActivityId, (int, int)> loadingEntries,
        StatusActivityId activityId,
        int count,
        int failedCount)
    {
        if (loadingEntries.TryGetValue(activityId, out var existing) && existing == (count, failedCount))
        {
            return loadingEntries;
        }

        var updated = loadingEntries.Remove(activityId);

        return count == 0 ? updated : updated.Add(activityId, (count, failedCount));
    }
}
