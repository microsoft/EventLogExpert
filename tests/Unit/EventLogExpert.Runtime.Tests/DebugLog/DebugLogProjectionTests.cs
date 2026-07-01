// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Display;
using EventLogExpert.Runtime.DebugLog;
using EventLogExpert.Runtime.Tests.TestUtils.Constants;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Runtime.Tests.DebugLog;

public sealed class DebugLogProjectionTests
{
    [Fact]
    public void Project_WhenCategoryUncategorizedSentinel_ShouldKeepNullCategoryEntries()
    {
        var entries = new[]
        {
            BuildEntry(LogLevel.Warning, "categorized", "DatabaseTools.Create"),
            BuildEntry(LogLevel.Warning, "uncategorized"),
        };

        var (lines, count) = DebugLogProjection.Project(entries, [Include(DebugLogFilterField.Category, ComparisonOperator.Equals, string.Empty)]);

        Assert.Equal(1, count);
        Assert.Contains("uncategorized", Assert.Single(lines));
    }

    [Fact]
    public void Project_WhenEntriesNull_ShouldThrow() =>
        Assert.Throws<ArgumentNullException>(() => DebugLogProjection.Project(null!, []));

    [Fact]
    public void Project_WhenExcludeLevelEquals_ShouldRemoveMatchingLevel()
    {
        var entries = new[]
        {
            BuildEntry(LogLevel.Information, "info"),
            BuildEntry(LogLevel.Error, "err"),
        };

        var (_, count) = DebugLogProjection.Project(entries, [Exclude(DebugLogFilterField.Level, ComparisonOperator.Equals, nameof(LogLevel.Error))]);

        Assert.Equal(1, count);
    }

    [Fact]
    public void Project_WhenExcludeMessageContains_ShouldEqualIncludeNotContains()
    {
        var entries = new[]
        {
            BuildEntry(LogLevel.Information, "has foo"),
            BuildEntry(LogLevel.Information, "no match"),
        };

        var (_, excludeContainsCount) = DebugLogProjection.Project(entries, [Exclude(DebugLogFilterField.Message, ComparisonOperator.Contains, "foo")]);
        var (_, includeNotContainsCount) = DebugLogProjection.Project(entries, [Include(DebugLogFilterField.Message, ComparisonOperator.NotContains, "foo")]);

        Assert.Equal(1, excludeContainsCount);
        Assert.Equal(includeNotContainsCount, excludeContainsCount);
    }

    [Fact]
    public void Project_WhenExcludeProcessEquals_ShouldRemoveMatchingOrigin()
    {
        var entries = new[]
        {
            BuildEntry(LogLevel.Warning, "helper", null, ProcessOrigin.ElevatedHelper),
            BuildEntry(LogLevel.Warning, "in-proc", null, ProcessOrigin.InProcess),
        };

        var (lines, count) = DebugLogProjection.Project(entries, [Exclude(DebugLogFilterField.Process, ComparisonOperator.Equals, nameof(ProcessOrigin.ElevatedHelper))]);

        Assert.Equal(1, count);
        Assert.Contains("in-proc", Assert.Single(lines));
    }

    [Theory]
    [InlineData(ComparisonOperator.Equals, false)]
    [InlineData(ComparisonOperator.NotEqual, false)]
    [InlineData(ComparisonOperator.Equals, true)]
    [InlineData(ComparisonOperator.NotEqual, true)]
    public void Project_WhenFilterIncomplete_ShouldBeNoOp(ComparisonOperator op, bool excluded)
    {
        var entries = new[]
        {
            BuildEntry(LogLevel.Information, "first"),
            BuildEntry(LogLevel.Warning, "second"),
        };

        var incomplete = new DebugLogFilter(DebugLogFilterField.Level, op, MatchMode.Single, excluded, []);

        var (_, count) = DebugLogProjection.Project(entries, [incomplete]);

        Assert.Equal(2, count);
    }

    [Fact]
    public void Project_WhenFiltersNull_ShouldThrow() =>
        Assert.Throws<ArgumentNullException>(() => DebugLogProjection.Project([], null!));

    [Fact]
    public void Project_WhenIncludeCategoryEquals_ShouldKeepOnlyMatchingCategory()
    {
        var entries = new[]
        {
            BuildEntry(LogLevel.Warning, "a", "DatabaseTools.Create"),
            BuildEntry(LogLevel.Warning, "b", "Elevation.Ipc"),
        };

        var (_, count) = DebugLogProjection.Project(entries, [Include(DebugLogFilterField.Category, ComparisonOperator.Equals, "DatabaseTools.Create")]);

        Assert.Equal(1, count);
    }

    [Fact]
    public void Project_WhenIncludeLevelEquals_ShouldKeepOnlyMatchingLevel()
    {
        var entries = new[]
        {
            BuildEntry(LogLevel.Information, "info"),
            BuildEntry(LogLevel.Error, "err"),
            BuildEntry(LogLevel.Information, "info2"),
        };

        var (_, count) = DebugLogProjection.Project(entries, [Include(DebugLogFilterField.Level, ComparisonOperator.Equals, nameof(LogLevel.Error))]);

        Assert.Equal(1, count);
    }

