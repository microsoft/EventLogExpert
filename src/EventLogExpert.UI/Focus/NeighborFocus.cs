// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.UI.Focus;

internal static class NeighborFocus
{
    // Neighbor to focus after removing the item at removedIndex: walk forward then backward for the first isFocusable
    // item, over the PRE-removal list so the identity survives the re-render. Try pattern so value-type keys avoid the
    // default(T)-wraps-to-non-null-Nullable trap; removedIndex out of range is clamped.
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
