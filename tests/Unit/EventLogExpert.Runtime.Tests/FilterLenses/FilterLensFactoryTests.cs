// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

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
    public void ForTimeRange_NormalizesReversedUtcInstants()
    {
        var earlierUtc = new DateTime(2026, 7, 16, 18, 0, 0, DateTimeKind.Utc);
        var laterUtc = new DateTime(2026, 7, 16, 20, 0, 0, DateTimeKind.Utc);

        var lens = FilterLensFactory.ForTimeRange(laterUtc, earlierUtc);

        var range = Assert.IsType<FilterLensLabel.TimeRange>(lens.Label);
        Assert.Equal(earlierUtc, range.AfterUtc);
        Assert.Equal(laterUtc, range.BeforeUtc);
    }

    [Fact]
    public void ForTimeRange_StoresTheProvidedUtcInstants()
    {
        var afterUtc = new DateTime(2026, 7, 16, 23, 55, 0, DateTimeKind.Utc);
        var beforeUtc = new DateTime(2026, 7, 17, 0, 5, 0, DateTimeKind.Utc);

        var lens = FilterLensFactory.ForTimeRange(afterUtc, beforeUtc);

        var range = Assert.IsType<FilterLensLabel.TimeRange>(lens.Label);
        Assert.Equal(afterUtc, range.AfterUtc);
        Assert.Equal(beforeUtc, range.BeforeUtc);
        Assert.Equal(afterUtc, lens.Window!.After);
        Assert.Equal(beforeUtc, lens.Window.Before);
    }

    [Fact]
    public void ForTimeWindow_StoresTheProvidedUtcCenter()
    {
        var centerUtc = new DateTime(2026, 7, 16, 18, 0, 0, DateTimeKind.Utc);
        var radius = TimeSpan.FromMinutes(5);

        var lens = FilterLensFactory.ForTimeWindow(centerUtc, radius);

        var window = Assert.IsType<FilterLensLabel.TimeWindow>(lens.Label);
        Assert.Equal(centerUtc, window.CenterUtc);
        Assert.Equal(radius, window.Radius);
        Assert.Equal(centerUtc.Add(-radius), lens.Window!.After);
        Assert.Equal(centerUtc.Add(radius), lens.Window.Before);
    }
}
