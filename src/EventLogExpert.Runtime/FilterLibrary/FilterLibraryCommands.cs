// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterLibrary;

internal sealed class FilterLibraryCommands(IDispatcher dispatcher) : IFilterLibraryCommands
{
    public void AddEntry(LibraryEntry entry) => dispatcher.Dispatch(new AddLibraryEntryAction(entry));

    public void AddFilterToExistingPreset(LibraryEntryId presetId, SavedFilter filter, LibraryEntryId? sourceEntryId) =>
        dispatcher.Dispatch(new AddFilterToExistingPresetAction(presetId, filter, sourceEntryId));

    public void AddFilterToNewPreset(string newPresetName, SavedFilter filter, LibraryEntryId? sourceEntryId) =>
        dispatcher.Dispatch(new AddFilterToNewPresetAction(newPresetName, filter, sourceEntryId));

    public void ApplyEntry(LibraryEntryId entryId) => dispatcher.Dispatch(new ApplyLibraryEntryAction(entryId));

    public void DeleteEntry(LibraryEntryId entryId) => dispatcher.Dispatch(new DeleteLibraryEntryAction(entryId));

    public void LoadLibrary() => dispatcher.Dispatch(new LoadLibraryAction());

    public void RecordFilterApplied(SavedFilter filter) => dispatcher.Dispatch(new RecordFilterAppliedAction(filter));

    public void ReplaceWithEntry(LibraryEntryId entryId) => dispatcher.Dispatch(new ReplaceWithLibraryEntryAction(entryId));

    public void SaveEntry(LibraryEntryId entryId) => dispatcher.Dispatch(new SaveEntryAction(entryId));

    public void SavePaneAsPreset(string name) => dispatcher.Dispatch(new SavePaneAsPresetAction(name));

    public void SavePreset(string name, ImmutableList<SavedFilter> filters) =>
        dispatcher.Dispatch(new SavePresetAction(name, filters));

    public void SetIsFavorite(LibraryEntryId entryId, bool isFavorite) =>
        dispatcher.Dispatch(new SetIsFavoriteAction(entryId, isFavorite));

    public void UpdateEntry(LibraryEntry entry) => dispatcher.Dispatch(new UpdateLibraryEntryAction(entry));
}
