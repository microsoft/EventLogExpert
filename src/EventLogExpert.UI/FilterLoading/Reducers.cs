// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.StatusBar;
using Fluxor;

namespace EventLogExpert.UI.FilterLoading;

public sealed class Reducers
{
    [ReducerMethod(typeof(CloseAllAction))]
    public static FilterLoadingState ReduceCloseAll(FilterLoadingState state) =>
        state.IsLoading ? new FilterLoadingState() : state;

    [ReducerMethod]
    public static FilterLoadingState ReduceSetFilterLoading(
        FilterLoadingState state,
        SetFilterLoadingAction action) =>
        state.IsLoading == action.IsLoading ? state : new FilterLoadingState
        {
            IsLoading = action.IsLoading
        };
}
