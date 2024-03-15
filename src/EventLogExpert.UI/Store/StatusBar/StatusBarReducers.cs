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
        return state with
        {
            EventsLoading = CommonLoadingReducer(state.EventsLoading, action.ActivityId, action.Count)
        };
    }

    [ReducerMethod]
    public static StatusBarState
        ReduceSetResolverStatus(StatusBarState state, StatusBarAction.SetResolverStatus action) =>
        new() { ResolverStatus = action.ResolverStatus };

    private static ImmutableDictionary<Guid, int> CommonLoadingReducer(
        ImmutableDictionary<Guid, int> loadingEntries,
        Guid activityId,
        int count)
    {
        if (loadingEntries.ContainsKey(activityId))
        {
            loadingEntries = loadingEntries.Remove(activityId);
        }

        return count == 0 ? loadingEntries : loadingEntries.Add(activityId, count);
    }
}
