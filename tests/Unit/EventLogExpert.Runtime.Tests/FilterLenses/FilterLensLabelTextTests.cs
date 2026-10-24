// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.FilterLenses;

namespace EventLogExpert.Runtime.Tests.FilterLenses;

/// <summary>
///     Pins the invariant-English rendering of every <see cref="FilterLensLabel" /> shape. This is the drift-guard
///     baseline the localized UI formatter mirrors, so the exact copy and math glyphs (<c>=</c>, <c>\u2260</c>,
///     <c>\u00b1</c>) plus the InvariantCulture date/time layout are locked here.
/// </summary>
public sealed class FilterLensLabelTextTests
{
    [Theory]
    [InlineData(1, 0, 0, "1h")]
    [InlineData(0, 5, 0, "5m")]
    [InlineData(0, 1, 30, "90s")]
    public void FormatRadius_UsesInvariantUnitGlyphs(int hours, int minutes, int seconds, string expected)
    {
        Assert.Equal(expected, FilterLensLabelText.FormatRadius(new TimeSpan(hours, minutes, seconds)));
    }

    [Fact]
    public void Invariant_RendersEachShapeInInvariantEnglish()
    {
        var activityId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        Assert.Equal(
            "Source = Contoso",
            FilterLensLabelText.Invariant(new FilterLensLabel.PropertyComparison(EventProperty.Source, IsEqual: true, "Contoso"), TimeZoneInfo.Utc));

        Assert.Equal(
            "Level \u2260 4",
            FilterLensLabelText.Invariant(new FilterLensLabel.PropertyComparison(EventProperty.Level, IsEqual: false, "4"), TimeZoneInfo.Utc));

        Assert.Equal(
            $"Parent Activity = {activityId}",
            FilterLensLabelText.Invariant(new FilterLensLabel.ParentActivity(activityId), TimeZoneInfo.Utc));

        Assert.Equal(
            "13:05:09 - 14:30:00",
            FilterLensLabelText.Invariant(new FilterLensLabel.TimeRange(
                new DateTime(2026, 7, 16, 13, 5, 9, DateTimeKind.Utc), new DateTime(2026, 7, 16, 14, 30, 0, DateTimeKind.Utc)), TimeZoneInfo.Utc));

        Assert.Equal(
            "07/16/2026 23:55:00 - 07/17/2026 00:05:00",
            FilterLensLabelText.Invariant(new FilterLensLabel.TimeRange(
                new DateTime(2026, 7, 16, 23, 55, 0, DateTimeKind.Utc), new DateTime(2026, 7, 17, 0, 5, 0, DateTimeKind.Utc)), TimeZoneInfo.Utc));

        Assert.Equal(
            "Near 13:05:09 \u00b15m",
            FilterLensLabelText.Invariant(new FilterLensLabel.TimeWindow(
                new DateTime(2026, 7, 16, 13, 5, 9, DateTimeKind.Utc), TimeSpan.FromMinutes(5)), TimeZoneInfo.Utc));
    }

    [Fact]
    public void Invariant_RendersUtcInstantsInTheRequestedDisplayZone()
    {
        var displayZone = TimeZoneInfo.CreateCustomTimeZone(
            "test+2", TimeSpan.FromHours(2), "test+2", "test+2");
        var afterUtc = new DateTime(2026, 7, 16, 23, 55, 0, DateTimeKind.Utc);
        var beforeUtc = new DateTime(2026, 7, 17, 0, 5, 0, DateTimeKind.Utc);
        var centerUtc = new DateTime(2026, 7, 16, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(
            "01:55:00 - 02:05:00",
            FilterLensLabelText.Invariant(new FilterLensLabel.TimeRange(afterUtc, beforeUtc), displayZone));
        Assert.Equal(
            "Near 14:00:00 \u00b15m",
            FilterLensLabelText.Invariant(new FilterLensLabel.TimeWindow(centerUtc, TimeSpan.FromMinutes(5)), displayZone));
    }
}
