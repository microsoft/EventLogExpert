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
    public void FilterByDate_WhenDateFilterIsNull_ShouldReturnEvent()
    {
        // Arrange
        var @event = EventUtils.CreateTestEvent(100);

        // Act
        var result = @event.FilterByDate(null);

        // Assert
        Assert.Same(@event, result);
    }

    [Fact]
    public void FilterByDate_WhenEventAfterRange_ShouldReturnNull()
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
        var result = @event.FilterByDate(dateFilter);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void FilterByDate_WhenEventBeforeRange_ShouldReturnNull()
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
        var result = @event.FilterByDate(dateFilter);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void FilterByDate_WhenEventExactlyAtAfter_ShouldReturnEvent()
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
        var result = @event.FilterByDate(dateFilter);

        // Assert
        Assert.Same(@event, result);
    }

    [Fact]
    public void FilterByDate_WhenEventExactlyAtBefore_ShouldReturnEvent()
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
        var result = @event.FilterByDate(dateFilter);

        // Assert
        Assert.Same(@event, result);
    }

    [Fact]
    public void FilterByDate_WhenEventIsNull_ShouldReturnNull()
    {
        // Arrange
        ResolvedEvent? @event = null;

        var dateFilter = new DateFilter
        {
            After = DateTime.Now.AddDays(-1),
            Before = DateTime.Now.AddDays(1)
        };

        // Act
        var result = @event.FilterByDate(dateFilter);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void FilterByDate_WhenEventWithinRange_ShouldReturnEvent()
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
        var result = @event.FilterByDate(dateFilter);

        // Assert
        Assert.Same(@event, result);
    }

    [Fact]
    public void Filter_WhenEventIsNull_ShouldReturnFalse()
    {
        // Arrange
        ResolvedEvent? @event = null;
        var filters = new List<SavedFilter> { CreateFilter(Constants.FilterIdEquals100) };

        // Act
        var result = @event.Filter(filters);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Filter_WhenExcludedFilterDoesNotMatch_ShouldReturnTrue()
    {
        // Arrange
        var @event = EventUtils.CreateTestEvent(200);
        var filter = CreateFilter(Constants.FilterIdEquals100, true);
        var filters = new List<SavedFilter> { filter };

        // Act
        var result = @event.Filter(filters);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Filter_WhenExcludedFilterMatches_ShouldReturnFalse()
    {
        // Arrange
        var @event = EventUtils.CreateTestEvent(100);
        var filter = CreateFilter(Constants.FilterIdEquals100, true);
        var filters = new List<SavedFilter> { filter };

        // Act
        var result = @event.Filter(filters);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Filter_WhenFilterDoesNotMatch_ShouldReturnFalse()
    {
        // Arrange
        var @event = EventUtils.CreateTestEvent(200);
        var filters = new List<SavedFilter> { CreateFilter(Constants.FilterIdEquals100) };

        // Act
        var result = @event.Filter(filters);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Filter_WhenFilterMatches_ShouldReturnTrue()
    {
        // Arrange
        var @event = EventUtils.CreateTestEvent(100);
        var filters = new List<SavedFilter> { CreateFilter(Constants.FilterIdEquals100) };

        // Act
        var result = @event.Filter(filters);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Filter_WhenIncludeAndExcludeFilters_ExcludeTakesPriority()
    {
        // Arrange
        var @event = EventUtils.CreateTestEvent(100, level: Constants.EventLevelError);
        var includeFilter = CreateFilter(Constants.FilterIdEquals100);
        var excludeFilter = CreateFilter(Constants.FilterLevelEqualsError, true);
        var filters = new List<SavedFilter> { includeFilter, excludeFilter };

        // Act
        var result = @event.Filter(filters);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Filter_WhenMultipleFiltersAnyMatch_ShouldReturnTrue()
    {
        // Arrange
        var @event = EventUtils.CreateTestEvent(200);

        var filters = new List<SavedFilter>
        {
            CreateFilter(Constants.FilterIdEquals100),
            CreateFilter(Constants.FilterIdEquals200)
        };

        // Act
        var result = @event.Filter(filters);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Filter_WhenMultipleFiltersNoneMatch_ShouldReturnFalse()
    {
        // Arrange
        var @event = EventUtils.CreateTestEvent(300);

        var filters = new List<SavedFilter>
        {
            CreateFilter(Constants.FilterIdEquals100),
            CreateFilter(Constants.FilterIdEquals200)
        };

        // Act
        var result = @event.Filter(filters);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Filter_WhenNoFilters_ShouldReturnTrue()
    {
        // Arrange
        var @event = EventUtils.CreateTestEvent(100);
        var filters = new List<SavedFilter>();

        // Act
        var result = @event.Filter(filters);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Filter_WhenOnlyExcludedFilters_ShouldReturnTrue()
    {
        // Arrange
        var @event = EventUtils.CreateTestEvent(200);
        var filter = CreateFilter(Constants.FilterIdEquals100, true);
        var filters = new List<SavedFilter> { filter };

        // Act
        var result = @event.Filter(filters);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Filter_WhenXmlFilterDoesNotMatch_ShouldReturnFalse()
    {
        // Arrange
        var @event = EventUtils.CreateTestEvent(100);
        var filters = new List<SavedFilter> { CreateFilter(Constants.FilterXmlContainsData) };

        // Act
        var result = @event.Filter(filters);

        // Assert
        Assert.False(result);
    }

    private static SavedFilter CreateFilter(string expression, bool isExcluded = false) =>
        FilterUtils.CreateTestFilter(comparisonValue: expression, isExcluded: isExcluded);
}
