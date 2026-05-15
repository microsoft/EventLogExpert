// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Common.EventLogs;

public static class EventLogDataExtensions
{
    private static readonly long s_ticksPerHour = TimeSpan.FromHours(1).Ticks;

    private static DateTime CeilingToHour(DateTime value) =>
        new((value.Ticks + s_ticksPerHour - 1) / s_ticksPerHour * s_ticksPerHour, DateTimeKind.Utc);

    private static DateTime FloorToHour(DateTime value) =>
        new(value.Ticks / s_ticksPerHour * s_ticksPerHour, DateTimeKind.Utc);

    extension(IEnumerable<EventLogData> logs)
    {
        /// <summary>
        ///     Tries to compute the RAW (unrounded) bounds across the supplied logs: <paramref name="range" />.<c>After</c>
        ///     is the oldest event timestamp and <paramref name="range" />.<c>Before</c> is the newest. Returns
        ///     <see langword="false" /> when no contributing log has any timestamped events.
        /// </summary>
        /// <remarks>
        ///     Pure projection over <paramref name="logs" /> with no clock dependency. Callers handle rounding and fallback
        ///     (see <see cref="RoundOrFallback" /> / <see cref="GetEventDateRange" />). Relies on the convention that
        ///     <see cref="EventLogData.Events" /> is sorted by <c>RecordId</c> descending — <c>FirstOrDefault()</c> is the newest
        ///     event and <c>LastOrDefault()</c> is the oldest.
        /// </remarks>
        public bool TryGetEventDateRange(out (DateTime After, DateTime Before) range)
        {
            DateTime? oldest = null;
            DateTime? newest = null;

            foreach (var log in logs)
            {
                if (log.Events.LastOrDefault()?.TimeCreated is { } logOldest &&
                    (oldest is null || logOldest < oldest))
                {
                    oldest = logOldest;
                }

                if (log.Events.FirstOrDefault()?.TimeCreated is { } logNewest &&
                    (newest is null || logNewest > newest))
                {
                    newest = logNewest;
                }
            }

            if (oldest is { } o && newest is { } n)
            {
                range = (o, n);

                return true;
            }

            range = default;

            return false;
        }

        /// <summary>
        ///     Returns bounds across the supplied logs: <c>After</c> is the oldest event timestamp and <c>Before</c> is the
        ///     newest. <c>After</c> is floored to the nearest hour and <c>Before</c> is ceilinged so the rounded range fully
        ///     contains every event.
        /// </summary>
        /// <remarks>
        ///     Convenience composition of <see cref="TryGetEventDateRange" /> and <see cref="RoundOrFallback" />. If no log
        ///     contributes any timestamp, both bounds fall back to the supplied <paramref name="fallbackUtcNow" /> (After floored,
        ///     Before ceilinged) so callers get a deterministic, non-empty range.
        /// </remarks>
        public (DateTime After, DateTime Before) GetEventDateRange(DateTime fallbackUtcNow) =>
            (logs.TryGetEventDateRange(out var range) ? range : default((DateTime After, DateTime Before)?))
                .RoundOrFallback(fallbackUtcNow);
    }

    extension((DateTime After, DateTime Before)? range)
    {
        /// <summary>
        ///     Rounds an unrounded range to hour boundaries (<c>After</c> floored, <c>Before</c> ceilinged) and substitutes
        ///     <paramref name="fallbackUtcNow" /> when the range is <c>null</c> so callers always receive a non-empty range.
        /// </summary>
        public (DateTime After, DateTime Before) RoundOrFallback(DateTime fallbackUtcNow)
        {
            var afterSource = range?.After ?? fallbackUtcNow;
            var beforeSource = range?.Before ?? fallbackUtcNow;

            return (FloorToHour(afterSource), CeilingToHour(beforeSource));
        }
    }
}

