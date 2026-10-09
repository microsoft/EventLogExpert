// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.UI.Focus;

internal static class NeighborFocus
{
    // Picks the neighbor to focus after the item at removedIndex is removed from a list: walk forward from the next
    // item, then backward from the previous, returning the first that isFocusable. Operates on the PRE-removal list so
    // the returned identity stays valid for the caller's ref lookup across the re-render. Try pattern so value-type
    // keys (e.g. FilterLensId) avoid the default(T)-wraps-to-a-non-null-Nullable trap - the bool gates usage, not the
    // out value. removedIndex out of range is tolerated (both walks clamp into range).
    public static bool TryGetNeighborAfterRemove<T>(
        IReadOnlyList<T> itemsBeforeRemoval,
        int removedIndex,
        Func<T, bool> isFocusable,
        [MaybeNullWhen(false)] out T neighbor)
    {
        ArgumentNullException.ThrowIfNull(itemsBeforeRemoval);
        ArgumentNullException.ThrowIfNull(isFocusable);

        for (int forward = Math.Max(removedIndex + 1, 0); forward < itemsBeforeRemoval.Count; forward++)
        {
            if (isFocusable(itemsBeforeRemoval[forward]))
            {
                neighbor = itemsBeforeRemoval[forward];

                return true;
            }
        }

        for (int backward = Math.Min(removedIndex - 1, itemsBeforeRemoval.Count - 1); backward >= 0; backward--)
        {
            if (isFocusable(itemsBeforeRemoval[backward]))
            {
                neighbor = itemsBeforeRemoval[backward];

                return true;
            }
        }

        neighbor = default;

        return false;
    }
}
