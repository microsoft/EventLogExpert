// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterLibrary;

public interface IFilterLibraryCommands
{
    /// <summary>Adds <paramref name="entry" /> to the library and persists.</summary>
    void AddEntry(LibraryEntry entry);

    /// <summary>
    ///     Appends <paramref name="filter" /> to an existing filter set (matched by id). Dedupes by (case-insensitive
    ///     ComparisonText, Mode, IsExcluded), matching the FilterLibrary store invariant.
    /// </summary>
    void AddFilterToExistingFilterSet(LibraryEntryId filterSetId, SavedFilter filter, LibraryEntryId? sourceEntryId);

    /// <summary>Creates a new filter set containing only <paramref name="filter" />.</summary>
    void AddFilterToNewFilterSet(string newFilterSetName, SavedFilter filter, LibraryEntryId? sourceEntryId);

    /// <summary>
    ///     Additively merges the library entry's filters into the current FilterPane. Dedupes by (case-insensitive
    ///     ComparisonText, Mode, IsExcluded).
    /// </summary>
    void ApplyEntry(LibraryEntryId entryId);

    /// <summary>Removes the library entry with <paramref name="entryId" /> and persists.</summary>
    void DeleteEntry(LibraryEntryId entryId);

    /// <summary>
    ///     Removes <paramref name="name" /> from every library entry that carries it (case-insensitive match after
    ///     <see cref="LibraryEntryTagNormalizer" /> normalization).
    /// </summary>
    void DeleteTag(string name);

    /// <summary>Loads persisted library entries from the store into the FilterLibrary state.</summary>
    void LoadLibrary();

    /// <summary>Auto-tracks a filter that was just applied via FilterPane (or bumps LastUsedUtc on an existing match).</summary>
    void RecordFilterApplied(SavedFilter filter);

    /// <summary>
    ///     Renames a tag across every library entry that carries it. Both names are normalized via
    ///     <see cref="LibraryEntryTagNormalizer" />. Entries containing both old and new tags retain only the canonical new
    ///     tag (dedup). No-op when normalized old and new are equal.
    /// </summary>
    void RenameTag(string oldName, string newName);

    /// <summary>Destructively replaces the FilterPane's filter list with the library entry's filters.</summary>
    void ReplaceWithEntry(LibraryEntryId entryId);

    /// <summary>Promotes the library entry's <see cref="LibraryEntry.Origin" /> to UserSaved (no-op if already UserSaved).</summary>
    void SaveEntry(LibraryEntryId entryId);

    /// <summary>Creates a new filter set from the given filter list (regenerates FilterIds for Razor key safety).</summary>
    void SaveFilterSet(string name, ImmutableList<SavedFilter> filters);

    /// <summary>Saves the current FilterPane filters as a new filter set (effect reads pane state).</summary>
    void SavePaneAsFilterSet(string name);

    void SetEntryName(LibraryEntryId entryId, string name);

    void SetEntryTags(LibraryEntryId entryId, ImmutableList<string> tags);

    void SetFilterSetFilters(LibraryEntryId filterSetId, ImmutableList<SavedFilter> filters);

    /// <summary>Sets the favorite flag on the library entry (applies mutex + Origin promotion).</summary>
    void SetIsFavorite(LibraryEntryId entryId, bool isFavorite);

    /// <summary>Replaces an existing library entry (matched by <see cref="LibraryEntry.Id" />) and persists.</summary>
    void UpdateEntry(LibraryEntry entry);
}
