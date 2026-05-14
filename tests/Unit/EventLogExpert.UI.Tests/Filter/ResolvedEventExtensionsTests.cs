// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.UI.Filter;
using EventLogExpert.UI.Tests.TestUtils;
using EventLogExpert.UI.Tests.TestUtils.Constants;

namespace EventLogExpert.UI.Tests.Filter;

public sealed class ResolvedEventExtensionsTests
{
    [Fact]
    public void MatchesDateFilter_WhenDateFilterDisabled_ShouldNotConstrainEvent()
    {
        // Arrange
        var eventTime = DateTime.Now.AddYears(-10);
        var @event = EventUtils.CreateTestEvent(100, timeCreated: eventTime);

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
        var @event = EventUtils.CreateTestEvent(100);

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
        var @event = EventUtils.CreateTestEvent(100, timeCreated: eventTime);

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
        var @event = EventUtils.CreateTestEvent(100, timeCreated: eventTime);

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
        var @event = EventUtils.CreateTestEvent(100, timeCreated: boundaryTime);

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
        var @event = EventUtils.CreateTestEvent(100, timeCreated: boundaryTime);

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
        var @event = EventUtils.CreateTestEvent(100, timeCreated: eventTime);

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
        var @event = EventUtils.CreateTestEvent(200);

        var filters = new List<SavedFilter>
        {
            CreateFilter(Constants.FilterIdEquals100),
            CreateFilter(Constants.FilterIdEquals200)
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
        var filters = new List<SavedFilter> { CreateFilter(Constants.FilterIdEquals100) };

        // Act
        var result = @event.MatchesFilters(filters);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void MatchesFilters_WhenExcludedFilterDoesNotMatch_ShouldIncludeEvent()
    {
        // Arrange
        var @event = EventUtils.CreateTestEvent(200);
        var filter = CreateFilter(Constants.FilterIdEquals100, true);
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
        var @event = EventUtils.CreateTestEvent(100);
        var filter = CreateFilter(Constants.FilterIdEquals100, true);
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
        var @event = EventUtils.CreateTestEvent(100);
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
        var @event = EventUtils.CreateTestEvent(100, level: Constants.EventLevelError);
        var includeFilter = CreateFilter(Constants.FilterIdEquals100);
        var excludeFilter = CreateFilter(Constants.FilterLevelEqualsError, true);
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
        var @event = EventUtils.CreateTestEvent(200);
        var filters = new List<SavedFilter> { CreateFilter(Constants.FilterIdEquals100) };

        // Act
        var result = @event.MatchesFilters(filters);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void MatchesFilters_WhenIncludeFilterMatches_ShouldIncludeEvent()
    {
        // Arrange
        var @event = EventUtils.CreateTestEvent(100);
        var filters = new List<SavedFilter> { CreateFilter(Constants.FilterIdEquals100) };

        // Act
        var result = @event.MatchesFilters(filters);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MatchesFilters_WhenNoIncludeFilterMatches_ShouldExcludeEvent()
    {
        // Arrange
        var @event = EventUtils.CreateTestEvent(300);

        var filters = new List<SavedFilter>
        {
            CreateFilter(Constants.FilterIdEquals100),
            CreateFilter(Constants.FilterIdEquals200)
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
        var @event = EventUtils.CreateTestEvent(200);
        var filter = CreateFilter(Constants.FilterIdEquals100, true);
        var filters = new List<SavedFilter> { filter };

        // Act
        var result = @event.MatchesFilters(filters);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MatchesFilters_WhenXmlFilterDoesNotMatch_ShouldExcludeEvent()
    {
        // Arrange
        var @event = EventUtils.CreateTestEvent(100);
        var filters = new List<SavedFilter> { CreateFilter(Constants.FilterXmlContainsData) };

        // Act
        var result = @event.MatchesFilters(filters);

        // Assert
        Assert.False(result);
    }

    private static SavedFilter CreateFilter(string expression, bool isExcluded = false) =>
        FilterUtils.CreateTestFilter(expression, isExcluded: isExcluded);
}
