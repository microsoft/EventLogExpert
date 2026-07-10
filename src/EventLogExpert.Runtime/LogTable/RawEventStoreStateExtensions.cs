// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.LogTable;

internal static class RawEventStoreStateExtensions
{
    internal static (DateTime After, DateTime Before)? TryGetRawEventDateRange(this RawEventStoreState state)
    {
        DateTime? oldest = null;
        DateTime? newest = null;

        foreach (var store in state.ByLog.Values)
        {
            if (!store.TryGetTimeRange(out long minTicks, out long maxTicks)) { continue; }

            // Only Ticks are read downstream (comparison and hour rounding), so stamping UTC is safe.
            var logOldest = new DateTime(minTicks, DateTimeKind.Utc);
            var logNewest = new DateTime(maxTicks, DateTimeKind.Utc);

            if (oldest is null || logOldest < oldest) { oldest = logOldest; }

            if (newest is null || logNewest > newest) { newest = logNewest; }
        }

        return oldest is { } o && newest is { } n ? (o, n) : null;
    }
}
