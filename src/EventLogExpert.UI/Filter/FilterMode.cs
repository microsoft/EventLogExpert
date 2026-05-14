// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Filter;

/// <summary>
///     Authoring mode for a <see cref="FilterDraft" /> / <see cref="SavedFilter" />. Drives which editor body the row
///     renders (structured Basic editor, free-text Advanced input, or a Cached-filter dropdown) and is persisted so
///     re-edit reopens the same surface. Cached rows track their cached-entry binding via the row's text equality with
///     <see cref="FilterCache.FilterCacheState.FavoriteFilters" /> / <see cref="FilterCache.FilterCacheState.RecentFilters" />.
/// </summary>
public enum FilterMode
{
    /// <summary>Free-form expression text. Default authoring mode.</summary>
    Advanced,

    /// <summary>Structured Property/Operator/Value editor with optional sub-filters.</summary>
    Basic,

    /// <summary>Inline picker over the user's favorite + recent cached filter strings.</summary>
    Cached
}
