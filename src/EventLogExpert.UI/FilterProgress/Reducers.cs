// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Common.Lifecycle;
using Fluxor;

namespace EventLogExpert.UI.FilterProgress;

public sealed class Reducers
{
    [ReducerMethod(typeof(CloseAllLogsAction))]
    public static FilterProgressState ReduceCloseAll(FilterProgressState state) =>
        state.IsLoading ? new FilterProgressState() : state;

    [ReducerMethod]
    public static FilterProgressState ReduceSetFilterProgress(
        FilterProgressState state,
        SetFilterProgressAction action) =>
        state.IsLoading == action.IsLoading ? state : new FilterProgressState
        {
            IsLoading = action.IsLoading
        };
}
