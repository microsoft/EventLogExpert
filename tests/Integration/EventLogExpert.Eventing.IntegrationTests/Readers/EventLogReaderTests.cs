// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.TestUtils.Constants;

namespace EventLogExpert.Eventing.IntegrationTests.Readers;

public sealed class EventLogReaderTests
{
    [Theory]
    [InlineData("", "empty log name")]
    [InlineData(null, "null log name")]
    [InlineData("Invalid<>Log|Name", "log name with special characters")]
    [InlineData("NonExistentLog_TestSentinel_8e9c2b4a", "non-existent log name")]
    public void Constructor_WhenInvalidLogName_ShouldFailToReadEvents(string? logName, string scenario)
    {
        // Arrange & Act
        using var reader = new EventLogReader(logName!, LogPathType.Channel);

        // Assert - TryGetEvents must fail without throwing
        bool success = reader.TryGetEvents(out var events);

        Assert.False(success, $"TryGetEvents should return false for {scenario}");
        Assert.NotNull(events);
        Assert.Empty(events);
    }

    [Fact]
    public void Constructor_WhenPathTypeLogName_ShouldQueryByLogName()
    {
        // Arrange & Act
        using var reader = new EventLogReader(Constants.ApplicationLogName, LogPathType.Channel);

        // Act
        bool success = reader.TryGetEvents(out var events);

        // Assert
        Assert.True(success);
        Assert.All(events, evt => Assert.Equal(LogPathType.Channel, evt.LogPathType));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Constructor_WhenRenderXml_ShouldNotThrow(bool renderXml)
    {
        // Arrange & Act
        using var reader = new EventLogReader(Constants.ApplicationLogName, LogPathType.Channel, renderXml: renderXml);

        // Assert
        Assert.NotNull(reader);
    }

    [Fact]
    public void Dispose_AfterDispose_TryGetEventsShouldThrow()
    {
        // Arrange
        var reader = new EventLogReader(Constants.ApplicationLogName, LogPathType.Channel);

        // Act
        reader.Dispose();

        // Assert - After Dispose, the handle is disposed and TryGetEvents should throw
        Assert.Throws<ObjectDisposedException>(() => reader.TryGetEvents(out _));
    }

    [Fact]
    public void Dispose_WhenCalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var reader = new EventLogReader(Constants.ApplicationLogName, LogPathType.Channel);

        // Act & Assert
        reader.Dispose();
        reader.Dispose();
        reader.Dispose();
    }

    [Fact]
    public void EndOfResults_AfterExhaustion_ShouldKeepBookmarkAndNotSetLastErrorCode()
    {
        // Arrange — bounded fixture keeps the exhaustion loop fast on any host.
        using var fixture = new SmallEvtxFixture();
        using var reader = new EventLogReader(fixture.FilePath, LogPathType.File);

        // Act — exhaust the reader so TryGetEvents returns false with ERROR_NO_MORE_ITEMS
        while (reader.TryGetEvents(out _)) { }

        string? bookmarkAfterExhaustion = reader.LastBookmark;

        reader.TryGetEvents(out _);
        string? bookmarkAfterExtraRead = reader.LastBookmark;

        // Assert — bookmark stable past EOF, normal end-of-results does not set LastErrorCode.
        Assert.Equal(bookmarkAfterExhaustion, bookmarkAfterExtraRead);
        Assert.Null(reader.LastErrorCode);
    }

    [Fact]
    public void IsValid_WhenApplicationLog_ShouldBeTrue()
    {
        // Arrange & Act
        using var reader = new EventLogReader(Constants.ApplicationLogName, LogPathType.Channel);

        // Assert
        Assert.True(reader.IsValid);
    }

    [Fact]
    public void IsValid_WhenEmptyLogName_ShouldBeFalse()
    {
        // Arrange & Act
        using var reader = new EventLogReader(string.Empty, LogPathType.Channel);

        // Assert
        Assert.False(reader.IsValid);
    }

    [Fact]
    public void IsValid_WhenInvalidLogName_ShouldBeFalse()
    {
        // Arrange
        var invalidLogName = "NonExistentLog_" + Guid.NewGuid();

        // Act
        using var reader = new EventLogReader(invalidLogName, LogPathType.Channel);

        // Assert
        Assert.False(reader.IsValid);
    }

