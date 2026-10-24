// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Common.EventLogs;

public static class EventLogDataExtensions
{
    private static DateTime CeilingToSecond(DateTime value) =>
        new((value.Ticks + TimeSpan.TicksPerSecond - 1) / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc);

    private static DateTime FloorToSecond(DateTime value) =>
        new(value.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc);

    extension((DateTime After, DateTime Before)? range)
    {
        /// <summary>
        ///     Widens an unrounded range to whole seconds (<c>After</c> floored, <c>Before</c> ceilinged) so it still
        ///     contains every event once shown in a seconds-precision input, and substitutes <paramref name="fallbackUtcNow" />
        ///     when the range is <c>null</c> so callers always receive a non-empty range.
        /// </summary>
        public (DateTime After, DateTime Before) RoundOrFallback(DateTime fallbackUtcNow)
        {
            var afterSource = range?.After ?? fallbackUtcNow;
            var beforeSource = range?.Before ?? fallbackUtcNow;

            return (FloorToSecond(afterSource), CeilingToSecond(beforeSource));
        }
    }
}
