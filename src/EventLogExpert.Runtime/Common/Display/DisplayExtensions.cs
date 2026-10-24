// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Reflection;
using System.Runtime.Serialization;

namespace EventLogExpert.Runtime.Common.Display;

public static class DisplayExtensions
{
    public static string ToFullString(this Enum value)
    {
        var memberAttribute = value.GetType().GetField(value.ToString())?
            .GetCustomAttribute(typeof(EnumMemberAttribute)) as EnumMemberAttribute;

        return memberAttribute?.Value ?? value.ToString();
    }

    private static DateTime FindGapTransitionUtc(DateTime skippedWallClock, TimeZoneInfo zone)
    {
        var offsetBeforeGap = zone.GetUtcOffset(skippedWallClock.AddDays(-1));
        var offsetAfterGap = zone.GetUtcOffset(skippedWallClock.AddDays(1));

        // The transition lies between the instant read with the post-gap offset (before it) and the pre-gap offset (after it).
        var lowTicks = (skippedWallClock - offsetAfterGap).Ticks;
        var highTicks = (skippedWallClock - offsetBeforeGap).Ticks;

        while (lowTicks < highTicks)
        {
            var middleTicks = lowTicks + ((highTicks - lowTicks) / 2);
            var reading = TimeZoneInfo.ConvertTimeFromUtc(new DateTime(middleTicks, DateTimeKind.Utc), zone);

            if (reading >= skippedWallClock) { highTicks = middleTicks; }
            else { lowTicks = middleTicks + 1; }
        }

        return new DateTime(highTicks, DateTimeKind.Utc);
    }

    extension(DateTime time)
    {
        public DateTime ConvertTimeZone(TimeZoneInfo? destinationTime) =>
            destinationTime is null ? time : TimeZoneInfo.ConvertTimeFromUtc(time, destinationTime);

        /// <summary>
        ///     Converts a wall-clock reading in <paramref name="zone" /> to UTC, mapping a daylight-saving gap to its
        ///     transition instant and a repeated reading per <paramref name="resolution" />.
        /// </summary>
        public DateTime ConvertWallClockToUtc(TimeZoneInfo zone, AmbiguousTimeResolution resolution)
        {
            var wallClock = DateTime.SpecifyKind(time, DateTimeKind.Unspecified);

            if (zone.IsInvalidTime(wallClock))
            {
                return FindGapTransitionUtc(wallClock, zone);
            }

            if (zone.IsAmbiguousTime(wallClock))
            {
                var offsets = zone.GetAmbiguousTimeOffsets(wallClock);
                var offset = resolution == AmbiguousTimeResolution.Earliest ? offsets.Max() : offsets.Min();

                return DateTime.SpecifyKind(wallClock - offset, DateTimeKind.Utc);
            }

            return TimeZoneInfo.ConvertTimeToUtc(wallClock, zone);
        }

        public bool IsSkippedWallClock(TimeZoneInfo zone) =>
            zone.IsInvalidTime(DateTime.SpecifyKind(time, DateTimeKind.Unspecified));
    }
}
