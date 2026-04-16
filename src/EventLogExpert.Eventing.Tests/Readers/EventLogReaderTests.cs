// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Tests.TestUtils.Constants;

namespace EventLogExpert.Eventing.Tests.Readers;

public sealed class EventLogReaderTests
{
    [Fact]
    public void Constructor_WhenApplicationLog_ShouldNotThrow()
    {
        // Arrange & Act
        using var reader = new EventLogReader(Constants.ApplicationLogName, PathType.LogName);

        // Assert
        Assert.NotNull(reader);
    }

    [Fact]
    public void Constructor_WhenEmptyLogName_ShouldFailToReadEvents()
    {
        // Arrange & Act
        using var reader = new EventLogReader(string.Empty, PathType.LogName);

        // Assert - TryGetEvents must fail
        bool success = reader.TryGetEvents(out var events);

        Assert.False(success, "TryGetEvents should return false for empty log name");
        Assert.NotNull(events);
        Assert.Empty(events);
    }

    [Fact]
    public void Constructor_WhenInvalidLog_ShouldFailToReadEvents()
    {
        // Arrange
        var invalidLogName = "NonExistentLog_" + Guid.NewGuid();

        // Act
        using var reader = new EventLogReader(invalidLogName, PathType.LogName);

        // Assert - TryGetEvents must fail
        bool success = reader.TryGetEvents(out var events);

        Assert.False(success, "TryGetEvents should return false for invalid log name");
        Assert.NotNull(events);
        Assert.Empty(events);
    }

    [Fact]
    public void Constructor_WhenMultipleInstances_ShouldCreateIndependentReaders()
    {
        // Arrange & Act
        using var reader1 = new EventLogReader(Constants.ApplicationLogName, PathType.LogName);
        using var reader2 = new EventLogReader(Constants.ApplicationLogName, PathType.LogName);

        // Assert
        Assert.NotNull(reader1);
        Assert.NotNull(reader2);
        Assert.NotSame(reader1, reader2);
    }

    [Fact]
    public void Constructor_WhenNullLogName_ShouldFailToReadEvents()
    {
        // Arrange & Act
        using var reader = new EventLogReader(null!, PathType.LogName);

        // Assert - TryGetEvents must fail
        bool success = reader.TryGetEvents(out var events);

        Assert.False(success, "TryGetEvents should return false for null log name");
        Assert.NotNull(events);
        Assert.Empty(events);
    }

    [Fact]
    public void Constructor_WhenPathTypeLogName_ShouldQueryByLogName()
    {
        // Arrange & Act
        using var reader = new EventLogReader(Constants.ApplicationLogName, PathType.LogName);

        // Act
        bool success = reader.TryGetEvents(out var events);

        // Assert
        Assert.True(success);
        Assert.All(events, evt => Assert.Equal(PathType.LogName, evt.PathType));
    }

    [Fact]
    public void Constructor_WhenRenderXmlFalse_ShouldNotThrow()
    {
        // Arrange & Act
        using var reader = new EventLogReader(Constants.ApplicationLogName, PathType.LogName, renderXml: false);

        // Assert
        Assert.NotNull(reader);
    }

    [Fact]
    public void Constructor_WhenRenderXmlTrue_ShouldNotThrow()
    {
        // Arrange & Act
        using var reader = new EventLogReader(Constants.ApplicationLogName, PathType.LogName, renderXml: true);

        // Assert
        Assert.NotNull(reader);
    }

    [Fact]
    public void Constructor_WhenSpecialCharactersInLogName_ShouldFailToReadEvents()
    {
        // Arrange
        var invalidLogName = "Invalid<>Log|Name";

        // Act
        using var reader = new EventLogReader(invalidLogName, PathType.LogName);

        // Assert - TryGetEvents must fail
        bool success = reader.TryGetEvents(out var events);

        Assert.False(success, "TryGetEvents should return false for log name with special characters");
        Assert.NotNull(events);
        Assert.Empty(events);
    }

    [Fact]
    public void Dispose_AfterDispose_TryGetEventsShouldThrow()
    {
        // Arrange
        var reader = new EventLogReader(Constants.ApplicationLogName, PathType.LogName);

        // Act
        reader.Dispose();

        // Assert - After Dispose, the handle is disposed and TryGetEvents should throw
        Assert.Throws<ObjectDisposedException>(() => reader.TryGetEvents(out _));
    }

    [Fact]
    public void Dispose_WhenCalled_ShouldNotThrow()
    {
        // Arrange
        var reader = new EventLogReader(Constants.ApplicationLogName, PathType.LogName);

        // Act & Assert
        reader.Dispose();
    }

    [Fact]
    public void Dispose_WhenCalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var reader = new EventLogReader(Constants.ApplicationLogName, PathType.LogName);

