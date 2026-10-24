// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Display;

namespace EventLogExpert.Runtime.Tests.Common.Display;

public sealed class DisplayExtensionsTests
{
    [Fact]
    public void ConvertWallClockToUtc_AmbiguousTime_ResolvesToEarliestOrLatestInstant()
    {
        var ambiguousWallClock = new DateTime(2024, 11, 3, 1, 30, 0);
        var zone = CreateDaylightSavingZone();

        var earliest = ambiguousWallClock.ConvertWallClockToUtc(zone, AmbiguousTimeResolution.Earliest);
        var latest = ambiguousWallClock.ConvertWallClockToUtc(zone, AmbiguousTimeResolution.Latest);

        Assert.Equal(new DateTime(2024, 11, 3, 5, 30, 0, DateTimeKind.Utc), earliest);
        Assert.Equal(new DateTime(2024, 11, 3, 6, 30, 0, DateTimeKind.Utc), latest);
        Assert.Equal(TimeSpan.FromHours(1), latest - earliest);
        Assert.True(earliest < latest);
    }

    [Fact]
    public void ConvertWallClockToUtc_NormalTime_UsesTheZoneOffset()
    {
        var wallClock = new DateTime(2024, 1, 15, 10, 0, 0);

        var converted = wallClock.ConvertWallClockToUtc(CreateDaylightSavingZone(), AmbiguousTimeResolution.Earliest);

        Assert.Equal(new DateTime(2024, 1, 15, 15, 0, 0, DateTimeKind.Utc), converted);
    }

    [Fact]
    public void ConvertWallClockToUtc_SkippedTime_ResolvesToTheTransitionInstantForEitherResolution()
    {
        var skippedWallClock = new DateTime(2024, 3, 10, 2, 30, 0);
        var zone = CreateDaylightSavingZone();
        var transitionInstant = new DateTime(2024, 3, 10, 7, 0, 0, DateTimeKind.Utc);

        Assert.Equal(transitionInstant, skippedWallClock.ConvertWallClockToUtc(zone, AmbiguousTimeResolution.Earliest));
        Assert.Equal(transitionInstant, skippedWallClock.ConvertWallClockToUtc(zone, AmbiguousTimeResolution.Latest));
        Assert.True(skippedWallClock.IsSkippedWallClock(zone));
        Assert.False(new DateTime(2024, 3, 10, 3, 30, 0).IsSkippedWallClock(zone));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Utc)]
    public void ConvertWallClockToUtc_UsesTheClockReadingRegardlessOfKind(DateTimeKind kind)
    {
        var wallClock = new DateTime(2024, 1, 15, 10, 0, 0, kind);

        var converted = wallClock.ConvertWallClockToUtc(CreateDaylightSavingZone(), AmbiguousTimeResolution.Latest);

        Assert.Equal(new DateTime(2024, 1, 15, 15, 0, 0, DateTimeKind.Utc), converted);
    }

    private static TimeZoneInfo CreateDaylightSavingZone()
    {
        var daylightBegins = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0), month: 3, week: 2, DayOfWeek.Sunday);
        var daylightEnds = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0), month: 11, week: 1, DayOfWeek.Sunday);
        var adjustmentRule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2024, 1, 1),
            new DateTime(2024, 12, 31),
            TimeSpan.FromHours(1),
            daylightBegins,
            daylightEnds);

        return TimeZoneInfo.CreateCustomTimeZone(
            "test-eastern", TimeSpan.FromHours(-5), "test-eastern", "test-eastern", "test-eastern",
            [adjustmentRule]);
    }
}