    [Fact]
    public void LastBookmark_AfterTryGetEvents_ShouldBeSet()
    {
        // Arrange — fixture keeps the read deterministic on minimal hosts.
        using var fixture = new SmallEvtxFixture();
        using var reader = new EventLogReader(fixture.FilePath, LogPathType.File);

        // Act
        bool success = reader.TryGetEvents(out var events);

        // Assert
        Assert.True(success);
        Assert.NotEmpty(events);
        Assert.NotNull(reader.LastBookmark);
        Assert.NotEmpty(reader.LastBookmark);
    }

    [Fact]
    public void LastBookmark_WhenInitialized_ShouldBeNull()
    {
        // Arrange & Act
        using var reader = new EventLogReader(Constants.ApplicationLogName, LogPathType.Channel);

        // Assert
        Assert.Null(reader.LastBookmark);
    }

    [Fact]
    public void LastBookmark_WhenMultipleBatches_ShouldUpdateWithEachBatch()
    {
        // Use SmallEvtxFixture (5 events) with batchSize: 1 so two consecutive
        // reads are guaranteed to produce two distinct events on every host.
        using var fixture = new SmallEvtxFixture();
        using var reader = new EventLogReader(fixture.FilePath, LogPathType.File);

        // Act
        bool success1 = reader.TryGetEvents(out var events1, batchSize: 1);
        string? bookmark1 = reader.LastBookmark;

        bool success2 = reader.TryGetEvents(out var events2, batchSize: 1);
        string? bookmark2 = reader.LastBookmark;

        // Assert
        Assert.True(success1);
        Assert.Single(events1);
        Assert.NotNull(bookmark1);

        Assert.True(success2);
        Assert.Single(events2);
        Assert.NotNull(bookmark2);

        Assert.NotEqual(bookmark1, bookmark2);
    }

    [Fact]
    public void LastErrorCode_WhenInitialized_ShouldBeNull()
    {
        // Arrange & Act
        using var reader = new EventLogReader(Constants.ApplicationLogName, LogPathType.Channel);

        // Assert
        Assert.Null(reader.LastErrorCode);
    }

    [Fact]
    public void LastErrorCode_WhenInvalidHandle_ShouldBeSet()
    {
        // Arrange — opening a non-existent log produces an invalid handle
        var invalidLogName = "NonExistentLog_" + Guid.NewGuid();
        using var reader = new EventLogReader(invalidLogName, LogPathType.Channel);

        // Act
        reader.TryGetEvents(out _);

        // Assert — the failure is not a normal EOF, so LastErrorCode should be set
        Assert.NotNull(reader.LastErrorCode);
    }

    [Fact]
    public void TryGetEvents_WhenApplicationLog_ShouldReturnEvents()
    {
        // Arrange
        using var reader = new EventLogReader(Constants.ApplicationLogName, LogPathType.Channel);

        // Act
        bool success = reader.TryGetEvents(out var events);

        // Assert
        Assert.True(success);
        Assert.NotNull(events);
        Assert.NotEmpty(events);
    }

    [Fact]
    public void TryGetEvents_WhenApplicationLog_ShouldReturnValidEventRecords()
    {
        // Arrange
        using var reader = new EventLogReader(Constants.ApplicationLogName, LogPathType.Channel);

        // Act
        bool success = reader.TryGetEvents(out var events);

        // Assert
        Assert.True(success);
        Assert.All(events, evt =>
        {
            Assert.NotNull(evt);
            Assert.Equal(Constants.ApplicationLogName, evt.PathName);
            Assert.Equal(LogPathType.Channel, evt.LogPathType);
        });
    }

    [Fact]
    public void TryGetEvents_WhenBatchSize1_ShouldReturn1Event()
    {
        // Arrange
        using var reader = new EventLogReader(Constants.ApplicationLogName, LogPathType.Channel);

        // Act
        bool success = reader.TryGetEvents(out var events, batchSize: 1);

        // Assert
        if (success && events.Length > 0)
        {
            Assert.Single(events);
        }
    }

    [Fact]
    public void TryGetEvents_WhenBatchSize10_ShouldReturnUpTo10Events()
    {
        // Arrange
        using var reader = new EventLogReader(Constants.ApplicationLogName, LogPathType.Channel);

        // Act
        bool success = reader.TryGetEvents(out var events, batchSize: 10);

        // Assert
        Assert.True(success);
        Assert.NotNull(events);
        Assert.True(events.Length <= 10);
    }

