// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.FilterLibrary;

public interface IFilterLibraryStore
{
    /// <summary>Inserts a new library entry into the persistent store.</summary>
    void Add(LibraryEntry entry);

    /// <summary>
    ///     Inserts <paramref name="candidate" /> only if no AutoTracked filter entry with the same
    ///     (lower(ComparisonText), Mode, IsExcluded) tuple already exists. Returns <c>(candidate, true)</c> when the row was
    ///     newly persisted, or <c>(existing, false)</c> when a same-tuple AutoTracked filter row was already present. The
    ///     returned entry's <see cref="LibraryEntry.Id" /> may differ from the candidate's Id when collision occurs.
    /// </summary>
    (LibraryEntry Entry, bool WasInserted) AddOrReturnExistingFilter(LibraryEntrySavedFilter candidate);

    /// <summary>
    ///     Inserts multiple library entries into the persistent store. The default implementation calls
    ///     <see cref="Add" /> per entry; production implementations should override with a transactional batch.
    /// </summary>
    void AddRange(IEnumerable<LibraryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        foreach (var entry in entries) { Add(entry); }
    }

    /// <summary>Removes the library entry with <paramref name="entryId" /> from the persistent store.</summary>
    void Delete(LibraryEntryId entryId);

    /// <summary>
    ///     Reads all library entries from the persistent store. Throws on connection/schema failures (FilterLibrary
    ///     Effects catch and dispatch failure action).
    /// </summary>
    IReadOnlyList<LibraryEntry> LoadAll();

    /// <summary>
    ///     Bumps <see cref="LibraryEntry.LastUsedUtc" /> on the entry with <paramref name="entryId" /> only when the
    ///     entry exists and is not favorited. Returns <c>true</c> when the bump succeeded; <c>false</c> when no row matched
    ///     (entry missing OR favorited).
    /// </summary>
    bool TryBumpLastUsedIfNotFavorite(LibraryEntryId entryId, DateTimeOffset lastUsedUtc);

    /// <summary>
    ///     Deletes the library entry with <paramref name="entryId" /> ONLY when the row is still an AutoTracked filter
    ///     AND is not favorited AND has a non-null <see cref="LibraryEntry.LastUsedUtc" /> at the moment of the delete.
    ///     Returns <c>true</c> when the delete affected a row; <c>false</c> when no row matched (entry missing, promoted to
    ///     UserSaved, favorited, or LastUsedUtc cleared since the caller's snapshot was projected). Used by the auto-prune
    ///     path to avoid losing data when a concurrent <c>SetIsFavorite</c> / <c>SaveEntry</c> has changed the row's
    ///     classification after the snapshot was taken.
    /// </summary>
    bool TryDeleteAutoTrackedIfNotFavorite(LibraryEntryId entryId);

    /// <summary>Updates an existing library entry in the persistent store (matched by <see cref="LibraryEntry.Id" />).</summary>
    void Update(LibraryEntry entry);
}
