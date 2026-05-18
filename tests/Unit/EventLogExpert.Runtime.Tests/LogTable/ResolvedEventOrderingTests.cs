// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Tests.TestUtils;
using System.Security.Principal;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class ResolvedEventOrderingTests
{
    [Fact]
    public void MergeSorted_WhenBatchIsEmpty_ShouldReturnExistingUnchanged()
    {
        var existing = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(100),
            EventUtils.CreateTestEvent(200)
        }.AsReadOnly();
        var batch = new List<ResolvedEvent>().AsReadOnly();

        var result = ResolvedEventOrdering.MergeSorted(existing, batch, ColumnName.EventId, false);

        Assert.Same(existing, result);
    }

    [Fact]
    public void MergeSorted_WhenDescending_ShouldMergeInDescendingOrder()
    {
        var existing = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(400),
            EventUtils.CreateTestEvent(200)
        }.AsReadOnly();
        var batch = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(300),
            EventUtils.CreateTestEvent(100)
        }.AsReadOnly();

        var result = ResolvedEventOrdering.MergeSorted(existing, batch, ColumnName.EventId, true);

        Assert.Equal(new[] { 400, 300, 200, 100 }, result.Select(e => e.Id));
    }

    [Fact]
    public void MergeSorted_WhenExistingIsEmpty_ShouldReturnSortedBatch()
    {
        var existing = new List<ResolvedEvent>().AsReadOnly();
        var batch = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(300),
            EventUtils.CreateTestEvent(100),
            EventUtils.CreateTestEvent(200)
        }.AsReadOnly();

        var result = ResolvedEventOrdering.MergeSorted(existing, batch, ColumnName.EventId, false);

        Assert.Equal(3, result.Count);
        Assert.Equal(100, result[0].Id);
        Assert.Equal(200, result[1].Id);
        Assert.Equal(300, result[2].Id);
    }

    [Fact]
    public void MergeSorted_WhenKeysInterleave_ShouldMergeInOrder()
    {
        var existing = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(100),
            EventUtils.CreateTestEvent(300)
        }.AsReadOnly();
        var batch = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(200),
            EventUtils.CreateTestEvent(400)
        }.AsReadOnly();

        var result = ResolvedEventOrdering.MergeSorted(existing, batch, ColumnName.EventId, false);

        Assert.Equal(new[] { 100, 200, 300, 400 }, result.Select(e => e.Id));
    }

    [Fact]
    public void MergeSorted_WhenKeysTie_ShouldPlaceExistingBeforeBatchForStability()
    {
        var existingA = EventUtils.CreateTestEvent(100, recordId: 10);
        var batchB = EventUtils.CreateTestEvent(100, recordId: 11);

        var existing = new List<ResolvedEvent> { existingA }.AsReadOnly();
        var batch = new List<ResolvedEvent> { batchB }.AsReadOnly();

        var result = ResolvedEventOrdering.MergeSorted(existing, batch, ColumnName.EventId, false);

        Assert.Equal(2, result.Count);
        Assert.Equal(10L, result[0].RecordId);
        Assert.Equal(11L, result[1].RecordId);
    }

    [Fact]
    public void MergeSorted_WhenOrderingByComputerName_ShouldUseSelectedColumnComparer()
    {
        var existing = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(computerName: "Alpha"),
            EventUtils.CreateTestEvent(computerName: "Charlie")
        }.AsReadOnly();
        var batch = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(computerName: "Bravo"),
            EventUtils.CreateTestEvent(computerName: "Delta")
        }.AsReadOnly();

        var result = ResolvedEventOrdering.MergeSorted(existing, batch, ColumnName.ComputerName, false);

        Assert.Equal(new[] { "Alpha", "Bravo", "Charlie", "Delta" }, result.Select(e => e.ComputerName));
    }

    [Fact]
    public void MergeSorted_WhenRangesAreDisjoint_ShouldMergeInOrder()
    {
        var existing = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(100),
            EventUtils.CreateTestEvent(200)
        }.AsReadOnly();
        var batch = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(300),
            EventUtils.CreateTestEvent(400)
        }.AsReadOnly();

        var result = ResolvedEventOrdering.MergeSorted(existing, batch, ColumnName.EventId, false);

        Assert.Equal(new[] { 100, 200, 300, 400 }, result.Select(e => e.Id));
    }

    [Fact]
    public void SortEvents_WhenComputerNameContainsNull_ShouldOrderNullsBeforeValues()
    {
        var events = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(computerName: "Server02"),
            EventUtils.CreateTestEvent(computerName: null!),
            EventUtils.CreateTestEvent(computerName: "Server01")
        };

        var result = events.SortEvents(ColumnName.ComputerName);

        Assert.Null(result[0].ComputerName);
        Assert.Equal("Server01", result[1].ComputerName);
        Assert.Equal("Server02", result[2].ComputerName);
    }

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
    public void SortEvents_WhenInputMutatedAfterCall_ShouldReturnIndependentList()
    {
        var events = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(300),
            EventUtils.CreateTestEvent(100),
            EventUtils.CreateTestEvent(200)
        };

        var result = events.SortEvents(ColumnName.EventId);

        events.Clear();
        events.Add(EventUtils.CreateTestEvent(999));

        Assert.Equal(3, result.Count);
        Assert.Equal(100, result[0].Id);
        Assert.Equal(200, result[1].Id);
        Assert.Equal(300, result[2].Id);
    }

    [Fact]
    public void SortEvents_WhenKeywordsContainsNull_ShouldOrderNullsBeforeValues()
    {
        var events = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(keywords: ["BKeyword"]),
            EventUtils.CreateTestEvent(),
            EventUtils.CreateTestEvent(keywords: ["AKeyword"])
        };

        var result = events.SortEvents(ColumnName.Keywords);

        Assert.True(string.IsNullOrEmpty(result[0].KeywordsDisplayName));
        Assert.Equal("AKeyword", result[1].KeywordsDisplayName);
        Assert.Equal("BKeyword", result[2].KeywordsDisplayName);
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
    public void SortEvents_WhenOrderByActivityIdDescending_ShouldSortByActivityIdDescending()
    {
        var guid1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var guid2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var guid3 = Guid.Parse("00000000-0000-0000-0000-000000000003");

        var events = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(activityId: guid1),
            EventUtils.CreateTestEvent(activityId: guid3),
            EventUtils.CreateTestEvent(activityId: guid2)
        };

        var result = events.SortEvents(ColumnName.ActivityId, true);

        Assert.Equal(guid3, result[0].ActivityId);
        Assert.Equal(guid2, result[1].ActivityId);
        Assert.Equal(guid1, result[2].ActivityId);
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
    public void SortEvents_WhenOrderByComputerNameDescending_ShouldSortByComputerNameDescending()
    {
        var events = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(computerName: "Server01"),
            EventUtils.CreateTestEvent(computerName: "Server03"),
            EventUtils.CreateTestEvent(computerName: "Server02")
        };

        var result = events.SortEvents(ColumnName.ComputerName, true);

        Assert.Equal("Server03", result[0].ComputerName);
        Assert.Equal("Server02", result[1].ComputerName);
        Assert.Equal("Server01", result[2].ComputerName);
    }

    [Fact]
    public void SortEvents_WhenOrderByDateAndTime_ShouldSortByTimeCreated()
    {
        var baseTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

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
    public void SortEvents_WhenOrderByDateAndTimeDescending_ShouldSortByTimeCreatedDescending()
    {
        var baseTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var events = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(timeCreated: baseTime),
            EventUtils.CreateTestEvent(timeCreated: baseTime.AddHours(2)),
            EventUtils.CreateTestEvent(timeCreated: baseTime.AddHours(1))
        };

        var result = events.SortEvents(ColumnName.DateAndTime, true);

        Assert.Equal(baseTime.AddHours(2), result[0].TimeCreated);
        Assert.Equal(baseTime.AddHours(1), result[1].TimeCreated);
        Assert.Equal(baseTime, result[2].TimeCreated);
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
    public void SortEvents_WhenOrderByKeywordsDescending_ShouldSortByKeywordsDisplayNameDescending()
    {
        var events = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(keywords: ["Apple"]),
            EventUtils.CreateTestEvent(keywords: ["Zebra"]),
            EventUtils.CreateTestEvent(keywords: ["Mango"])
        };

        var result = events.SortEvents(ColumnName.Keywords, true);

        Assert.Equal("Zebra", result[0].KeywordsDisplayName);
        Assert.Equal("Mango", result[1].KeywordsDisplayName);
        Assert.Equal("Apple", result[2].KeywordsDisplayName);
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
    public void SortEvents_WhenOrderByLevelDescending_ShouldSortByLevelDescending()
    {
        var events = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(level: "Information"),
            EventUtils.CreateTestEvent(level: "Warning"),
            EventUtils.CreateTestEvent(level: "Error")
        };

        var result = events.SortEvents(ColumnName.Level, true);

        Assert.Equal("Warning", result[0].Level);
        Assert.Equal("Information", result[1].Level);
        Assert.Equal("Error", result[2].Level);
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
    public void SortEvents_WhenOrderByLogDescending_ShouldSortByLogNameDescending()
    {
        var events = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(logName: "Application"),
            EventUtils.CreateTestEvent(logName: "System"),
            EventUtils.CreateTestEvent(logName: "Security")
        };

        var result = events.SortEvents(ColumnName.Log, true);

        Assert.Equal("System", result[0].LogName);
        Assert.Equal("Security", result[1].LogName);
        Assert.Equal("Application", result[2].LogName);
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
    public void SortEvents_WhenOrderByProcessIdDescending_ShouldSortByProcessIdDescending()
    {
        var events = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(processId: 100),
            EventUtils.CreateTestEvent(processId: 300),
            EventUtils.CreateTestEvent(processId: 200)
        };

        var result = events.SortEvents(ColumnName.ProcessId, true);

        Assert.Equal(300, result[0].ProcessId);
        Assert.Equal(200, result[1].ProcessId);
        Assert.Equal(100, result[2].ProcessId);
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
    public void SortEvents_WhenOrderBySourceDescending_ShouldSortBySourceDescending()
    {
        var events = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(source: "ASource"),
            EventUtils.CreateTestEvent(source: "ZSource"),
            EventUtils.CreateTestEvent(source: "MSource")
        };

        var result = events.SortEvents(ColumnName.Source, true);

        Assert.Equal("ZSource", result[0].Source);
        Assert.Equal("MSource", result[1].Source);
        Assert.Equal("ASource", result[2].Source);
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
    public void SortEvents_WhenOrderByTaskCategoryDescending_ShouldSortByTaskCategoryDescending()
    {
        var events = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(taskCategory: "ACategory"),
            EventUtils.CreateTestEvent(taskCategory: "ZCategory"),
            EventUtils.CreateTestEvent(taskCategory: "MCategory")
        };

        var result = events.SortEvents(ColumnName.TaskCategory, true);

        Assert.Equal("ZCategory", result[0].TaskCategory);
        Assert.Equal("MCategory", result[1].TaskCategory);
        Assert.Equal("ACategory", result[2].TaskCategory);
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
    public void SortEvents_WhenOrderByThreadIdDescending_ShouldSortByThreadIdDescending()
    {
        var events = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(threadId: 10),
            EventUtils.CreateTestEvent(threadId: 30),
            EventUtils.CreateTestEvent(threadId: 20)
        };

        var result = events.SortEvents(ColumnName.ThreadId, true);

        Assert.Equal(30, result[0].ThreadId);
        Assert.Equal(20, result[1].ThreadId);
        Assert.Equal(10, result[2].ThreadId);
    }

    [Fact]
    public void SortEvents_WhenOrderByUser_ShouldSortByUserIdValue()
    {
        var sid1 = new SecurityIdentifier("S-1-5-18"); // LocalSystem
        var sid2 = new SecurityIdentifier("S-1-5-19"); // LocalService
        var sid3 = new SecurityIdentifier("S-1-5-20"); // NetworkService

        var events = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(userId: sid3),
            EventUtils.CreateTestEvent(userId: sid1),
            EventUtils.CreateTestEvent(userId: sid2)
        };

        var result = events.SortEvents(ColumnName.User);

        Assert.Equal(sid1.Value, result[0].UserId?.Value);
        Assert.Equal(sid2.Value, result[1].UserId?.Value);
        Assert.Equal(sid3.Value, result[2].UserId?.Value);
    }

    [Fact]
    public void SortEvents_WhenOrderByUserDescending_ShouldSortByUserIdValueDescending()
    {
        var sid1 = new SecurityIdentifier("S-1-5-18");
        var sid2 = new SecurityIdentifier("S-1-5-19");
        var sid3 = new SecurityIdentifier("S-1-5-20");

        var events = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(userId: sid1),
            EventUtils.CreateTestEvent(userId: sid3),
            EventUtils.CreateTestEvent(userId: sid2)
        };

        var result = events.SortEvents(ColumnName.User, true);

        Assert.Equal(sid3.Value, result[0].UserId?.Value);
        Assert.Equal(sid2.Value, result[1].UserId?.Value);
        Assert.Equal(sid1.Value, result[2].UserId?.Value);
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
    public void SortEvents_WhenStringValuesDifferOnlyInCase_ShouldOrderByOrdinalCodepoint()
    {
        var events = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(source: "apple"),
            EventUtils.CreateTestEvent(source: "Apple"),
            EventUtils.CreateTestEvent(source: "Banana")
        };

        var result = events.SortEvents(ColumnName.Source);

        Assert.Equal("Apple", result[0].Source);
        Assert.Equal("Banana", result[1].Source);
        Assert.Equal("apple", result[2].Source);
    }

    [Fact]
    public void SortEvents_WhenTaskCategoryContainsNull_ShouldOrderNullsBeforeValues()
    {
        var events = new List<ResolvedEvent>
        {
            EventUtils.CreateTestEvent(taskCategory: "BCategory"),
            EventUtils.CreateTestEvent(taskCategory: null!),
            EventUtils.CreateTestEvent(taskCategory: "ACategory")
        };

        var result = events.SortEvents(ColumnName.TaskCategory);

        Assert.Null(result[0].TaskCategory);
        Assert.Equal("ACategory", result[1].TaskCategory);
        Assert.Equal("BCategory", result[2].TaskCategory);
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
}
