// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.LogTable;

internal static class RawEventStoreStateExtensions
{
    /// <summary>
    ///     Computes the raw (unrounded) bounds across every log in the store: <c>After</c> is the oldest event timestamp
    ///     and <c>Before</c> is the newest. Returns <see langword="null" /> when no log has any events. Relies on the raw-list
    ///     ordering invariant (index 0 = newest, index Count-1 = oldest), so each endpoint is O(1) per log; callers apply
    ///     rounding and fallback via <c>RoundOrFallback</c>.
    /// </summary>
    internal static (DateTime After, DateTime Before)? TryGetRawEventDateRange(this RawEventStoreState state)
    {
        DateTime? oldest = null;
        DateTime? newest = null;

        foreach (var events in state.ByLog.Values)
        {
            if (events.Count == 0) { continue; }

            var logNewest = events[0].TimeCreated;
            var logOldest = events[^1].TimeCreated;

            if (oldest is null || logOldest < oldest) { oldest = logOldest; }
            if (newest is null || logNewest > newest) { newest = logNewest; }
        }

        return oldest is { } o && newest is { } n ? (o, n) : null;
    }
}
