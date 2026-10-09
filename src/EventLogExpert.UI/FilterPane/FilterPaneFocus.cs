// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.UI.Focus;

namespace EventLogExpert.UI.FilterPane;

/// <summary>
///     Pure helpers for picking the next filter row to focus after a remove or pending-discard event. Extracted from
///     <see cref="FilterPane" /> so the logic is unit-testable without needing <c>InternalsVisibleTo</c> seams on the
///     component itself. Callers supply an <c>isFocusable</c> predicate that decides whether a candidate row can accept
///     focus (typically: a live row whose component reference exists and is not mid-edit).
/// </summary>
public static class FilterPaneFocus
{
    /// <summary>
    ///     Picks the row to focus after a pending draft is discarded. Walks backward from the last saved filter. Returns
    ///     <see langword="null" /> when no focusable row exists.
    /// </summary>
    public static FilterId? ComputeFocusTargetAfterPendingDiscard(
        IReadOnlyList<SavedFilter> savedFilters,
        Func<FilterId, bool> isFocusable)
    {
        ArgumentNullException.ThrowIfNull(savedFilters);
        ArgumentNullException.ThrowIfNull(isFocusable);

        // Pass removedIndex = Count so the forward walk no-ops and the backward walk starts at the last saved filter -
        // the pending-discard "walk backward from the end" semantics expressed through the shared neighbor helper.
        return NeighborFocus.TryGetNeighborAfterRemove(
            savedFilters, savedFilters.Count, saved => isFocusable(saved.Id), out var target) ?
                target.Id : null;
    }

    /// <summary>
    ///     Picks the row to focus after a saved filter is removed. Walks forward from the removed index first, then
    ///     backward. Returns <see langword="null" /> when no focusable row exists (caller falls back to the Add-Filter
    ///     button).
    /// </summary>
    public static FilterId? ComputeFocusTargetAfterRemove(
        IReadOnlyList<SavedFilter> savedFilters,
        FilterId removedId,
        Func<FilterId, bool> isFocusable)
    {
        ArgumentNullException.ThrowIfNull(savedFilters);
        ArgumentNullException.ThrowIfNull(isFocusable);

        int removedIndex = -1;

        for (int i = 0; i < savedFilters.Count; i++)
        {
            if (savedFilters[i].Id == removedId)
            {
                removedIndex = i;

                break;
            }
        }

        if (removedIndex < 0) { return null; }

        return NeighborFocus.TryGetNeighborAfterRemove(
            savedFilters,
            removedIndex,
            saved => isFocusable(saved.Id),
            out var target) ?
            target.Id : null;
    }
}

