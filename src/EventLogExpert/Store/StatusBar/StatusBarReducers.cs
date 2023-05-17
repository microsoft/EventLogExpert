// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.Store.StatusBar;

public class StatusBarReducers
{
    [ReducerMethod]
    public static StatusBarState ReduceSetEventsLoaded(StatusBarState state, StatusBarAction.SetEventsLoaded action) =>
        new() { EventsLoaded = action.EventCount };
}
