// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.FilterLibrary;

public interface IFilterLibraryCommands
{
    /// <summary>Adds <paramref name="entry" /> to the library and persists.</summary>
    void AddEntry(LibraryEntry entry);

    /// <summary>Applies the library entry's filters to the FilterPane (replacing existing filters).</summary>
    void ApplyEntry(string entryId);

    /// <summary>Removes the library entry with <paramref name="entryId" /> and persists.</summary>
    void DeleteEntry(string entryId);

    /// <summary>Loads persisted library entries from the store into the FilterLibrary state.</summary>
    void LoadLibrary();

    /// <summary>Replaces an existing library entry (matched by <see cref="LibraryEntry.Id" />) and persists.</summary>
    void UpdateEntry(LibraryEntry entry);
}