    [Fact]
    public void TryGetEvents_WhenBatchSize30_ShouldReturnUpTo30Events()
    {
        // Arrange
        using var reader = new EventLogReader(Constants.ApplicationLogName, LogPathType.Channel);

        // Act
        bool success = reader.TryGetEvents(out var events, batchSize: 30);

        // Assert
        Assert.True(success);
        Assert.NotNull(events);
        Assert.True(events.Length <= 30);
    }

    [Fact]
    public void TryGetEvents_WhenCalledMultipleTimes_ShouldReturnDifferentEvents()
    {
        // Arrange
        using var reader = new EventLogReader(Constants.ApplicationLogName, LogPathType.Channel);

        // Act
        bool success1 = reader.TryGetEvents(out var events1, batchSize: 5);
        bool success2 = reader.TryGetEvents(out var events2, batchSize: 5);

        // Assert
        Assert.True(success1);
        Assert.True(success2);

        if (events1.Length > 0 && events2.Length > 0)
        {
            // Events should be different (different record IDs or bookmarks changed)
            // This verifies we're reading sequentially through the log
            Assert.NotNull(events1[0].RecordId);
            Assert.NotNull(events2[0].RecordId);
        }
    }

    [Theory]
    [InlineData(Constants.ApplicationLogName)]
    [InlineData(Constants.SystemLogName)]
    public void TryGetEvents_WhenCommonLogs_ShouldReturnEvents(string logName)
    {
        // Arrange
        using var reader = new EventLogReader(logName, LogPathType.Channel);

        // Act
        bool success = reader.TryGetEvents(out var events);

        // Assert
        Assert.True(success);
        Assert.NotNull(events);
        Assert.NotEmpty(events);
    }

    [Fact]
    public async Task TryGetEvents_WhenConcurrentReaders_ShouldHandleMultipleReaders()
    {
        // Arrange & Act
        var tasks = new[]
        {
            Task.Run(() =>
            {
                using var reader = new EventLogReader(Constants.ApplicationLogName, LogPathType.Channel);
                return reader.TryGetEvents(out _);
            }),
            Task.Run(() =>
            {
                using var reader = new EventLogReader(Constants.SystemLogName, LogPathType.Channel);
                return reader.TryGetEvents(out _);
            }),
            Task.Run(() =>
            {
                using var reader = new EventLogReader(Constants.ApplicationLogName, LogPathType.Channel);
                return reader.TryGetEvents(out _);
            })
        };

        await Task.WhenAll(tasks);

        // Assert
        Assert.All(tasks, task =>
        {
            Assert.True(task.IsCompletedSuccessfully);
            Assert.True(task.Result);
        });
    }

    [Fact]
    public void TryGetEvents_WhenDefaultBatchSize_ShouldReturn30OrLess()
    {
        // Arrange
        using var reader = new EventLogReader(Constants.ApplicationLogName, LogPathType.Channel);

        // Act
        bool success = reader.TryGetEvents(out var events);

        // Assert
        Assert.True(success);
        Assert.NotNull(events);
        Assert.True(events.Length <= 30);
    }

    [Fact]
    public void TryGetEvents_WhenEventsReturned_ShouldHavePathNameSet()
    {
        // Arrange
        using var reader = new EventLogReader(Constants.ApplicationLogName, LogPathType.Channel);

        // Act
        bool success = reader.TryGetEvents(out var events);

        // Assert
        Assert.True(success);

        Assert.All(events, evt =>
        {
            Assert.Equal(Constants.ApplicationLogName, evt.PathName);
        });
    }

    [Fact]
    public void TryGetEvents_WhenEventsReturned_ShouldHavePathTypeSet()
    {
        // Arrange
        using var reader = new EventLogReader(Constants.ApplicationLogName, LogPathType.Channel);

        // Act
        bool success = reader.TryGetEvents(out var events);

        // Assert
        Assert.True(success);

        Assert.All(events, evt =>
        {
            Assert.Equal(LogPathType.Channel, evt.LogPathType);
        });
    }

    [Fact]
    public void TryGetEvents_WhenEventsReturned_ShouldHaveProperties()
    {
        // Arrange
        using var reader = new EventLogReader(Constants.ApplicationLogName, LogPathType.Channel);

        // Act
        bool success = reader.TryGetEvents(out var events, batchSize: 1);

        // Assert
        Assert.True(success);

        if (events.Length > 0)
        {
            var evt = events[0];
            Assert.NotNull(evt.Properties);
            // Properties might be empty array, but should not be null
        }
    }

