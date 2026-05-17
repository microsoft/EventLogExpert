// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Tests.TestUtils;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class ResolvedEventOrderingTests
{
    [Fact]
    public void SortEvents_WhenDescendingTrue_ShouldSortInDescendingOrder()
    {
        // Arrange
        var events = new List<ResolvedEvent>
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
        var events = new List<ResolvedEvent>();

        // Act
        var result = events.SortEvents(ColumnName.EventId);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void SortEvents_WhenNoOrderSpecifiedDescending_ShouldSortByRecordIdDescending()
    {
        // Arrange
        var events = new List<ResolvedEvent>
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
    public void SortEvents_WhenNoOrderSpecified_ShouldSortByRecordIdAscending()
    {
        // Arrange
        var events = new List<ResolvedEvent>
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
    public void SortEvents_WhenOrderByActivityId_ShouldSortByActivityId()
    {
        // Arrange
        var guid1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var guid2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var guid3 = Guid.Parse("00000000-0000-0000-0000-000000000003");

        var events = new List<ResolvedEvent>
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
        var events = new List<ResolvedEvent>
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

        var events = new List<ResolvedEvent>
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
        var events = new List<ResolvedEvent>
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
        var events = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(keywords: ["Zebra"]),
            EventUtils.CreateTestEvent(keywords: ["Apple"]),
            EventUtils.CreateTestEvent(keywords: ["Mango"])
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
        var events = new List<ResolvedEvent>
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
        var events = new List<ResolvedEvent>
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
        var events = new List<ResolvedEvent>
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
        var events = new List<ResolvedEvent>
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
        var events = new List<ResolvedEvent>
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
        var events = new List<ResolvedEvent>
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
        var events = new List<ResolvedEvent> { EventUtils.CreateTestEvent(100) };

        // Act
        var result = events.SortEvents(ColumnName.EventId);

        // Assert
        Assert.Single(result);
        Assert.Equal(100, result[0].Id);
    }

    [Fact]
    public void SortEvents_WhenTiedOnPrimaryKeyDescending_ShouldBreakTieByRecordIdDescending()
    {
        // Arrange
        var events = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(100, recordId: 1),
            EventUtils.CreateTestEvent(100, recordId: 3),
            EventUtils.CreateTestEvent(100, recordId: 2)
        };

        // Act
        var result = events.SortEvents(ColumnName.EventId, true);

        // Assert - tied on Id, should fall back to RecordId descending
        Assert.Equal(3L, result[0].RecordId);
        Assert.Equal(2L, result[1].RecordId);
        Assert.Equal(1L, result[2].RecordId);
    }

    [Fact]
    public void SortEvents_WhenTiedOnPrimaryKey_ShouldBreakTieByRecordId()
    {
        // Arrange - all events have the same TimeCreated but different RecordIds
        var baseTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var events = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(timeCreated: baseTime, recordId: 3),
            EventUtils.CreateTestEvent(timeCreated: baseTime, recordId: 1),
            EventUtils.CreateTestEvent(timeCreated: baseTime, recordId: 2)
        };

        // Act
        var result = events.SortEvents(ColumnName.DateAndTime);

        // Assert - should be deterministic, ordered by RecordId ascending
        Assert.Equal(1L, result[0].RecordId);
        Assert.Equal(2L, result[1].RecordId);
        Assert.Equal(3L, result[2].RecordId);
    }
}
