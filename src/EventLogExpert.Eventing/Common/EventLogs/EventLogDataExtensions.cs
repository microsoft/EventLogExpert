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

