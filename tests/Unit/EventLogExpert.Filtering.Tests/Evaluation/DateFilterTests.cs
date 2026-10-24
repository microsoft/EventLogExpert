// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Filtering.Tests.Evaluation;

public sealed class DateFilterTests
{
    [Fact]
    public void Contains_WhenAfterIsOpen_IncludesEarlierValuesThroughBefore()
    {
        var before = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var filter = new DateFilter { Before = before };

        Assert.True(filter.Contains(before.AddHours(-1)));
        Assert.True(filter.Contains(before));
        Assert.False(filter.Contains(before.AddTicks(1)));
    }

    [Fact]
    public void Contains_WhenBeforeIsOpen_IncludesAfterThroughLaterValues()
    {
        var after = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var filter = new DateFilter { After = after };

        Assert.False(filter.Contains(after.AddTicks(-1)));
        Assert.True(filter.Contains(after));
        Assert.True(filter.Contains(after.AddHours(1)));
    }

    [Fact]
    public void Contains_WhenBothBoundsAreOpen_IncludesAnyTime()
    {
        var filter = new DateFilter();

        Assert.True(filter.Contains(new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.True(filter.Contains(new DateTime(2200, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Contains_WhenBothBoundsExist_IncludesBothEdges()
    {
        var after = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var before = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var filter = new DateFilter { After = after, Before = before };

        Assert.False(filter.Contains(after.AddTicks(-1)));
        Assert.True(filter.Contains(after));
        Assert.True(filter.Contains(before));
        Assert.False(filter.Contains(before.AddTicks(1)));
    }
}
