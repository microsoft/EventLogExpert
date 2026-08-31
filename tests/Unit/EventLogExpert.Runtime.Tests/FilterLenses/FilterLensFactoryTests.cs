// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Display;
using EventLogExpert.Runtime.FilterLenses;

namespace EventLogExpert.Runtime.Tests.FilterLenses;

public sealed class FilterLensFactoryTests
{
    [Fact]
    public void ForActivityId_EmitsActivityIdEqualityDescriptor()
    {
        var activityId = Guid.NewGuid();

        var lens = FilterLensFactory.ForActivityId(activityId)!;

        var comparison = Assert.IsType<FilterLensLabel.PropertyComparison>(lens.Label);
        Assert.Equal(EventProperty.ActivityId, comparison.Property);
        Assert.True(comparison.IsEqual);
        Assert.Equal(activityId.ToString(), comparison.Value);
    }

    [Fact]
    public void ForExcludedValue_EmitsInequalityDescriptor()
    {
        var lens = FilterLensFactory.ForExcludedValue(EventProperty.Source, "Contoso")!;

        var comparison = Assert.IsType<FilterLensLabel.PropertyComparison>(lens.Label);
        Assert.Equal(EventProperty.Source, comparison.Property);
        Assert.False(comparison.IsEqual);
        Assert.Equal("Contoso", comparison.Value);
    }

    [Fact]
    public void ForIncludedValue_EmitsEqualityDescriptor()
    {
        var lens = FilterLensFactory.ForIncludedValue(EventProperty.Source, "Contoso")!;

        var comparison = Assert.IsType<FilterLensLabel.PropertyComparison>(lens.Label);
        Assert.Equal(EventProperty.Source, comparison.Property);
        Assert.True(comparison.IsEqual);
        Assert.Equal("Contoso", comparison.Value);
    }

    [Fact]
    public void ForParentActivity_EmitsParentActivityDescriptor()
    {
        var activityId = Guid.NewGuid();

        var lens = FilterLensFactory.ForParentActivity(activityId)!;

        var parent = Assert.IsType<FilterLensLabel.ParentActivity>(lens.Label);
        Assert.Equal(activityId, parent.ActivityId);
    }

    [Fact]
    public void ForRelatedActivityId_EmitsRelatedActivityIdEqualityDescriptor()
    {
        var relatedActivityId = Guid.NewGuid();

        var lens = FilterLensFactory.ForRelatedActivityId(relatedActivityId)!;

        var comparison = Assert.IsType<FilterLensLabel.PropertyComparison>(lens.Label);
        Assert.Equal(EventProperty.RelatedActivityId, comparison.Property);
        Assert.True(comparison.IsEqual);
        Assert.Equal(relatedActivityId.ToString(), comparison.Value);
    }

    [Fact]
    public void ForTimeRange_CrossesDisplayedMidnight_LabelIncludesDates()
    {
        var after = new DateTime(2026, 7, 16, 23, 55, 0, DateTimeKind.Utc);
        var before = new DateTime(2026, 7, 17, 0, 5, 0, DateTimeKind.Utc);

        var lens = FilterLensFactory.ForTimeRange(after, before, TimeZoneInfo.Utc);

        var afterLocal = after.ConvertTimeZone(TimeZoneInfo.Utc);
        var beforeLocal = before.ConvertTimeZone(TimeZoneInfo.Utc);
        var range = Assert.IsType<FilterLensLabel.TimeRange>(lens.Label);
        Assert.Equal(afterLocal, range.AfterLocal);
        Assert.Equal(beforeLocal, range.BeforeLocal);
        Assert.False(range.SameDay);
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
        var range = Assert.IsType<FilterLensLabel.TimeRange>(lens.Label);
        Assert.Equal(afterLocal, range.AfterLocal);
        Assert.Equal(beforeLocal, range.BeforeLocal);
        Assert.False(range.SameDay);
    }

    [Fact]
    public void ForTimeRange_WithinOneDisplayedDay_LabelShowsTimesOnly()
    {
        var after = new DateTime(2026, 7, 16, 10, 0, 0, DateTimeKind.Utc);
        var before = new DateTime(2026, 7, 16, 14, 30, 0, DateTimeKind.Utc);

        var lens = FilterLensFactory.ForTimeRange(after, before, TimeZoneInfo.Utc);

        var range = Assert.IsType<FilterLensLabel.TimeRange>(lens.Label);
        Assert.Equal(after.ConvertTimeZone(TimeZoneInfo.Utc), range.AfterLocal);
        Assert.Equal(before.ConvertTimeZone(TimeZoneInfo.Utc), range.BeforeLocal);
        Assert.True(range.SameDay);
    }

    [Fact]
    public void ForTimeWindow_EmitsTimeWindowDescriptorInDisplayZone()
    {
        var timeCreated = new DateTime(2026, 7, 16, 18, 0, 0, DateTimeKind.Utc);
        var radius = TimeSpan.FromMinutes(5);
        var plusFive = TimeZoneInfo.CreateCustomTimeZone("t+5", TimeSpan.FromHours(5), "t+5", "t+5");

        var lens = FilterLensFactory.ForTimeWindow(timeCreated, radius, plusFive);

        var window = Assert.IsType<FilterLensLabel.TimeWindow>(lens.Label);
        Assert.Equal(timeCreated.ConvertTimeZone(plusFive), window.CenterLocal);
        Assert.Equal(radius, window.Radius);
    }
}
