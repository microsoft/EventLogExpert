// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.UI.Store.StatusBar;

public class StatusBarReducers
{
    [ReducerMethod]
    public static StatusBarState ReduceSetResolverStatus(StatusBarState state, StatusBarAction.SetResolverStatus action) =>
        new() { ResolverStatus = action.ResolverStatus };
}
