// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Filtering.TestUtils.Constants;

namespace EventLogExpert.Filtering.Tests.Runtime;

public sealed class ResolvedEventExtensionsTests
{
    [Fact]
    public void MatchesDateFilter_WhenAfterIsAfterBefore_ShouldFailDateConstraint()
    {
        // Arrange
        var @event = FilterEventBuilder.CreateTestEvent(100,
            timeCreated: new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc));

        var dateFilter = new DateFilter
        {
            IsEnabled = true,
            After = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            Before = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        // Act
        var result = @event.MatchesDateFilter(dateFilter);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void MatchesDateFilter_WhenDateFilterDisabled_ShouldNotConstrainEvent()
    {
        // Arrange
        var eventTime = DateTime.Now.AddYears(-10);
        var @event = FilterEventBuilder.CreateTestEvent(100, timeCreated: eventTime);

        var dateFilter = new DateFilter
        {
            IsEnabled = false,
            After = DateTime.Now.AddDays(-1),
            Before = DateTime.Now.AddDays(1)
        };

        // Act
        var result = @event.MatchesDateFilter(dateFilter);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MatchesDateFilter_WhenDateFilterIsNull_ShouldNotConstrainEvent()
    {
        // Arrange
        var @event = FilterEventBuilder.CreateTestEvent(100);

        // Act
        var result = @event.MatchesDateFilter(null);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MatchesDateFilter_WhenEventAfterRange_ShouldFailDateConstraint()
    {
        // Arrange
        var eventTime = DateTime.Now.AddDays(2);
        var @event = FilterEventBuilder.CreateTestEvent(100, timeCreated: eventTime);

        var dateFilter = new DateFilter
        {
            After = DateTime.Now.AddDays(-1),
            Before = DateTime.Now.AddDays(1)
        };

        // Act
        var result = @event.MatchesDateFilter(dateFilter);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void MatchesDateFilter_WhenEventBeforeRange_ShouldFailDateConstraint()
    {
        // Arrange
        var eventTime = DateTime.Now.AddDays(-2);
        var @event = FilterEventBuilder.CreateTestEvent(100, timeCreated: eventTime);

        var dateFilter = new DateFilter
        {
            After = DateTime.Now.AddDays(-1),
            Before = DateTime.Now.AddDays(1)
        };

        // Act
        var result = @event.MatchesDateFilter(dateFilter);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void MatchesDateFilter_WhenEventExactlyAtAfter_ShouldTreatBoundaryAsInclusive()
    {
        // Arrange
        var boundaryTime = DateTime.Now;
        var @event = FilterEventBuilder.CreateTestEvent(100, timeCreated: boundaryTime);

        var dateFilter = new DateFilter
        {
            After = boundaryTime,
            Before = boundaryTime.AddHours(1)
        };

        // Act
        var result = @event.MatchesDateFilter(dateFilter);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MatchesDateFilter_WhenEventExactlyAtBefore_ShouldTreatBoundaryAsInclusive()
    {
        // Arrange
        var boundaryTime = DateTime.Now;
        var @event = FilterEventBuilder.CreateTestEvent(100, timeCreated: boundaryTime);

        var dateFilter = new DateFilter
        {
            After = boundaryTime.AddHours(-1),
            Before = boundaryTime
        };

        // Act
        var result = @event.MatchesDateFilter(dateFilter);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MatchesDateFilter_WhenEventIsNull_ShouldRejectNullEvent()
    {
        // Arrange
        ResolvedEvent? @event = null;

        var dateFilter = new DateFilter
        {
            After = DateTime.Now.AddDays(-1),
            Before = DateTime.Now.AddDays(1)
        };

        // Act
        var result = @event.MatchesDateFilter(dateFilter);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void MatchesDateFilter_WhenEventWithinRange_ShouldSatisfyDateConstraint()
    {
        // Arrange
        var eventTime = DateTime.Now;
        var @event = FilterEventBuilder.CreateTestEvent(100, timeCreated: eventTime);

        var dateFilter = new DateFilter
        {
            After = eventTime.AddHours(-1),
            Before = eventTime.AddHours(1)
        };

        // Act
        var result = @event.MatchesDateFilter(dateFilter);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MatchesFilters_WhenAnyIncludeFilterMatches_ShouldIncludeEvent()
    {
        // Arrange
        var @event = FilterEventBuilder.CreateTestEvent(200);

        var filters = new List<SavedFilter>
        {
            CreateFilter(FilterTestConstants.FilterIdEquals100),
            CreateFilter(FilterTestConstants.FilterIdEquals200)
        };

        // Act
        var result = @event.MatchesFilters(filters);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MatchesFilters_WhenEventIsNull_ShouldRejectNullEvent()
    {
        // Arrange
        ResolvedEvent? @event = null;
        var filters = new List<SavedFilter> { CreateFilter(FilterTestConstants.FilterIdEquals100) };

        // Act
        var result = @event.MatchesFilters(filters);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void MatchesFilters_WhenExcludedFilterDoesNotMatch_ShouldIncludeEvent()
    {
        // Arrange
        var @event = FilterEventBuilder.CreateTestEvent(200);
        var filter = CreateFilter(FilterTestConstants.FilterIdEquals100, true);
        var filters = new List<SavedFilter> { filter };

        // Act
        var result = @event.MatchesFilters(filters);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MatchesFilters_WhenExcludedFilterMatches_ShouldExcludeEvent()
    {
        // Arrange
        var @event = FilterEventBuilder.CreateTestEvent(100);
        var filter = CreateFilter(FilterTestConstants.FilterIdEquals100, true);
        var filters = new List<SavedFilter> { filter };

        // Act
        var result = @event.MatchesFilters(filters);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void MatchesFilters_WhenFilterListIsEmpty_ShouldIncludeAllEvents()
    {
        // Arrange
        var @event = FilterEventBuilder.CreateTestEvent(100);
        var filters = new List<SavedFilter>();

        // Act
        var result = @event.MatchesFilters(filters);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MatchesFilters_WhenIncludeAndExcludeFilters_ExcludeTakesPriority()
    {
        // Arrange
        var @event = FilterEventBuilder.CreateTestEvent(100, level: FilterTestConstants.EventLevelError);
        var includeFilter = CreateFilter(FilterTestConstants.FilterIdEquals100);
        var excludeFilter = CreateFilter(FilterTestConstants.FilterLevelEqualsError, true);
        var filters = new List<SavedFilter> { includeFilter, excludeFilter };

        // Act
        var result = @event.MatchesFilters(filters);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void MatchesFilters_WhenIncludeFilterDoesNotMatch_ShouldExcludeEvent()
    {
        // Arrange
        var @event = FilterEventBuilder.CreateTestEvent(200);
        var filters = new List<SavedFilter> { CreateFilter(FilterTestConstants.FilterIdEquals100) };

        // Act
        var result = @event.MatchesFilters(filters);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void MatchesFilters_WhenIncludeFilterHasNullCompiled_ShouldSkipNullCompiledFilter()
    {
        // Arrange
        var @event = FilterEventBuilder.CreateTestEvent(100);
        var filters = new List<SavedFilter>
        {
            SavedFilter.Empty,
            CreateFilter(FilterTestConstants.FilterIdEquals100)
        };

        // Act
        var result = @event.MatchesFilters(filters);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MatchesFilters_WhenIncludeFilterMatches_ShouldIncludeEvent()
    {
        // Arrange
        var @event = FilterEventBuilder.CreateTestEvent(100);
        var filters = new List<SavedFilter> { CreateFilter(FilterTestConstants.FilterIdEquals100) };

        // Act
        var result = @event.MatchesFilters(filters);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MatchesFilters_WhenIncludeMatchesAndExcludeDoesNot_ShouldIncludeEvent()
    {
        // Arrange
        var @event = FilterEventBuilder.CreateTestEvent(100);
        var filters = new List<SavedFilter>
        {
            CreateFilter(FilterTestConstants.FilterIdEquals100),
            CreateFilter(FilterTestConstants.FilterIdEquals200, true)
        };

        // Act
        var result = @event.MatchesFilters(filters);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MatchesFilters_WhenMultipleExcludesAndNoneMatch_ShouldIncludeEvent()
    {
        // Arrange
        var @event = FilterEventBuilder.CreateTestEvent(300);
        var filters = new List<SavedFilter>
        {
            CreateFilter(FilterTestConstants.FilterIdEquals100, true),
            CreateFilter(FilterTestConstants.FilterIdEquals200, true)
        };

        // Act
        var result = @event.MatchesFilters(filters);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MatchesFilters_WhenMultipleExcludesAndOneMatches_ShouldExcludeEvent()
    {
        // Arrange
        var @event = FilterEventBuilder.CreateTestEvent(100);
        var filters = new List<SavedFilter>
        {
            CreateFilter(FilterTestConstants.FilterIdEquals100, true),
            CreateFilter(FilterTestConstants.FilterIdEquals200, true)
        };

        // Act
        var result = @event.MatchesFilters(filters);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void MatchesFilters_WhenNoIncludeFilterMatches_ShouldExcludeEvent()
    {
        // Arrange
        var @event = FilterEventBuilder.CreateTestEvent(300);

        var filters = new List<SavedFilter>
        {
            CreateFilter(FilterTestConstants.FilterIdEquals100),
            CreateFilter(FilterTestConstants.FilterIdEquals200)
        };

        // Act
        var result = @event.MatchesFilters(filters);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void MatchesFilters_WhenOnlyExcludeFiltersExist_ShouldNotRequireIncludeMatch()
    {
        // Arrange
        var @event = FilterEventBuilder.CreateTestEvent(200);
        var filter = CreateFilter(FilterTestConstants.FilterIdEquals100, true);
        var filters = new List<SavedFilter> { filter };

        // Act
        var result = @event.MatchesFilters(filters);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MatchesFilters_WhenOnlyFilterHasNullCompiled_ShouldIncludeEvent()
    {
        // Arrange
        var @event = FilterEventBuilder.CreateTestEvent(100);
        var filters = new List<SavedFilter> { SavedFilter.Empty };

        // Act
        var result = @event.MatchesFilters(filters);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MatchesFilters_WhenXmlFilterDoesNotMatch_ShouldExcludeEvent()
    {
        // Arrange
        var eventWithoutXml = FilterEventBuilder.CreateTestEvent(100);
        var xmlContainsDataFilter = CreateFilter(FilterTestConstants.FilterXmlContainsData);
        var filters = new List<SavedFilter> { xmlContainsDataFilter };

        // Act
        var result = eventWithoutXml.MatchesFilters(filters);

        // Assert
        Assert.False(result);
    }

    private static SavedFilter CreateFilter(string expression, bool isExcluded = false) =>
        FilterBuilder.CreateTestFilter(expression, isExcluded: isExcluded);
}
