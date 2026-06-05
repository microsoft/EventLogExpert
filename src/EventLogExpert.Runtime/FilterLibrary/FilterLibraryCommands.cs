// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterLibrary;

internal sealed class FilterLibraryCommands(IDispatcher dispatcher) : IFilterLibraryCommands
{
    public void AddEntry(LibraryEntry entry) => dispatcher.Dispatch(new AddLibraryEntryAction(entry));

    public void AddFilterToExistingFilterSet(LibraryEntryId filterSetId, SavedFilter filter, LibraryEntryId? sourceEntryId) =>
        dispatcher.Dispatch(new AddFilterToExistingFilterSetAction(filterSetId, filter, sourceEntryId));

    public void AddFilterToNewFilterSet(string newFilterSetName, SavedFilter filter, LibraryEntryId? sourceEntryId) =>
        dispatcher.Dispatch(new AddFilterToNewFilterSetAction(newFilterSetName, filter, sourceEntryId));

    public void ApplyEntry(LibraryEntryId entryId) => dispatcher.Dispatch(new ApplyLibraryEntryAction(entryId));

    public void DeleteEntry(LibraryEntryId entryId) => dispatcher.Dispatch(new DeleteLibraryEntryAction(entryId));

    public void DeleteTag(string name) => dispatcher.Dispatch(new DeleteTagAction(name));

    public void LoadLibrary() => dispatcher.Dispatch(new LoadLibraryAction());

    public void RecordFilterApplied(SavedFilter filter) => dispatcher.Dispatch(new RecordFilterAppliedAction(filter));

    public void RenameTag(string oldName, string newName) =>
        dispatcher.Dispatch(new RenameTagAction(oldName, newName));

    public void ReplaceWithEntry(LibraryEntryId entryId) => dispatcher.Dispatch(new ReplaceWithLibraryEntryAction(entryId));

    public void SaveEntry(LibraryEntryId entryId) => dispatcher.Dispatch(new SaveEntryAction(entryId));

    public void SaveFilterSet(string name, ImmutableList<SavedFilter> filters) =>
        dispatcher.Dispatch(new SaveFilterSetAction(name, filters));

    public void SavePaneAsFilterSet(string name) => dispatcher.Dispatch(new SavePaneAsFilterSetAction(name));

    public void SetIsFavorite(LibraryEntryId entryId, bool isFavorite) =>
        dispatcher.Dispatch(new SetIsFavoriteAction(entryId, isFavorite));

    public void UpdateEntry(LibraryEntry entry) => dispatcher.Dispatch(new UpdateLibraryEntryAction(entry));
}
