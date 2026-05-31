// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;

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

        if (TryPick(savedFilters, savedFilters.Count - 1, -1, isFocusable, out var target)) { return target; }

        return null;
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

        if (TryPick(savedFilters, removedIndex + 1, +1, isFocusable, out var next)) { return next; }

        if (TryPick(savedFilters, removedIndex - 1, -1, isFocusable, out var prev)) { return prev; }

        return null;
    }

    private static bool TryPick(
        IReadOnlyList<SavedFilter> saved,
        int startIndex,
        int step,
        Func<FilterId, bool> isFocusable,
        out FilterId target)
    {
        for (int i = startIndex; i >= 0 && i < saved.Count; i += step)
        {
            var candidateId = saved[i].Id;

            if (isFocusable(candidateId))
            {
                target = candidateId;
                return true;
            }
        }

        target = default;
        return false;
    }
}
