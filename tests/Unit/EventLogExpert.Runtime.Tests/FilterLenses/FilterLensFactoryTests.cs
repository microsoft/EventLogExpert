// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Display;
using EventLogExpert.Runtime.FilterLenses;

namespace EventLogExpert.Runtime.Tests.FilterLenses;

public sealed class FilterLensFactoryTests
{
    [Fact]
    public void ForTimeRange_CrossesDisplayedMidnight_LabelIncludesDates()
    {
        var after = new DateTime(2026, 7, 16, 23, 55, 0, DateTimeKind.Utc);
        var before = new DateTime(2026, 7, 17, 0, 5, 0, DateTimeKind.Utc);

        var lens = FilterLensFactory.ForTimeRange(after, before, TimeZoneInfo.Utc);

        var afterLocal = after.ConvertTimeZone(TimeZoneInfo.Utc);
        var beforeLocal = before.ConvertTimeZone(TimeZoneInfo.Utc);
        Assert.Equal(
            $"{afterLocal:d} {afterLocal:T} - {beforeLocal:d} {beforeLocal:T}",
            lens.Label);
    }

    [Fact]
    public void ForTimeRange_DayBoundaryEvaluatedInDisplayZoneNotUtc()
    {
        // Both endpoints share a UTC calendar day (2026-07-16), but the +5h display zone pushes them across displayed midnight, so the label must still show dates.
        var plusFive = TimeZoneInfo.CreateCustomTimeZone("t+5", TimeSpan.FromHours(5), "t+5", "t+5");
        var after = new DateTime(2026, 7, 16, 18, 0, 0, DateTimeKind.Utc);   // +5 => 2026-07-16 23:00
        var before = new DateTime(2026, 7, 16, 20, 0, 0, DateTimeKind.Utc);  // +5 => 2026-07-17 01:00

        var lens = FilterLensFactory.ForTimeRange(after, before, plusFive);

        var afterLocal = after.ConvertTimeZone(plusFive);
        var beforeLocal = before.ConvertTimeZone(plusFive);
        Assert.NotEqual(afterLocal.Date, beforeLocal.Date);
        Assert.Equal(
            $"{afterLocal:d} {afterLocal:T} - {beforeLocal:d} {beforeLocal:T}",
            lens.Label);
    }

    [Fact]
    public void ForTimeRange_WithinOneDisplayedDay_LabelShowsTimesOnly()
    {
        var after = new DateTime(2026, 7, 16, 10, 0, 0, DateTimeKind.Utc);
        var before = new DateTime(2026, 7, 16, 14, 30, 0, DateTimeKind.Utc);

        var lens = FilterLensFactory.ForTimeRange(after, before, TimeZoneInfo.Utc);

        Assert.Equal(
            $"{after.ConvertTimeZone(TimeZoneInfo.Utc):T} - {before.ConvertTimeZone(TimeZoneInfo.Utc):T}",
            lens.Label);
    }
}
