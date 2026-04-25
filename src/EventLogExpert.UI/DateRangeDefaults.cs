// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.


// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.UI;

/// <summary>
///     Computes the default initial bounds for the date-range filter from the active logs. Uses ENVELOPE semantics:
///     <c>After</c> is the oldest event timestamp across all logs and <c>Before</c> is the newest. <c>After</c> is floored
///     to the nearest hour and <c>Before</c> is ceilinged so the rounded range fully contains every event.
/// </summary>
/// <remarks>
///     Relies on the convention that <see cref="EventLogData.Events" /> is sorted by <c>RecordId</c> in descending
///     order (newest first), which in practice correlates with <c>TimeCreated</c> for Windows Event Log data:
///     <c>FirstOrDefault()</c> is treated as the newest event in a log and <c>LastOrDefault()</c> as the oldest. Logs with
///     no events are skipped. If no log contributes any timestamp, both bounds fall back to the supplied
///     <paramref name="fallbackUtcNow" /> (After floored, Before ceilinged) so callers get a deterministic, non-empty
///     range.
/// </remarks>
public static class DateRangeDefaults
{
    private static readonly long s_ticksPerHour = TimeSpan.FromHours(1).Ticks;

    public static (DateTime After, DateTime Before) ComputeFromActiveLogs(
        IEnumerable<EventLogData> logs,
        DateTime fallbackUtcNow)
    {
        DateTime? oldest = null;
        DateTime? newest = null;

        foreach (var log in logs)
        {
            // Last = oldest because Events is sorted by RecordId descending (newest first).
            if (log.Events.LastOrDefault()?.TimeCreated is { } logOldest &&
                (oldest is null || logOldest < oldest))
            {
                oldest = logOldest;
            }

            // First = newest for the same reason.
            if (log.Events.FirstOrDefault()?.TimeCreated is { } logNewest &&
                (newest is null || logNewest > newest))
            {
                newest = logNewest;
            }
        }

        var afterSource = oldest ?? fallbackUtcNow;
        var beforeSource = newest ?? fallbackUtcNow;

        return (FloorToHour(afterSource), CeilingToHour(beforeSource));
    }

    private static DateTime CeilingToHour(DateTime value) =>
        new((value.Ticks + s_ticksPerHour - 1) / s_ticksPerHour * s_ticksPerHour, DateTimeKind.Utc);

    private static DateTime FloorToHour(DateTime value) =>
        new(value.Ticks / s_ticksPerHour * s_ticksPerHour, DateTimeKind.Utc);
}
