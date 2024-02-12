// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.StatusBar;

public sealed class StatusBarReducers
{
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

    [ReducerMethod]
    public static StatusBarState ReduceSetXmlLoading(StatusBarState state, StatusBarAction.SetXmlLoading action)
    {
        return state with { XmlLoading = CommonLoadingReducer(state.XmlLoading, action.ActivityId, action.Count) };
    }

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
