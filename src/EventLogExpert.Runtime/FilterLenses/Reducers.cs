// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.Runtime.FilterLenses;

internal sealed class Reducers
{
    [ReducerMethod(typeof(ClearFilterLensesAction))]
    public static FilterLensState ReduceClear(FilterLensState state) =>
        state.Lenses.IsEmpty ? state : new FilterLensState();

    [ReducerMethod]
    public static FilterLensState ReducePush(FilterLensState state, PushFilterLensAction action) =>
        state with { Lenses = state.Lenses.Add(action.Lens) };

    [ReducerMethod]
    public static FilterLensState ReduceRemove(FilterLensState state, RemoveFilterLensAction action)
    {
        var updated = state.Lenses.RemoveAll(lens => lens.Id == action.Lens.Id);

        return updated.Count == state.Lenses.Count ? state : state with { Lenses = updated };
    }

    [ReducerMethod]
    public static FilterLensState ReduceRemoveForLog(FilterLensState state, RemoveLensesForLogAction action)
    {
        var updated = state.Lenses.RemoveAll(
            lens => string.Equals(lens.OriginLog, action.LogName, StringComparison.Ordinal));

        return updated.Count == state.Lenses.Count ? state : state with { Lenses = updated };
    }
}
