// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Tests.TestUtils;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests;

public sealed class FilterMethodsTests
{
    [Fact]
    public void AddFilterGroup_WhenAddingToEmptyDictionary_ShouldCreateNewEntry()
    {
        // Arrange
        var dictionary = new Dictionary<string, FilterGroupData>();
        var filterGroup = new FilterGroupModel { Name = Constants.FilterGroupName };
        var groupNames = Constants.FilterGroupName.Split('\\');

        // Act
        var result = dictionary.AddFilterGroup(groupNames, filterGroup);

        // Assert
        Assert.Single(result);
        Assert.True(result.ContainsKey(Constants.FilterGroupSection));
    }

    [Fact]
    public void AddFilterGroup_WhenAddingToExistingSection_ShouldAppendFilterGroup()
    {
        // Arrange
        var dictionary = new Dictionary<string, FilterGroupData>();
        var filterGroup1 = new FilterGroupModel { Name = Constants.FilterGroupName };
        var filterGroup2 = new FilterGroupModel { Name = "TestSection\\AnotherGroup" };

        // Act
        dictionary.AddFilterGroup(Constants.FilterGroupName.Split('\\'), filterGroup1);
        dictionary.AddFilterGroup("TestSection\\AnotherGroup".Split('\\'), filterGroup2);

        // Assert
        Assert.Single(dictionary);
        Assert.Equal(2, dictionary[Constants.FilterGroupSection].FilterGroups.Count);
    }

    [Fact]
    public void AddFilterGroup_WhenCalled_ShouldReturnSameDictionary()
    {
        // Arrange
        var dictionary = new Dictionary<string, FilterGroupData>();
        var filterGroup = new FilterGroupModel { Name = Constants.FilterGroupName };

        // Act
        var result = dictionary.AddFilterGroup(Constants.FilterGroupName.Split('\\'), filterGroup);

        // Assert
        Assert.Same(dictionary, result);
    }

    [Fact]
    public void AddFilterGroup_WhenNestedGroupNames_ShouldCreateHierarchy()
    {
        // Arrange
        var dictionary = new Dictionary<string, FilterGroupData>();
        var filterGroup = new FilterGroupModel { Name = Constants.FilterGroupNameNested };
        var groupNames = Constants.FilterGroupNameNested.Split('\\');

        // Act
        dictionary.AddFilterGroup(groupNames, filterGroup);

        // Assert
        Assert.True(dictionary.ContainsKey(Constants.FilterGroupSection));
        Assert.True(dictionary[Constants.FilterGroupSection].ChildGroup.ContainsKey(Constants.FilterGroupSubSection));
    }

    [Fact]
    public void AddFilterGroup_WhenSingleGroupName_ShouldAddToRoot()
    {
        // Arrange
        var dictionary = new Dictionary<string, FilterGroupData>();
        var filterGroup = new FilterGroupModel { Name = "SingleGroup" };
        var groupNames = new[] { "SingleGroup" };

        // Act
        dictionary.AddFilterGroup(groupNames, filterGroup);

        // Assert
        Assert.True(dictionary.ContainsKey(string.Empty));
        Assert.Single(dictionary[string.Empty].FilterGroups);
    }

