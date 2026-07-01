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
    public void Project_WhenCategoryFilterActive_ShouldExcludeNullCategoryWithoutSentinel()
    {
        var entries = new[]
        {
            BuildEntry(LogLevel.Information, "categorized", LogCategories.DatabaseToolsCreate),
            BuildEntry(LogLevel.Information, "uncategorized"),
        };

        var (_, count) = DebugLogProjection.Project(
            entries,
            ComparisonOperator.Equals,
            [],
            string.Empty,
            [LogCategories.DatabaseToolsCreate]);

        Assert.Equal(1, count);
    }

    [Fact]
    public void Project_WhenCategoryFilterActive_ShouldKeepOnlyMatchingCategories()
    {
        var entries = new[]
        {
            BuildEntry(LogLevel.Information, "a", LogCategories.DatabaseToolsCreate),
            BuildEntry(LogLevel.Information, "b", LogCategories.ElevationIpc),
            BuildEntry(LogLevel.Information, "c", LogCategories.DatabaseToolsMerge),
        };

        var (_, count) = DebugLogProjection.Project(
            entries,
            ComparisonOperator.Equals,
            [],
            string.Empty,
            [LogCategories.DatabaseToolsCreate, LogCategories.DatabaseToolsMerge]);

        Assert.Equal(2, count);
    }

    [Fact]
    public void Project_WhenCategoryLevelAndProcessOriginCombined_ShouldRequireAll()
    {
        var entries = new[]
        {
            BuildEntry(LogLevel.Warning, "match", LogCategories.DatabaseToolsCreate, ProcessOrigin.ElevatedHelper),
            BuildEntry(LogLevel.Information, "wrong level", LogCategories.DatabaseToolsCreate, ProcessOrigin.ElevatedHelper),
            BuildEntry(LogLevel.Warning, "wrong category", LogCategories.ElevationIpc, ProcessOrigin.ElevatedHelper),
            BuildEntry(LogLevel.Warning, "wrong origin", LogCategories.DatabaseToolsCreate, ProcessOrigin.InProcess),
        };

        var (lines, count) = DebugLogProjection.Project(
            entries,
            ComparisonOperator.Equals,
            [LogLevel.Warning],
            string.Empty,
            [LogCategories.DatabaseToolsCreate],
            ProcessOrigin.ElevatedHelper);

        Assert.Equal(1, count);
        var only = Assert.Single(lines);
        Assert.Contains("match", only);
    }

    [Fact]
    public void Project_WhenEntriesNull_ShouldThrow()
    {
        // Act + Assert
        Assert.Throws<ArgumentNullException>(() =>
            DebugLogProjection.Project(null!, ComparisonOperator.Equals, [], string.Empty));
    }

    [Fact]
    public void Project_WhenInputEmpty_ShouldReturnEmpty()
    {
        // Act
        var (lines, count) = DebugLogProjection.Project([], ComparisonOperator.Equals, [], string.Empty);

        // Assert
        Assert.Empty(lines);
        Assert.Equal(0, count);
    }

    [Fact]
    public void Project_WhenLevelAndTextFilterCombined_ShouldRequireBoth()
    {
        // Arrange
        var entries = new[]
        {
            BuildEntry(LogLevel.Information, "match foo"),
            BuildEntry(LogLevel.Warning, "match foo"),
            BuildEntry(LogLevel.Information, "no match"),
        };

        // Act
        var (view, count) = ProjectView(
            entries,
            ComparisonOperator.Equals,
            [LogLevel.Information],
            "foo");

        // Assert
        Assert.Equal(1, count);
        var only = Assert.Single(view);
        Assert.Contains("match foo", only);
    }

    [Fact]
    public void Project_WhenLevelEquals_ShouldKeepOnlyMatchingLevel()
    {
        // Arrange
        var entries = new[]
        {
            BuildEntry(LogLevel.Information, Constants.DebugLogFirstMessage),
            BuildEntry(LogLevel.Warning, Constants.DebugLogSecondMessage),
            BuildEntry(LogLevel.Information, Constants.DebugLogThirdMessage),
        };

        // Act
        var (view, count) = ProjectView(
            entries,
            ComparisonOperator.Equals,
            [LogLevel.Information],
            string.Empty);

        // Assert
        Assert.Equal(2, count);
        Assert.Equal(2, view.Count);
        Assert.Contains(Constants.DebugLogThirdMessage, view[0]);
        Assert.Contains(Constants.DebugLogFirstMessage, view[1]);
    }

    [Fact]
    public void Project_WhenLevelFilterActiveAndEntryHasNullLevel_ShouldExcludeEntry()
    {
        // Arrange
        var entries = new[]
        {
            BuildEntry(LogLevel.Information, Constants.DebugLogFirstMessage),
            new DebugLogEntry(null, null, null, 0, "orphan"),
        };

        // Act
        var (_, count) = DebugLogProjection.Project(
            entries,
            ComparisonOperator.Equals,
            [LogLevel.Information],
            string.Empty);

        // Assert
        Assert.Equal(1, count);
    }

    [Fact]
    public void Project_WhenLevelMultiSelect_ShouldKeepAnyListedLevel()
    {
        // Arrange
        var entries = new[]
        {
            BuildEntry(LogLevel.Information, Constants.DebugLogFirstMessage),
            BuildEntry(LogLevel.Warning, Constants.DebugLogSecondMessage),
            BuildEntry(LogLevel.Error, Constants.DebugLogThirdMessage),
            BuildEntry(LogLevel.Critical, Constants.DebugLogNewMessage),
        };

        // Act
        var (view, count) = ProjectView(
            entries,
            ComparisonOperator.Equals,
            [LogLevel.Error, LogLevel.Critical],
            string.Empty);

        // Assert
        Assert.Equal(2, count);
        Assert.Equal(2, view.Count);
        Assert.Contains(Constants.DebugLogNewMessage, view[0]);
        Assert.Contains(Constants.DebugLogThirdMessage, view[1]);
    }

    [Fact]
    public void Project_WhenLevelMultiSelectAndEntryHasNullLevel_ShouldExcludeEntry()
    {
        // Arrange
        var entries = new[]
        {
            BuildEntry(LogLevel.Information, Constants.DebugLogFirstMessage),
            new DebugLogEntry(null, null, null, 0, "orphan"),
        };

        // Act
        var (_, count) = DebugLogProjection.Project(
            entries,
            ComparisonOperator.Equals,
            [LogLevel.Information, LogLevel.Warning],
            string.Empty);

        // Assert
        Assert.Equal(1, count);
    }

    [Fact]
    public void Project_WhenLevelNotEqual_ShouldExcludeMatchingLevel()
    {
        // Arrange
        var entries = new[]
        {
            BuildEntry(LogLevel.Information, Constants.DebugLogFirstMessage),
            BuildEntry(LogLevel.Warning, Constants.DebugLogSecondMessage),
            BuildEntry(LogLevel.Information, Constants.DebugLogThirdMessage),
        };

        // Act
        var (lines, count) = DebugLogProjection.Project(
            entries,
            ComparisonOperator.NotEqual,
            [LogLevel.Information],
            string.Empty);

        // Assert
        Assert.Equal(1, count);
        var only = Assert.Single(lines);
        Assert.Contains(Constants.DebugLogSecondMessage, only);
    }

    [Fact]
    public void Project_WhenLevelNotEqualAndEntryHasNullLevel_ShouldIncludeEntry()
    {
        // Arrange — null Level is "not equal" to any specific level, so NotEqual must include it.
        var entries = new[]
        {
            BuildEntry(LogLevel.Information, Constants.DebugLogFirstMessage),
            new DebugLogEntry(null, null, null, 0, "orphan"),
        };

        // Act
        var (lines, count) = DebugLogProjection.Project(
            entries,
            ComparisonOperator.NotEqual,
            [LogLevel.Information],
            string.Empty);

        // Assert
        Assert.Equal(1, count);
        var only = Assert.Single(lines);
        Assert.Equal("orphan", only);
    }

    [Fact]
    public void Project_WhenLevelsEmpty_ShouldIgnoreLevelOperator()
    {
        // Arrange
        var entries = new[]
        {
            BuildEntry(LogLevel.Information, Constants.DebugLogFirstMessage),
            BuildEntry(LogLevel.Warning, Constants.DebugLogSecondMessage),
        };

        // Act
        var (_, count) = DebugLogProjection.Project(entries, ComparisonOperator.NotEqual, [], string.Empty);

        // Assert
        Assert.Equal(2, count);
    }

    [Fact]
    public void Project_WhenLevelsNull_ShouldThrow()
    {
        // Act + Assert
        Assert.Throws<ArgumentNullException>(() =>
            DebugLogProjection.Project([], ComparisonOperator.Equals, null!, string.Empty));
    }

    [Fact]
    public void Project_WhenMultiLineEntryHasBlankPhysicalLine_ShouldEmitEmptyStringForBlank()
    {
        // Arrange
        var rawLine =
            $"[{Constants.DebugLogTestTimestamp}] [{Constants.DebugLogTestThreadId}] [Error] outer\n\nat MoreFrames";

        var message = "outer\n\nat MoreFrames";

        var entry = new DebugLogEntry(
            DateTimeOffset.Parse(Constants.DebugLogTestTimestamp),
            Constants.DebugLogTestThreadId,
            LogLevel.Error,
            rawLine.Length - message.Length,
            rawLine);

        // Act
        var (view, count) = ProjectView([entry], ComparisonOperator.Equals, [], string.Empty);

        // Assert
        Assert.Equal(1, count);
        Assert.Equal(3, view.Count);
        Assert.Contains("outer", view[0]);
        Assert.Equal(string.Empty, view[1]);
        Assert.Equal("at MoreFrames", view[2]);
    }

    [Fact]
    public void Project_WhenMultiLineEntryMatchesViaContinuation_ShouldEmitAllPhysicalLines()
    {
        // Arrange
        var rawLine =
            $"[{Constants.DebugLogTestTimestamp}] [{Constants.DebugLogTestThreadId}] [Error] outer\nstack-trace-foo\nat MoreFrames";

        var message = "outer\nstack-trace-foo\nat MoreFrames";

        var entry = new DebugLogEntry(
            DateTimeOffset.Parse(Constants.DebugLogTestTimestamp),
            Constants.DebugLogTestThreadId,
            LogLevel.Error,
            rawLine.Length - message.Length,
            rawLine);

        // Act
        var (view, count) = ProjectView([entry], ComparisonOperator.Equals, [], "foo");

        // Assert
        Assert.Equal(1, count);
        Assert.Equal(3, view.Count);
        Assert.Contains("outer", view[0]);
        Assert.Equal("stack-trace-foo", view[1]);
        Assert.Equal("at MoreFrames", view[2]);
    }

    [Fact]
    public void Project_WhenNoFilters_ShouldReturnAllLinesInDisplayOrderViaReversedView()
    {
        // Arrange
        var entries = new[]
        {
            BuildEntry(LogLevel.Information, Constants.DebugLogFirstMessage),
            BuildEntry(LogLevel.Warning, Constants.DebugLogSecondMessage),
            BuildEntry(LogLevel.Error, Constants.DebugLogThirdMessage),
        };

        // Act
        var (view, count) = ProjectView(entries, ComparisonOperator.Equals, [], string.Empty);

        // Assert
        Assert.Equal(3, count);
        Assert.Equal(3, view.Count);
        Assert.Contains(Constants.DebugLogThirdMessage, view[0]);
        Assert.Contains(Constants.DebugLogSecondMessage, view[1]);
        Assert.Contains(Constants.DebugLogFirstMessage, view[2]);
    }

    [Fact]
    public void Project_WhenProcessOriginFilterActive_ShouldKeepOnlyMatchingOrigin()
    {
        var entries = new[]
        {
            BuildEntry(LogLevel.Information, "in-proc", LogCategories.DatabaseToolsCreate, ProcessOrigin.InProcess),
            BuildEntry(LogLevel.Information, "helper", LogCategories.DatabaseToolsCreate, ProcessOrigin.ElevatedHelper),
        };

        var (lines, count) = DebugLogProjection.Project(
            entries,
            ComparisonOperator.Equals,
            [],
            string.Empty,
            categories: null,
            processOriginFilter: ProcessOrigin.ElevatedHelper);

        Assert.Equal(1, count);
        var only = Assert.Single(lines);
        Assert.Contains("helper", only);
    }

    [Fact]
    public void Project_WhenTextFilterDifferentCase_ShouldMatchCaseInsensitively()
    {
        // Arrange
        var entries = new[]
        {
            BuildEntry(LogLevel.Information, "Foo Bar"),
        };

        // Act
        var (_, count) = DebugLogProjection.Project(entries, ComparisonOperator.Equals, [], "FOO");

        // Assert
        Assert.Equal(1, count);
    }

    [Fact]
    public void Project_WhenTextFilterMatches_ShouldKeepOnlyContainingEntries()
    {
        // Arrange
        var entries = new[]
        {
            BuildEntry(LogLevel.Information, "alpha foo bravo"),
            BuildEntry(LogLevel.Information, "charlie delta"),
            BuildEntry(LogLevel.Warning, "echo foo foxtrot"),
        };

        // Act
        var (view, count) = ProjectView(entries, ComparisonOperator.Equals, [], "foo");

        // Assert
        Assert.Equal(2, count);
        Assert.Equal(2, view.Count);
        Assert.Contains("echo foo foxtrot", view[0]);
        Assert.Contains("alpha foo bravo", view[1]);
    }

    [Fact]
    public void Project_WhenUncategorizedSentinelSelected_ShouldKeepNullCategoryEntries()
    {
        var entries = new[]
        {
            BuildEntry(LogLevel.Information, "categorized", LogCategories.DatabaseToolsCreate),
            BuildEntry(LogLevel.Information, "uncategorized"),
        };

        var (lines, count) = DebugLogProjection.Project(
            entries,
            ComparisonOperator.Equals,
            [],
            string.Empty,
            [string.Empty]);

        Assert.Equal(1, count);
        var only = Assert.Single(lines);
        Assert.Contains("uncategorized", only);
    }

    [Fact]
    public void ProjectRange_WhenFilterApplied_ShouldEvaluateAgainstSliceOnly()
    {
        // Arrange
        var entries = new[]
        {
            BuildEntry(LogLevel.Information, Constants.DebugLogFirstMessage),
            BuildEntry(LogLevel.Warning, Constants.DebugLogSecondMessage),
            BuildEntry(LogLevel.Information, Constants.DebugLogThirdMessage),
        };

        // Act
        var (lines, count) = DebugLogProjection.ProjectRange(
            entries,
            1,
            3,
            ComparisonOperator.Equals,
            [LogLevel.Information],
            string.Empty);

        // Assert
        Assert.Equal(1, count);
        var only = Assert.Single(lines);
        Assert.Contains(Constants.DebugLogThirdMessage, only);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 4)]
    [InlineData(2, 1)]
    public void ProjectRange_WhenIndicesOutOfRange_ShouldThrow(int startIndex, int endIndex)
    {
        // Arrange
        var entries = new[]
        {
            BuildEntry(LogLevel.Information, Constants.DebugLogFirstMessage),
            BuildEntry(LogLevel.Warning, Constants.DebugLogSecondMessage),
            BuildEntry(LogLevel.Information, Constants.DebugLogThirdMessage),
        };

        // Act + Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DebugLogProjection.ProjectRange(
                entries,
                startIndex,
                endIndex,
                ComparisonOperator.Equals,
                [],
                string.Empty));
    }

    [Fact]
    public void ProjectRange_WhenSliceIsEmpty_ShouldReturnEmpty()
    {
        // Arrange
        var entries = new[]
        {
            BuildEntry(LogLevel.Information, Constants.DebugLogFirstMessage),
            BuildEntry(LogLevel.Warning, Constants.DebugLogSecondMessage),
        };

        // Act
        var (lines, count) = DebugLogProjection.ProjectRange(
            entries,
            2,
            2,
            ComparisonOperator.Equals,
            [],
            string.Empty);

        // Assert
        Assert.Empty(lines);
        Assert.Equal(0, count);
    }

    [Fact]
    public void ProjectRange_WhenSliceIsTrailingThird_ShouldReturnOnlyThatSliceInDisplayOrderViaReversedView()
    {
        // Arrange
        var entries = new[]
        {
            BuildEntry(LogLevel.Information, Constants.DebugLogFirstMessage),
            BuildEntry(LogLevel.Warning, Constants.DebugLogSecondMessage),
            BuildEntry(LogLevel.Error, Constants.DebugLogThirdMessage),
        };

        // Act
        var (lines, count) = DebugLogProjection.ProjectRange(
            entries,
            1,
            3,
            ComparisonOperator.Equals,
            [],
            string.Empty);

        var view = new ReversedListView<string>(lines);

        // Assert
        Assert.Equal(2, count);
        Assert.Equal(2, view.Count);
        Assert.Contains(Constants.DebugLogThirdMessage, view[0]);
        Assert.Contains(Constants.DebugLogSecondMessage, view[1]);
    }

    private static DebugLogEntry BuildEntry(LogLevel level, string message, string? category = null, ProcessOrigin? processOrigin = null)
    {
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

    private static (ReversedListView<string> View, int Count) ProjectView(
        IReadOnlyList<DebugLogEntry> entries,
        ComparisonOperator levelOperator,
        IReadOnlyList<LogLevel> levels,
        string? textFilter)
    {
        var (lines, count) = DebugLogProjection.Project(entries, levelOperator, levels, textFilter);

        return (new ReversedListView<string>(lines), count);
    }
}
