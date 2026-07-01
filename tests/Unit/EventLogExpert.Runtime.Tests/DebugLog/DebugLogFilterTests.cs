// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.DebugLog;

namespace EventLogExpert.Runtime.Tests.DebugLog;

public sealed class DebugLogFilterTests
{
    [Fact]
    public void IsComplete_WhenCategoryUncategorizedSentinel_ShouldBeTrue()
    {
        var filter = new DebugLogFilter(DebugLogFilterField.Category, ComparisonOperator.Equals, MatchMode.Single, false, [string.Empty]);

        Assert.True(filter.IsComplete);
    }

    [Fact]
    public void IsComplete_WhenManyWithAtLeastOneValue_ShouldBeTrue()
    {
        var filter = new DebugLogFilter(DebugLogFilterField.Level, ComparisonOperator.Equals, MatchMode.Many, false, ["Error"]);

        Assert.True(filter.IsComplete);
    }

    [Fact]
    public void IsComplete_WhenManyWithNoValues_ShouldBeFalse()
    {
        var filter = new DebugLogFilter(DebugLogFilterField.Level, ComparisonOperator.Equals, MatchMode.Many, false, []);

        Assert.False(filter.IsComplete);
    }

    [Fact]
    public void IsComplete_WhenSingleWithNonWhitespaceValue_ShouldBeTrue()
    {
        var filter = new DebugLogFilter(DebugLogFilterField.Message, ComparisonOperator.Contains, MatchMode.Single, false, ["foo"]);

        Assert.True(filter.IsComplete);
    }

    [Fact]
    public void IsComplete_WhenSingleWithNoValues_ShouldBeFalse()
    {
        var filter = new DebugLogFilter(DebugLogFilterField.Message, ComparisonOperator.Contains, MatchMode.Single, false, []);

        Assert.False(filter.IsComplete);
    }

    [Fact]
    public void IsComplete_WhenSingleWithWhitespaceValue_ShouldBeFalse()
    {
        var filter = new DebugLogFilter(DebugLogFilterField.Message, ComparisonOperator.Contains, MatchMode.Single, false, ["   "]);

        Assert.False(filter.IsComplete);
    }
}