    [Fact]
    public void Filter_WhenEventIsNull_ShouldReturnFalse()
    {
        // Arrange
        DisplayEventModel? @event = null;
        var filters = new List<FilterModel> { CreateFilter(Constants.FilterIdEquals100) };

        // Act
        var result = @event.Filter(filters, false);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Filter_WhenExcludedFilterDoesNotMatch_ShouldReturnTrue()
    {
        // Arrange
        var @event = EventUtils.CreateTestEvent(200);
        var filter = CreateFilter(Constants.FilterIdEquals100);
        filter.IsExcluded = true;
        var filters = new List<FilterModel> { filter };

        // Act
        var result = @event.Filter(filters, false);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Filter_WhenExcludedFilterMatches_ShouldReturnFalse()
    {
        // Arrange
        var @event = EventUtils.CreateTestEvent(100);
        var filter = CreateFilter(Constants.FilterIdEquals100);
        filter.IsExcluded = true;
        var filters = new List<FilterModel> { filter };

        // Act
        var result = @event.Filter(filters, false);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Filter_WhenFilterDoesNotMatch_ShouldReturnFalse()
    {
        // Arrange
        var @event = EventUtils.CreateTestEvent(200);
        var filters = new List<FilterModel> { CreateFilter(Constants.FilterIdEquals100) };

        // Act
        var result = @event.Filter(filters, false);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Filter_WhenFilterMatches_ShouldReturnTrue()
    {
        // Arrange
        var @event = EventUtils.CreateTestEvent(100);
        var filters = new List<FilterModel> { CreateFilter(Constants.FilterIdEquals100) };

        // Act
        var result = @event.Filter(filters, false);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Filter_WhenIncludeAndExcludeFilters_ExcludeTakesPriority()
    {
        // Arrange
        var @event = EventUtils.CreateTestEvent(100, level: Constants.EventLevelError);
        var includeFilter = CreateFilter(Constants.FilterIdEquals100);
        var excludeFilter = CreateFilter(Constants.FilterLevelEqualsError);
        excludeFilter.IsExcluded = true;
        var filters = new List<FilterModel> { includeFilter, excludeFilter };

        // Act
        var result = @event.Filter(filters, false);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Filter_WhenMultipleFiltersAnyMatch_ShouldReturnTrue()
    {
        // Arrange
        var @event = EventUtils.CreateTestEvent(200);

        var filters = new List<FilterModel>
        {
            CreateFilter(Constants.FilterIdEquals100),
            CreateFilter(Constants.FilterIdEquals200)
        };

        // Act
        var result = @event.Filter(filters, false);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Filter_WhenMultipleFiltersNoneMatch_ShouldReturnFalse()
    {
        // Arrange
        var @event = EventUtils.CreateTestEvent(300);

        var filters = new List<FilterModel>
        {
            CreateFilter(Constants.FilterIdEquals100),
            CreateFilter(Constants.FilterIdEquals200)
        };

        // Act
        var result = @event.Filter(filters, false);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Filter_WhenNoFilters_ShouldReturnTrue()
    {
        // Arrange
        var @event = EventUtils.CreateTestEvent(100);
        var filters = new List<FilterModel>();

        // Act
        var result = @event.Filter(filters, false);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Filter_WhenOnlyExcludedFilters_ShouldReturnTrue()
    {
        // Arrange
        var @event = EventUtils.CreateTestEvent(200);
        var filter = CreateFilter(Constants.FilterIdEquals100);
        filter.IsExcluded = true;
        var filters = new List<FilterModel> { filter };

        // Act
        var result = @event.Filter(filters, false);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Filter_WhenXmlFilterAndXmlDisabled_ShouldReturnFalse()
    {
        // Arrange
        var @event = EventUtils.CreateTestEvent(100);
        var filters = new List<FilterModel> { CreateFilter(Constants.FilterXmlContainsData) };

        // Act
        var result = @event.Filter(filters, false);

        // Assert
        Assert.False(result);
    }

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

        var dateFilter = new FilterDateModel
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

        var dateFilter = new FilterDateModel
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

        var dateFilter = new FilterDateModel
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

        var dateFilter = new FilterDateModel
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
        DisplayEventModel? @event = null;

        var dateFilter = new FilterDateModel
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

        var dateFilter = new FilterDateModel
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
    public void HasFilteringChanged_WhenBothEmpty_ShouldReturnFalse()
    {
        // Arrange
        var original = new EventFilter(null, []);
        var updated = new EventFilter(null, []);

        // Act
        var result = FilterMethods.HasFilteringChanged(updated, original);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasFilteringChanged_WhenDateFilterAdded_ShouldReturnTrue()
    {
        // Arrange
        var original = new EventFilter(null, []);
        var dateFilter = new FilterDateModel { After = DateTime.Now.AddDays(-1), Before = DateTime.Now };
        var updated = new EventFilter(dateFilter, []);

        // Act
        var result = FilterMethods.HasFilteringChanged(updated, original);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasFilteringChanged_WhenDateFilterDifferent_ShouldReturnTrue()
    {
        // Arrange
        var dateFilter1 = new FilterDateModel { After = DateTime.Now.AddDays(-1), Before = DateTime.Now };
        var dateFilter2 = new FilterDateModel { After = DateTime.Now.AddDays(-2), Before = DateTime.Now };
        var original = new EventFilter(dateFilter1, []);
        var updated = new EventFilter(dateFilter2, []);

        // Act
        var result = FilterMethods.HasFilteringChanged(updated, original);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasFilteringChanged_WhenDateFilterRemoved_ShouldReturnTrue()
    {
        // Arrange
        var dateFilter = new FilterDateModel { After = DateTime.Now.AddDays(-1), Before = DateTime.Now };
        var original = new EventFilter(dateFilter, []);
        var updated = new EventFilter(null, []);

        // Act
        var result = FilterMethods.HasFilteringChanged(updated, original);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasFilteringChanged_WhenFiltersAdded_ShouldReturnTrue()
    {
        // Arrange
        var original = new EventFilter(null, []);
        var filter = CreateFilter(Constants.FilterIdEquals100);
        var updated = new EventFilter(null, [filter]);

        // Act
        var result = FilterMethods.HasFilteringChanged(updated, original);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasFilteringChanged_WhenFiltersRemoved_ShouldReturnTrue()
    {
        // Arrange
        var filter = CreateFilter(Constants.FilterIdEquals100);
        var original = new EventFilter(null, [filter]);
        var updated = new EventFilter(null, []);

        // Act
        var result = FilterMethods.HasFilteringChanged(updated, original);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasFilteringChanged_WhenSameFilters_ShouldReturnFalse()
    {
        // Arrange
        var filters = ImmutableList.Create(CreateFilter(Constants.FilterIdEquals100));
        var original = new EventFilter(null, filters);
        var updated = new EventFilter(null, filters);

        // Act
        var result = FilterMethods.HasFilteringChanged(updated, original);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IndexOf_WhenDuplicateItems_ShouldReturnFirstIndex()
    {
        // Arrange
        IReadOnlyList<int> list = [1, 2, 2, 3, 2];

        // Act
        var result = list.IndexOf(2);

        // Assert
        Assert.Equal(1, result);
    }

    [Fact]
    public void IndexOf_WhenEmptyList_ShouldReturnNegativeOne()
    {
        // Arrange
        IReadOnlyList<int> list = [];

        // Act
        var result = list.IndexOf(1);

        // Assert
        Assert.Equal(-1, result);
    }

    [Fact]
    public void IndexOf_WhenItemAtEnd_ShouldReturnLastIndex()
    {
        // Arrange
        IReadOnlyList<string> list = ["first", "second", "third"];

        // Act
        var result = list.IndexOf("third");

        // Assert
        Assert.Equal(2, result);
    }

    [Fact]
    public void IndexOf_WhenItemAtStart_ShouldReturnZero()
    {
        // Arrange
        IReadOnlyList<string> list = ["first", "second", "third"];

        // Act
        var result = list.IndexOf("first");

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public void IndexOf_WhenItemExists_ShouldReturnCorrectIndex()
    {
        // Arrange
        IReadOnlyList<int> list = [1, 2, 3, 4, 5];

        // Act
        var result = list.IndexOf(3);

        // Assert
        Assert.Equal(2, result);
    }

    [Fact]
    public void IndexOf_WhenItemNotExists_ShouldReturnNegativeOne()
    {
        // Arrange
        IReadOnlyList<int> list = [1, 2, 3, 4, 5];

        // Act
        var result = list.IndexOf(10);

        // Assert
        Assert.Equal(-1, result);
    }

    [Fact]
    public void IndexOf_WhenNullItem_ShouldFindNull()
    {
        // Arrange
        IReadOnlyList<string?> list = ["first", null, "third"];

        // Act
        var result = list.IndexOf(null);

        // Assert
        Assert.Equal(1, result);
    }

    [Fact]
    public void IsFilteringEnabled_WhenBothDateFilterAndFiltersExist_ShouldReturnTrue()
    {
        // Arrange
        var dateFilter = new FilterDateModel { IsEnabled = true };
        var filter = CreateFilter(Constants.FilterIdEquals100);
        var eventFilter = new EventFilter(dateFilter, [filter]);

        // Act
        var result = FilterMethods.IsFilteringEnabled(eventFilter);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsFilteringEnabled_WhenDateFilterDisabled_ShouldReturnFalse()
    {
        // Arrange
        var dateFilter = new FilterDateModel { IsEnabled = false };
        var eventFilter = new EventFilter(dateFilter, []);

        // Act
        var result = FilterMethods.IsFilteringEnabled(eventFilter);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsFilteringEnabled_WhenDateFilterEnabled_ShouldReturnTrue()
    {
        // Arrange
        var dateFilter = new FilterDateModel { IsEnabled = true };
        var eventFilter = new EventFilter(dateFilter, []);

        // Act
        var result = FilterMethods.IsFilteringEnabled(eventFilter);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsFilteringEnabled_WhenFiltersExist_ShouldReturnTrue()
    {
        // Arrange
        var filter = CreateFilter(Constants.FilterIdEquals100);
        var eventFilter = new EventFilter(null, [filter]);

        // Act
        var result = FilterMethods.IsFilteringEnabled(eventFilter);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsFilteringEnabled_WhenNoDateFilterAndNoFilters_ShouldReturnFalse()
    {
        // Arrange
        var eventFilter = new EventFilter(null, []);

        // Act
        var result = FilterMethods.IsFilteringEnabled(eventFilter);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void SortEvents_WhenDescendingTrue_ShouldSortInDescendingOrder()
    {
        // Arrange
        var events = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(100),
            EventUtils.CreateTestEvent(300),
            EventUtils.CreateTestEvent(200)
        };

        // Act
        var result = events.SortEvents(ColumnName.EventId, true);

        // Assert
        Assert.Equal(300, result[0].Id);
        Assert.Equal(200, result[1].Id);
        Assert.Equal(100, result[2].Id);
    }

    [Fact]
    public void SortEvents_WhenEmptyCollection_ShouldReturnEmptyList()
    {
        // Arrange
        var events = new List<DisplayEventModel>();

        // Act
        var result = events.SortEvents(ColumnName.EventId);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void SortEvents_WhenNoOrderSpecified_ShouldSortByRecordIdAscending()
    {
        // Arrange
        var events = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(recordId: 3),
            EventUtils.CreateTestEvent(recordId: 1),
            EventUtils.CreateTestEvent(recordId: 2)
        };

        // Act
        var result = events.SortEvents();

        // Assert
        Assert.Equal(1L, result[0].RecordId);
        Assert.Equal(2L, result[1].RecordId);
        Assert.Equal(3L, result[2].RecordId);
    }

    [Fact]
    public void SortEvents_WhenNoOrderSpecifiedDescending_ShouldSortByRecordIdDescending()
    {
        // Arrange
        var events = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(recordId: 1),
            EventUtils.CreateTestEvent(recordId: 3),
            EventUtils.CreateTestEvent(recordId: 2)
        };

        // Act
        var result = events.SortEvents(isDescending: true);

        // Assert
        Assert.Equal(3L, result[0].RecordId);
        Assert.Equal(2L, result[1].RecordId);
        Assert.Equal(1L, result[2].RecordId);
    }

    [Fact]
    public void SortEvents_WhenOrderByActivityId_ShouldSortByActivityId()
    {
        // Arrange
        var guid1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var guid2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var guid3 = Guid.Parse("00000000-0000-0000-0000-000000000003");

        var events = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(activityId: guid3),
            EventUtils.CreateTestEvent(activityId: guid1),
            EventUtils.CreateTestEvent(activityId: guid2)
        };

        // Act
        var result = events.SortEvents(ColumnName.ActivityId);

        // Assert
        Assert.Equal(guid1, result[0].ActivityId);
        Assert.Equal(guid2, result[1].ActivityId);
        Assert.Equal(guid3, result[2].ActivityId);
    }

    [Fact]
    public void SortEvents_WhenOrderByComputerName_ShouldSortByComputerName()
    {
        // Arrange
        var events = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(computerName: "Server03"),
            EventUtils.CreateTestEvent(computerName: "Server01"),
            EventUtils.CreateTestEvent(computerName: "Server02")
        };

        // Act
        var result = events.SortEvents(ColumnName.ComputerName);

        // Assert
        Assert.Equal("Server01", result[0].ComputerName);
        Assert.Equal("Server02", result[1].ComputerName);
        Assert.Equal("Server03", result[2].ComputerName);
    }

    [Fact]
    public void SortEvents_WhenOrderByDateAndTime_ShouldSortByTimeCreated()
    {
        // Arrange
        var baseTime = DateTime.Now;

        var events = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(timeCreated: baseTime.AddHours(2)),
            EventUtils.CreateTestEvent(timeCreated: baseTime),
            EventUtils.CreateTestEvent(timeCreated: baseTime.AddHours(1))
        };

        // Act
        var result = events.SortEvents(ColumnName.DateAndTime);

        // Assert
        Assert.Equal(baseTime, result[0].TimeCreated);
        Assert.Equal(baseTime.AddHours(1), result[1].TimeCreated);
        Assert.Equal(baseTime.AddHours(2), result[2].TimeCreated);
    }

    [Fact]
    public void SortEvents_WhenOrderByEventId_ShouldSortById()
    {
        // Arrange
        var events = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(300),
            EventUtils.CreateTestEvent(100),
            EventUtils.CreateTestEvent(200)
        };

        // Act
        var result = events.SortEvents(ColumnName.EventId);

        // Assert
        Assert.Equal(100, result[0].Id);
        Assert.Equal(200, result[1].Id);
        Assert.Equal(300, result[2].Id);
    }

    [Fact]
    public void SortEvents_WhenOrderByKeywords_ShouldSortByKeywordsDisplayName()
    {
        // Arrange
        var events = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(keywordsDisplayName: "Zebra"),
            EventUtils.CreateTestEvent(keywordsDisplayName: "Apple"),
            EventUtils.CreateTestEvent(keywordsDisplayName: "Mango")
        };

        // Act
        var result = events.SortEvents(ColumnName.Keywords);

        // Assert
        Assert.Equal("Apple", result[0].KeywordsDisplayName);
        Assert.Equal("Mango", result[1].KeywordsDisplayName);
        Assert.Equal("Zebra", result[2].KeywordsDisplayName);
    }

    [Fact]
    public void SortEvents_WhenOrderByLevel_ShouldSortByLevel()
    {
        // Arrange
        var events = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(level: "Warning"),
            EventUtils.CreateTestEvent(level: "Error"),
            EventUtils.CreateTestEvent(level: "Information")
        };

        // Act
        var result = events.SortEvents(ColumnName.Level);

        // Assert
        Assert.Equal("Error", result[0].Level);
        Assert.Equal("Information", result[1].Level);
        Assert.Equal("Warning", result[2].Level);
    }

    [Fact]
    public void SortEvents_WhenOrderByLog_ShouldSortByLogName()
    {
        // Arrange
        var events = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(logName: "System"),
            EventUtils.CreateTestEvent(logName: "Application"),
            EventUtils.CreateTestEvent(logName: "Security")
        };

        // Act
        var result = events.SortEvents(ColumnName.Log);

        // Assert
        Assert.Equal("Application", result[0].LogName);
        Assert.Equal("Security", result[1].LogName);
        Assert.Equal("System", result[2].LogName);
    }

    [Fact]
    public void SortEvents_WhenOrderByProcessId_ShouldSortByProcessId()
    {
        // Arrange
        var events = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(processId: 300),
            EventUtils.CreateTestEvent(processId: 100),
            EventUtils.CreateTestEvent(processId: 200)
        };

        // Act
        var result = events.SortEvents(ColumnName.ProcessId);

        // Assert
        Assert.Equal(100, result[0].ProcessId);
        Assert.Equal(200, result[1].ProcessId);
        Assert.Equal(300, result[2].ProcessId);
    }

    [Fact]
    public void SortEvents_WhenOrderBySource_ShouldSortBySource()
    {
        // Arrange
        var events = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(source: "ZSource"),
            EventUtils.CreateTestEvent(source: "ASource"),
            EventUtils.CreateTestEvent(source: "MSource")
        };

        // Act
        var result = events.SortEvents(ColumnName.Source);

        // Assert
        Assert.Equal("ASource", result[0].Source);
        Assert.Equal("MSource", result[1].Source);
        Assert.Equal("ZSource", result[2].Source);
    }

    [Fact]
    public void SortEvents_WhenOrderByTaskCategory_ShouldSortByTaskCategory()
    {
        // Arrange
        var events = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(taskCategory: "ZCategory"),
            EventUtils.CreateTestEvent(taskCategory: "ACategory"),
            EventUtils.CreateTestEvent(taskCategory: "MCategory")
        };

        // Act
        var result = events.SortEvents(ColumnName.TaskCategory);

        // Assert
        Assert.Equal("ACategory", result[0].TaskCategory);
        Assert.Equal("MCategory", result[1].TaskCategory);
        Assert.Equal("ZCategory", result[2].TaskCategory);
    }

    [Fact]
    public void SortEvents_WhenOrderByThreadId_ShouldSortByThreadId()
    {
        // Arrange
        var events = new List<DisplayEventModel>
        {
            EventUtils.CreateTestEvent(threadId: 30),
            EventUtils.CreateTestEvent(threadId: 10),
            EventUtils.CreateTestEvent(threadId: 20)
        };

        // Act
        var result = events.SortEvents(ColumnName.ThreadId);

        // Assert
        Assert.Equal(10, result[0].ThreadId);
        Assert.Equal(20, result[1].ThreadId);
        Assert.Equal(30, result[2].ThreadId);
    }

    [Fact]
    public void SortEvents_WhenSingleItem_ShouldReturnSingleItem()
    {
        // Arrange
        var events = new List<DisplayEventModel> { EventUtils.CreateTestEvent(100) };

        // Act
        var result = events.SortEvents(ColumnName.EventId);

        // Assert
        Assert.Single(result);
        Assert.Equal(100, result[0].Id);
    }

    private static FilterModel CreateFilter(string expression, bool isExcluded = false)
    {
        return new FilterModel
        {
            Comparison = new FilterComparison { Value = expression },
            IsExcluded = isExcluded
        };
    }
}
