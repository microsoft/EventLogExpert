// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.Runtime.FilterLibrary;

internal sealed class Reducers
{
    [ReducerMethod]
    public static FilterLibraryState ReduceAddLibraryEntrySuccess(
        FilterLibraryState state,
        AddLibraryEntrySuccessAction action) =>
        state with { Entries = state.Entries.Add(action.Entry) };

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
        state with { Entries = [], IsLoaded = true, LoadError = true };

    [ReducerMethod]
    public static FilterLibraryState ReduceLoadLibrarySuccess(
        FilterLibraryState state,
        LoadLibrarySuccessAction action) =>
        state with { Entries = action.Entries, IsLoaded = true, LoadError = false };

    [ReducerMethod]
    public static FilterLibraryState ReduceUpdateLibraryEntrySuccess(
        FilterLibraryState state,
        UpdateLibraryEntrySuccessAction action)
    {
        int index = state.Entries.FindIndex(entry => entry.Id == action.Entry.Id);
        return index < 0 ? state : state with { Entries = state.Entries.SetItem(index, action.Entry) };
    }
}