    [Fact]
    public void Project_WhenIncludeLevelWithMultipleValues_ShouldKeepAnyListed()
    {
        var entries = new[]
        {
            BuildEntry(LogLevel.Information, "info"),
            BuildEntry(LogLevel.Error, "err"),
            BuildEntry(LogLevel.Warning, "warn"),
        };

        var (_, count) = DebugLogProjection.Project(
            entries,
            [Include(DebugLogFilterField.Level, ComparisonOperator.Equals, nameof(LogLevel.Error), nameof(LogLevel.Warning))]);

        Assert.Equal(2, count);
    }

    [Fact]
    public void Project_WhenIncludeMessageContains_ShouldKeepContainingEntries()
    {
        var entries = new[]
        {
            BuildEntry(LogLevel.Information, "alpha foo bravo"),
            BuildEntry(LogLevel.Information, "charlie delta"),
        };

        var (_, count) = DebugLogProjection.Project(entries, [Include(DebugLogFilterField.Message, ComparisonOperator.Contains, "foo")]);

        Assert.Equal(1, count);
    }

    [Fact]
    public void Project_WhenIncludeMessageNotEqual_ShouldRemoveExactMatch()
    {
        var entries = new[]
        {
            BuildEntry(LogLevel.Information, "exact"),
            BuildEntry(LogLevel.Information, "different"),
        };

        var (lines, count) = DebugLogProjection.Project(entries, [Include(DebugLogFilterField.Message, ComparisonOperator.NotEqual, "exact")]);

        Assert.Equal(1, count);
        Assert.Contains("different", Assert.Single(lines));
    }

    [Fact]
    public void Project_WhenIncludeProcessEquals_ShouldKeepMatchingAndExcludeNull()
    {
        var entries = new[]
        {
            BuildEntry(LogLevel.Warning, "helper", "DatabaseTools.Create", ProcessOrigin.ElevatedHelper),
            BuildEntry(LogLevel.Warning, "in-proc", "DatabaseTools.Create", ProcessOrigin.InProcess),
            BuildEntry(null, "orphan"),
        };

        var (lines, count) = DebugLogProjection.Project(entries, [Include(DebugLogFilterField.Process, ComparisonOperator.Equals, nameof(ProcessOrigin.ElevatedHelper))]);

        Assert.Equal(1, count);
        Assert.Contains("helper", Assert.Single(lines));
    }

    [Fact]
    public void Project_WhenIncludeProcessNotEqual_ShouldKeepOtherOriginsAndNull()
    {
        var entries = new[]
        {
            BuildEntry(LogLevel.Warning, "helper", null, ProcessOrigin.ElevatedHelper),
            BuildEntry(LogLevel.Warning, "in-proc", null, ProcessOrigin.InProcess),
            BuildEntry(null, "orphan"),
        };

        var (_, count) = DebugLogProjection.Project(entries, [Include(DebugLogFilterField.Process, ComparisonOperator.NotEqual, nameof(ProcessOrigin.ElevatedHelper))]);

        Assert.Equal(2, count);
    }

    [Fact]
    public void Project_WhenLevelValuesAllUnparseable_ShouldBeNoOpNotMatchNothing()
    {
        var entries = new[]
        {
            BuildEntry(LogLevel.Information, "info"),
            BuildEntry(LogLevel.Error, "err"),
        };

        var (_, includeCount) = DebugLogProjection.Project(entries, [Include(DebugLogFilterField.Level, ComparisonOperator.Equals, "Bogus", "AlsoBogus")]);
        var (_, excludeCount) = DebugLogProjection.Project(entries, [Exclude(DebugLogFilterField.Level, ComparisonOperator.Equals, "Bogus", "AlsoBogus")]);

        Assert.Equal(2, includeCount);
        Assert.Equal(2, excludeCount);
    }

    [Fact]
    public void Project_WhenMessageEquals_ShouldMatchExactMessageCaseInsensitively()
    {
        var entries = new[]
        {
            BuildEntry(LogLevel.Information, "Exact Message"),
            BuildEntry(LogLevel.Information, "Exact Message with more"),
        };

        var (lines, count) = DebugLogProjection.Project(entries, [Include(DebugLogFilterField.Message, ComparisonOperator.Equals, "exact message")]);

        Assert.Equal(1, count);
        Assert.Contains("Exact Message", Assert.Single(lines));
    }

    [Fact]
    public void Project_WhenMultiLineEntryMatches_ShouldEmitAllPhysicalLinesReversed()
    {
        var rawLine = $"[{Constants.DebugLogTestTimestamp}] [{Constants.DebugLogTestThreadId}] [Error] outer\nstack-trace-foo\nat MoreFrames";
        var message = "outer\nstack-trace-foo\nat MoreFrames";
        var entry = new DebugLogEntry(
            DateTimeOffset.Parse(Constants.DebugLogTestTimestamp),
            Constants.DebugLogTestThreadId,
            LogLevel.Error,
            rawLine.Length - message.Length,
            rawLine);

        var (lines, count) = DebugLogProjection.Project([entry], [Include(DebugLogFilterField.Message, ComparisonOperator.Contains, "foo")]);
        var view = new ReversedListView<string>(lines);

        Assert.Equal(1, count);
        Assert.Equal(3, view.Count);
        Assert.Contains("outer", view[0]);
        Assert.Equal("stack-trace-foo", view[1]);
        Assert.Equal("at MoreFrames", view[2]);
    }

