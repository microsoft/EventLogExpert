// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.FilterLibrary;

public interface IFilterLibraryStore
{
    /// <summary>Inserts a new library entry into the persistent store.</summary>
    Task AddAsync(LibraryEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Inserts <paramref name="candidate" /> only if no AutoTracked filter entry with the same
    ///     (lower(ComparisonText), Mode, IsExcluded) tuple already exists. Returns <c>(candidate, true)</c> when the row was
    ///     newly persisted, or <c>(existing, false)</c> when a same-tuple AutoTracked filter row was already present. The
    ///     returned entry's <see cref="LibraryEntry.Id" /> may differ from the candidate's Id when collision occurs.
    /// </summary>
    Task<(LibraryEntry Entry, bool WasInserted)> AddOrReturnExistingFilterAsync(
        LibraryEntrySavedFilter candidate,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Inserts multiple library entries into the persistent store. The default implementation calls
    ///     <see cref="AddAsync" /> per entry; production implementations should override with a transactional batch.
    /// </summary>
    async Task AddRangeAsync(IEnumerable<LibraryEntry> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        foreach (var entry in entries) { await AddAsync(entry, cancellationToken).ConfigureAwait(false); }
    }

    /// <summary>Removes the library entry with <paramref name="entryId" /> from the persistent store.</summary>
    Task DeleteAsync(LibraryEntryId entryId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reads all library entries from the persistent store. Throws on connection/schema failures (FilterLibrary
    ///     Effects catch and dispatch failure action).
    /// </summary>
    Task<IReadOnlyList<LibraryEntry>> LoadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Bumps <see cref="LibraryEntry.LastUsedUtc" /> on the entry with <paramref name="entryId" /> only when the
    ///     entry exists and is not favorited. Returns <c>true</c> when the bump succeeded; <c>false</c> when no row matched
    ///     (entry missing OR favorited).
    /// </summary>
    Task<bool> TryBumpLastUsedIfNotFavoriteAsync(
        LibraryEntryId entryId,
        DateTimeOffset lastUsedUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deletes the library entry with <paramref name="entryId" /> ONLY when the row is still an AutoTracked filter
    ///     AND is not favorited AND has a non-null <see cref="LibraryEntry.LastUsedUtc" /> at the moment of the delete.
    ///     Returns <c>true</c> when the delete affected a row; <c>false</c> when no row matched (entry missing, promoted to
    ///     UserSaved, favorited, or LastUsedUtc cleared since the caller's snapshot was projected). Used by the auto-prune
    ///     path to avoid losing data when a concurrent <c>SetIsFavorite</c> / <c>SaveEntry</c> has changed the row's
    ///     classification after the snapshot was taken.
    /// </summary>
    Task<bool> TryDeleteAutoTrackedIfNotFavoriteAsync(LibraryEntryId entryId, CancellationToken cancellationToken = default);

    /// <summary>Updates an existing library entry in the persistent store (matched by <see cref="LibraryEntry.Id" />).</summary>
    Task UpdateAsync(LibraryEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Updates every entry in <paramref name="entries" /> within a single transaction (each matched by
    ///     <see cref="LibraryEntry.Id" />). Returns the ids of the rows that were actually updated, in input order; an entry
    ///     whose row no longer exists is skipped and its id is omitted from the result. On any failure the transaction is
    ///     rolled back so no partial writes persist, and the exception propagates. Returns an empty list when
    ///     <paramref name="entries" /> is empty.
    /// </summary>
    Task<IReadOnlyList<LibraryEntryId>> UpdateRangeAsync(
        IReadOnlyList<LibraryEntry> entries,
        CancellationToken cancellationToken = default);
}
