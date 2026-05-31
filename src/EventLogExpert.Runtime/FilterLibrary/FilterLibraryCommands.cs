// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.Runtime.FilterLibrary;

internal sealed class FilterLibraryCommands(IDispatcher dispatcher) : IFilterLibraryCommands
{
    public void LoadLibrary() => dispatcher.Dispatch(new LoadLibraryAction());

    public void AddEntry(LibraryEntry entry) => dispatcher.Dispatch(new AddLibraryEntryAction(entry));

    public void UpdateEntry(LibraryEntry entry) => dispatcher.Dispatch(new UpdateLibraryEntryAction(entry));

    public void DeleteEntry(string entryId) => dispatcher.Dispatch(new DeleteLibraryEntryAction(entryId));

    public void ApplyEntry(string entryId) => dispatcher.Dispatch(new ApplyLibraryEntryAction(entryId));
}