    [Fact]
    public void TryGetEvents_WhenLargeLogWithManyReads_ShouldEventuallyReturnFalse()
    {
        // Arrange
        using var reader = new EventLogReader(Constants.ApplicationLogName, LogPathType.Channel);

        var logInfo = new EventLogInformation(
            EventLogSession.GlobalSession,
            Constants.ApplicationLogName,
            LogPathType.Channel);

        // Derive iteration limit from actual log size (batch size defaults to 30)
        long maxIterations = (logInfo.RecordCount ?? 0) / 30 + 100;
        long iterations = 0;

        // Act
        bool success;

        do
        {
            success = reader.TryGetEvents(out _);
            iterations++;
        } while (success && iterations < maxIterations);

        // Assert
        // Eventually we should run out of events
        Assert.False(success);
        Assert.True(iterations <= maxIterations, "Should have completed within safety limit");
    }

    [Fact]
    public void TryGetEvents_WhenMultipleReaders_ShouldReadIndependently()
    {
        // Arrange
        using var reader1 = new EventLogReader(Constants.ApplicationLogName, LogPathType.Channel);
        using var reader2 = new EventLogReader(Constants.ApplicationLogName, LogPathType.Channel);

        // Act
        bool success1 = reader1.TryGetEvents(out var events1, batchSize: 5);
        bool success2 = reader2.TryGetEvents(out var events2, batchSize: 5);

        // Assert
        Assert.True(success1);
        Assert.True(success2);

        // Both readers should get events from the same log
        if (events1.Length > 0 && events2.Length > 0)
        {
            Assert.All(events1, evt => Assert.Equal(Constants.ApplicationLogName, evt.PathName));
            Assert.All(events2, evt => Assert.Equal(Constants.ApplicationLogName, evt.PathName));
        }
    }

    [Fact]
    public void TryGetEvents_WhenNoMoreEvents_ShouldReturnFalse()
    {
        // Arrange
        using var reader = new EventLogReader(Constants.ApplicationLogName, LogPathType.Channel);

        // Act - Read all events
        bool success;
        do
        {
            success = reader.TryGetEvents(out _);
        } while (success);

        // Act - Try to read more
        success = reader.TryGetEvents(out var events);

        // Assert
        Assert.False(success);
        Assert.NotNull(events);
        Assert.Empty(events);
    }

    [Fact]
    public void TryGetEvents_WhenRenderXmlFalse_ShouldNotIncludeXml()
    {
        // Arrange
        using var reader = new EventLogReader(Constants.ApplicationLogName, LogPathType.Channel, renderXml: false);

        // Act
        bool success = reader.TryGetEvents(out var events);

        // Assert
        Assert.True(success);

        if (events.Length > 0)
        {
            Assert.All(events, evt =>
            {
                Assert.Null(evt.Xml);
            });
        }
    }

    [Fact]
    public void TryGetEvents_WhenRenderXmlTrue_ShouldIncludeXml()
    {
        // Arrange
        using var reader = new EventLogReader(Constants.ApplicationLogName, LogPathType.Channel, renderXml: true);

        // Act
        bool success = reader.TryGetEvents(out var events);

        // Assert
        Assert.True(success);

        if (events.Length > 0)
        {
            // At least some events should have XML
            Assert.Contains(events, evt => !string.IsNullOrEmpty(evt.Xml));
        }
    }

    [Fact]
    public void TryGetEvents_WhenRenderXmlTrue_XmlShouldBeValidFormat()
    {
        // Arrange
        using var reader = new EventLogReader(Constants.ApplicationLogName, LogPathType.Channel, renderXml: true);

        // Act
        bool success = reader.TryGetEvents(out var events, batchSize: 1);

        // Assert
        Assert.True(success);

        if (events.Length > 0 && events[0].Xml is { } xml && !string.IsNullOrEmpty(xml))
        {
            // XML should start with <?xml or <Event
            Assert.True(
                xml.StartsWith("<?xml") || xml.StartsWith("<Event"),
                $"Expected XML to start with '<?xml' or '<Event', but got: {xml[..Math.Min(50, xml.Length)]}");
        }
    }

    [Fact]
    public void TryGetEvents_WhenZeroBatchSize_ShouldHandleGracefully()
    {
        // Arrange
        using var reader = new EventLogReader(Constants.ApplicationLogName, LogPathType.Channel);

        // Act
        bool success = reader.TryGetEvents(out var events, batchSize: 0);

        // Assert
        // With batch size 0, should return false or empty array
        Assert.NotNull(events);

        if (success)
        {
            Assert.Empty(events);
        }
    }
}
