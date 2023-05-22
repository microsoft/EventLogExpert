// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.Store.StatusBar;

public class StatusBarReducers
{
    [ReducerMethod]
    public static StatusBarState ReduceSetEventsLoaded(StatusBarState state, StatusBarAction.SetEventsLoaded action) =>
        new() { EventsLoaded = action.EventCount, ResolverStatus = state.ResolverStatus };

    [ReducerMethod]
    public static StatusBarState ReduceSetResolverStatus(StatusBarState state, StatusBarAction.SetResolverStatus action) =>
        new() { EventsLoaded = state.EventsLoaded, ResolverStatus = action.ResolverStatus };
}
