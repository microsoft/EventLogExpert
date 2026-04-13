// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.StatusBar;

public sealed class StatusBarReducers
{
    [ReducerMethod]
    public static StatusBarState ReduceClearStatus(StatusBarState state, StatusBarAction.ClearStatus action)
    {
        var updatedState = state with { };

        if (state.EventsLoading.ContainsKey(action.ActivityId))
        {
            updatedState = updatedState with { EventsLoading = updatedState.EventsLoading.Remove(action.ActivityId) };
        }

        return updatedState;
    }

    [ReducerMethod(typeof(StatusBarAction.CloseAll))]
    public static StatusBarState ReduceCloseAll(StatusBarState state) => new();

    [ReducerMethod]
    public static StatusBarState ReduceSetEventsLoading(StatusBarState state, StatusBarAction.SetEventsLoading action)
    {
        var newLoading = CommonLoadingReducer(state.EventsLoading, action.ActivityId, action.Count, action.FailedCount);

        return ReferenceEquals(newLoading, state.EventsLoading) ? state : state with { EventsLoading = newLoading };
    }

    [ReducerMethod]
    public static StatusBarState
        ReduceSetResolverStatus(StatusBarState state, StatusBarAction.SetResolverStatus action) =>
        new() { ResolverStatus = action.ResolverStatus };

    private static ImmutableDictionary<Guid, (int, int)> CommonLoadingReducer(
        ImmutableDictionary<Guid, (int, int)> loadingEntries,
        Guid activityId,
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
