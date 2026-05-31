// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.FilterLibrary;

public interface IFilterLibraryStore
{
    /// <summary>Inserts a new library entry into the persistent store.</summary>
    void Add(LibraryEntry entry);

    /// <summary>Removes the library entry with <paramref name="entryId" /> from the persistent store.</summary>
    void Delete(string entryId);

    /// <summary>
    ///     Reads all library entries from the persistent store. Throws on connection/schema failures (FilterLibrary
    ///     Effects catch and dispatch failure action).
    /// </summary>
    IReadOnlyList<LibraryEntry> LoadAll();

    /// <summary>Updates an existing library entry in the persistent store (matched by <see cref="LibraryEntry.Id" />).</summary>
    void Update(LibraryEntry entry);
}
