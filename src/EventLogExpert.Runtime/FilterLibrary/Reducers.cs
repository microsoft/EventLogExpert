// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.Runtime.FilterLibrary;

internal sealed class Reducers
{
    [ReducerMethod]
    public static FilterLibraryState ReduceAddLibraryEntrySuccess(
        FilterLibraryState state,
        AddLibraryEntrySuccessAction action)
    {
        // Idempotent: if id already present, replace (newer wins). Defensive against
        // RecordFilterApplied collision path where two concurrent effects may both dispatch
        // AddLibraryEntrySuccessAction for the same row (one as the original insert,
        // one as the collision-bumped entry). Without this, the second dispatch would
        // append a duplicate row.
        int existingIndex = state.Entries.FindIndex(entry => entry.Id == action.Entry.Id);

        if (existingIndex >= 0)
        {
            return state with { Entries = state.Entries.SetItem(existingIndex, action.Entry) };
        }

        return state with { Entries = state.Entries.Add(action.Entry) };
    }

    [ReducerMethod]
    public static FilterLibraryState ReduceDeleteLibraryEntrySuccess(
        FilterLibraryState state,
        DeleteLibraryEntrySuccessAction action)
    {
        int index = state.Entries.FindIndex(entry => entry.Id == action.EntryId);

        return index < 0 ? state : state with { Entries = state.Entries.RemoveAt(index) };
    }

    [ReducerMethod(typeof(LoadLibraryFailureAction))]
    public static FilterLibraryState ReduceLoadLibraryFailure(FilterLibraryState state) =>
        state with { Entries = [], IsLoaded = true, IsLoading = false, LoadError = true };

    [ReducerMethod(typeof(LoadLibraryStartedAction))]
    public static FilterLibraryState ReduceLoadLibraryStarted(FilterLibraryState state) =>
        state with { IsLoading = true };

    [ReducerMethod]
    public static FilterLibraryState ReduceLoadLibrarySuccess(
        FilterLibraryState state,
        LoadLibrarySuccessAction action) =>
        state with { Entries = action.Entries, IsLoaded = true, IsLoading = false, LoadError = false };

    [ReducerMethod]
    public static FilterLibraryState ReduceUpdateLibraryEntrySuccess(
        FilterLibraryState state,
        UpdateLibraryEntrySuccessAction action)
    {
        int index = state.Entries.FindIndex(entry => entry.Id == action.Entry.Id);

        return index < 0 ? state : state with { Entries = state.Entries.SetItem(index, action.Entry) };
    }
}
