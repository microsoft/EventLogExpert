// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.FilterCache;

public interface IFilterCacheCommands
{
    /// <summary>Adds <paramref name="filter" /> to the favorites cache (and removes it from recents if present).</summary>
    void AddFavoriteFilter(string filter);

    /// <summary>Merges <paramref name="filters" /> into the favorites cache (de-duped, case-insensitive).</summary>
    void ImportFavorites(IEnumerable<string> filters);

    /// <summary>Loads persisted favorite + recent filters from preferences into the FilterCache store.</summary>
    void LoadFilters();

    /// <summary>Removes <paramref name="filter" /> from the favorites cache (and re-adds it to recents).</summary>
    void RemoveFavoriteFilter(string filter);
}
