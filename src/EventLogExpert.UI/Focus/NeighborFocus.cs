// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.UI.Focus;

internal static class NeighborFocus
{
    public static IReadOnlyList<T> GetFallbackNeighbors<T>(IReadOnlyList<T> itemsBeforeRemoval, int removedIndex)
    {
        ArgumentNullException.ThrowIfNull(itemsBeforeRemoval);

        return [.. FallbackNeighbors(itemsBeforeRemoval, removedIndex)];
    }

    public static bool TryGetNeighborAfterRemove<T>(
        IReadOnlyList<T> itemsBeforeRemoval,
        int removedIndex,
        Func<T, bool> isFocusable,
        [MaybeNullWhen(false)] out T neighbor)
    {
        ArgumentNullException.ThrowIfNull(itemsBeforeRemoval);
        ArgumentNullException.ThrowIfNull(isFocusable);

        foreach (var candidate in FallbackNeighbors(itemsBeforeRemoval, removedIndex))
        {
            if (isFocusable(candidate))
            {
                neighbor = candidate;

                return true;
            }
        }

        neighbor = default;

        return false;
    }

    private static IEnumerable<T> FallbackNeighbors<T>(IReadOnlyList<T> itemsBeforeRemoval, int removedIndex)
    {
        int clampedIndex = Math.Clamp(removedIndex, -1, itemsBeforeRemoval.Count);

        for (int forward = clampedIndex + 1; forward < itemsBeforeRemoval.Count; forward++)
        {
            yield return itemsBeforeRemoval[forward];
        }

        for (int backward = clampedIndex - 1; backward >= 0; backward--)
        {
            yield return itemsBeforeRemoval[backward];
        }
    }
}