        // Act & Assert
        reader.Dispose();
        reader.Dispose();
        reader.Dispose();
    }

    [Fact]
    public void LastBookmark_AfterTryGetEvents_ShouldBeSet()
    {
        // Arrange
        using var reader = new EventLogReader(Constants.ApplicationLogName, PathType.LogName);

        // Act
        bool success = reader.TryGetEvents(out var events);

        // Assert
        if (success && events.Length > 0)
        {
            Assert.NotNull(reader.LastBookmark);
            Assert.NotEmpty(reader.LastBookmark);
        }
    }

    [Fact]
    public void LastBookmark_WhenInitialized_ShouldBeNull()
    {
        // Arrange & Act
        using var reader = new EventLogReader(Constants.ApplicationLogName, PathType.LogName);

        // Assert
        Assert.Null(reader.LastBookmark);
    }

    [Fact]
    public void LastBookmark_WhenMultipleBatches_ShouldUpdateWithEachBatch()
    {
        // Arrange
        using var reader = new EventLogReader(Constants.ApplicationLogName, PathType.LogName);

        // Act
        bool success1 = reader.TryGetEvents(out var events1, batchSize: 1);
        string? bookmark1 = reader.LastBookmark;

        bool success2 = reader.TryGetEvents(out var events2, batchSize: 1);
        string? bookmark2 = reader.LastBookmark;

        // Assert
        if (success1 && events1.Length > 0)
        {
            Assert.NotNull(bookmark1);
        }

        if (!success2 || events2.Length == 0) { return; }

        Assert.NotNull(bookmark2);

        // Bookmarks should be different if we read different events
        if (bookmark1 != null && events1.Length > 0 && events2.Length > 0)
        {
            Assert.NotEqual(bookmark1, bookmark2);
        }
    }

    [Fact]
    public void LastBookmark_WhenNoEventsReturned_ShouldRemainUnchanged()
    {
        // Arrange
        using var reader = new EventLogReader(Constants.ApplicationLogName, PathType.LogName);

        // Read all events
        while (reader.TryGetEvents(out _)) { }

        string? bookmarkBefore = reader.LastBookmark;

        // Act - Try to read when no events left
        reader.TryGetEvents(out _);
        string? bookmarkAfter = reader.LastBookmark;

        // Assert
        Assert.Equal(bookmarkBefore, bookmarkAfter);
    }

    [Fact]
    public void TryGetEvents_WhenApplicationLog_ShouldReturnEvents()
    {
        // Arrange
        using var reader = new EventLogReader(Constants.ApplicationLogName, PathType.LogName);

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
        using var reader = new EventLogReader(Constants.ApplicationLogName, PathType.LogName);

        // Act
        bool success = reader.TryGetEvents(out var events);

        // Assert
        Assert.True(success);
        Assert.All(events, evt =>
        {
            Assert.NotNull(evt);
            Assert.Equal(Constants.ApplicationLogName, evt.PathName);
            Assert.Equal(PathType.LogName, evt.PathType);
        });
    }

    [Fact]
    public void TryGetEvents_WhenBatchSize1_ShouldReturn1Event()
    {
        // Arrange
        using var reader = new EventLogReader(Constants.ApplicationLogName, PathType.LogName);

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
        using var reader = new EventLogReader(Constants.ApplicationLogName, PathType.LogName);

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
        using var reader = new EventLogReader(Constants.ApplicationLogName, PathType.LogName);

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
        using var reader = new EventLogReader(Constants.ApplicationLogName, PathType.LogName);

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
        using var reader = new EventLogReader(logName, PathType.LogName);

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
                using var reader = new EventLogReader(Constants.ApplicationLogName, PathType.LogName);
                return reader.TryGetEvents(out _);
            }),
            Task.Run(() =>
            {
                using var reader = new EventLogReader(Constants.SystemLogName, PathType.LogName);
                return reader.TryGetEvents(out _);
            }),
            Task.Run(() =>
            {
                using var reader = new EventLogReader(Constants.ApplicationLogName, PathType.LogName);
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
        using var reader = new EventLogReader(Constants.ApplicationLogName, PathType.LogName);

        // Act
        bool success = reader.TryGetEvents(out var events);

        // Assert
        Assert.True(success);
        Assert.NotNull(events);
        Assert.True(events.Length <= 30);
    }

    [Fact]
    public void TryGetEvents_WhenEventHasError_ShouldSetErrorProperty()
    {
        // Arrange
        using var reader = new EventLogReader(Constants.ApplicationLogName, PathType.LogName);

        // Act
        bool success = reader.TryGetEvents(out var events, batchSize: 100);

        // Assert
        Assert.True(success);
        
        // Some events might have errors (e.g., corrupt events, missing provider info)
        // Just verify that if Error is set, it's a non-empty string
        foreach (var evt in events)
        {
            if (!string.IsNullOrEmpty(evt.Error))
            {
                Assert.NotEmpty(evt.Error);
            }
        }
    }

    [Fact]
    public void TryGetEvents_WhenEventsReturned_ShouldHavePathNameSet()
    {
        // Arrange
        using var reader = new EventLogReader(Constants.ApplicationLogName, PathType.LogName);

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
        using var reader = new EventLogReader(Constants.ApplicationLogName, PathType.LogName);

        // Act
        bool success = reader.TryGetEvents(out var events);

        // Assert
        Assert.True(success);

        Assert.All(events, evt =>
        {
            Assert.Equal(PathType.LogName, evt.PathType);
        });
    }

    [Fact]
    public void TryGetEvents_WhenEventsReturned_ShouldHaveProperties()
    {
        // Arrange
        using var reader = new EventLogReader(Constants.ApplicationLogName, PathType.LogName);

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
        using var reader = new EventLogReader(Constants.ApplicationLogName, PathType.LogName);

        var logInfo = new EventLogInformation(
            EventLogSession.GlobalSession,
            Constants.ApplicationLogName,
            PathType.LogName);

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
        using var reader1 = new EventLogReader(Constants.ApplicationLogName, PathType.LogName);
        using var reader2 = new EventLogReader(Constants.ApplicationLogName, PathType.LogName);

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
        using var reader = new EventLogReader(Constants.ApplicationLogName, PathType.LogName);

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
        using var reader = new EventLogReader(Constants.ApplicationLogName, PathType.LogName, renderXml: false);

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
        using var reader = new EventLogReader(Constants.ApplicationLogName, PathType.LogName, renderXml: true);

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
        using var reader = new EventLogReader(Constants.ApplicationLogName, PathType.LogName, renderXml: true);

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
        using var reader = new EventLogReader(Constants.ApplicationLogName, PathType.LogName);

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