    [Fact]
    public void Project_WhenMultipleFilters_ShouldRequireAll()
    {
        var entries = new[]
        {
            BuildEntry(LogLevel.Warning, "match foo", "DatabaseTools.Create"),
            BuildEntry(LogLevel.Information, "match foo", "DatabaseTools.Create"),
            BuildEntry(LogLevel.Warning, "no match", "DatabaseTools.Create"),
        };

        var (lines, count) = DebugLogProjection.Project(
            entries,
            [
                Include(DebugLogFilterField.Level, ComparisonOperator.Equals, nameof(LogLevel.Warning)),
                Include(DebugLogFilterField.Message, ComparisonOperator.Contains, "foo"),
            ]);

        Assert.Equal(1, count);
        Assert.Contains("match foo", Assert.Single(lines));
    }

    [Fact]
    public void Project_WhenNoFilters_ShouldReturnAllEntries()
    {
        var entries = new[]
        {
            BuildEntry(LogLevel.Information, "first"),
            BuildEntry(LogLevel.Warning, "second"),
        };

        var (_, count) = DebugLogProjection.Project(entries, []);

        Assert.Equal(2, count);
    }

    [Fact]
    public void Project_WhenNullLevel_IncludeEqualsExcludesIt_IncludeNotEqualKeepsIt()
    {
        var entries = new[]
        {
            BuildEntry(LogLevel.Error, "err"),
            BuildEntry(null, "orphan"),
        };

        var (_, equalsCount) = DebugLogProjection.Project(entries, [Include(DebugLogFilterField.Level, ComparisonOperator.Equals, nameof(LogLevel.Error))]);
        var (notEqualLines, notEqualCount) = DebugLogProjection.Project(entries, [Include(DebugLogFilterField.Level, ComparisonOperator.NotEqual, nameof(LogLevel.Error))]);

        Assert.Equal(1, equalsCount);
        Assert.Equal(1, notEqualCount);
        Assert.Contains("orphan", Assert.Single(notEqualLines));
    }

    [Fact]
    public void Project_WhenProcessValuesAllUnparseable_ShouldBeNoOpNotMatchNothing()
    {
        var entries = new[]
        {
            BuildEntry(LogLevel.Warning, "helper", null, ProcessOrigin.ElevatedHelper),
            BuildEntry(LogLevel.Warning, "in-proc", null, ProcessOrigin.InProcess),
        };

        var (_, includeCount) = DebugLogProjection.Project(entries, [Include(DebugLogFilterField.Process, ComparisonOperator.Equals, "Bogus")]);
        var (_, excludeCount) = DebugLogProjection.Project(entries, [Exclude(DebugLogFilterField.Process, ComparisonOperator.Equals, "Bogus")]);

        Assert.Equal(2, includeCount);
        Assert.Equal(2, excludeCount);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 4)]
    [InlineData(2, 1)]
    public void ProjectRange_WhenIndicesOutOfRange_ShouldThrow(int startIndex, int endIndex)
    {
        var entries = new[] { BuildEntry(LogLevel.Information, "only") };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DebugLogProjection.ProjectRange(entries, startIndex, endIndex, []));
    }

    [Fact]
    public void ProjectRange_WhenSliceGiven_ShouldEvaluateSliceOnly()
    {
        var entries = new[]
        {
            BuildEntry(LogLevel.Information, "first"),
            BuildEntry(LogLevel.Warning, "second"),
            BuildEntry(LogLevel.Information, "third"),
        };

        var (_, count) = DebugLogProjection.ProjectRange(entries, 1, 3, [Include(DebugLogFilterField.Level, ComparisonOperator.Equals, nameof(LogLevel.Information))]);

        Assert.Equal(1, count);
    }

    private static DebugLogEntry BuildEntry(LogLevel? level, string message, string? category = null, ProcessOrigin? processOrigin = null)
    {
        if (level is null)
        {
            return new DebugLogEntry(null, null, null, 0, message, category, processOrigin);
        }

        var rawLine = $"[{Constants.DebugLogTestTimestamp}] [{Constants.DebugLogTestThreadId}] [{level}] {message}";

        return new DebugLogEntry(
            DateTimeOffset.Parse(Constants.DebugLogTestTimestamp),
            Constants.DebugLogTestThreadId,
            level,
            rawLine.Length - message.Length,
            rawLine,
            category,
            processOrigin);
    }

    private static DebugLogFilter Exclude(DebugLogFilterField field, ComparisonOperator op, params string[] values) =>
        new(field, op, MatchMode.Single, true, values);

    private static DebugLogFilter Include(DebugLogFilterField field, ComparisonOperator op, params string[] values) =>
        new(field, op, MatchMode.Single, false, values);
}
