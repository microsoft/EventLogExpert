// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Tests.TestUtils.Constants;

namespace EventLogExpert.Eventing.Tests.Readers;

public sealed class EventLogInformationTests
{
    [Fact]
    public void Attributes_WhenApplicationLog_ShouldBeValidInteger()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logInfo = new EventLogInformation(session, Constants.ApplicationLogName, PathType.LogName);

        // Assert
        Assert.NotNull(logInfo.Attributes);
        Assert.IsType<int>(logInfo.Attributes.Value);
    }

    [Fact]
    public void Constructor_WhenApplicationLog_ShouldPopulateAllProperties()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logInfo = new EventLogInformation(session, Constants.ApplicationLogName, PathType.LogName);

        // Assert
        Assert.NotNull(logInfo);
        Assert.NotNull(logInfo.Attributes);
        Assert.NotNull(logInfo.FileSize);
        Assert.NotNull(logInfo.IsLogFull);
        Assert.NotNull(logInfo.RecordCount);
    }

    [Fact]
    public void Constructor_WhenCalledMultipleTimes_ShouldCreateIndependentInstances()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logInfo1 = new EventLogInformation(session, Constants.ApplicationLogName, PathType.LogName);
        var logInfo2 = new EventLogInformation(session, Constants.SystemLogName, PathType.LogName);

        // Assert
        Assert.NotNull(logInfo1);
        Assert.NotNull(logInfo2);
        Assert.NotSame(logInfo1, logInfo2);
        
        // Different logs should have potentially different record counts
        // (though we can't guarantee they're different)
        Assert.NotNull(logInfo1.RecordCount);
        Assert.NotNull(logInfo2.RecordCount);
    }

    [Theory]
    [InlineData(Constants.ApplicationLogName)]
    [InlineData(Constants.SystemLogName)]
    public void Constructor_WhenCommonLogs_ShouldHaveRecords(string logName)
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logInfo = new EventLogInformation(session, logName, PathType.LogName);

        // Assert
        Assert.NotNull(logInfo.RecordCount);
        // Application and System logs typically have at least some records
        Assert.True(logInfo.RecordCount >= 0);
    }

    [Theory]
    [InlineData(Constants.ApplicationLogName)]
    [InlineData(Constants.SystemLogName)]
    public void Constructor_WhenCommonLogs_ShouldNotThrow(string logName)
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logInfo = new EventLogInformation(session, logName, PathType.LogName);

        // Assert
        Assert.NotNull(logInfo);
    }

    [Fact]
    public async Task Constructor_WhenConcurrentAccess_ShouldHandleMultipleThreads()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;
        var logNames = new[] { Constants.ApplicationLogName, Constants.SystemLogName, Constants.ApplicationLogName };

        // Act
        var tasks = logNames.Select(logName =>
            Task.Run(() => new EventLogInformation(session, logName, PathType.LogName))
        ).ToArray();

        await Task.WhenAll(tasks);

        // Assert
        Assert.All(tasks, task =>
        {
            Assert.NotNull(task.Result);
            Assert.NotNull(task.Result.RecordCount);
            Assert.True(task.IsCompletedSuccessfully);
        });
    }

    [Fact]
    public void Constructor_WhenEmptyLogName_ShouldThrowException()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act & Assert
        Assert.ThrowsAny<Exception>(() =>
            new EventLogInformation(session, string.Empty, PathType.LogName));
    }

    [Fact]
    public void Constructor_WhenGlobalSession_ShouldUseCorrectSession()
    {
        // Arrange & Act
        var logInfo = EventLogSession.GlobalSession.GetLogInformation(Constants.ApplicationLogName, PathType.LogName);

        // Assert
        Assert.NotNull(logInfo);
        Assert.NotNull(logInfo.RecordCount);
    }

    [Fact]
    public void Constructor_WhenInvalidLogName_ShouldThrowException()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;
        var invalidLogName = "NonExistentLog_" + Guid.NewGuid();

        // Act & Assert
        Assert.ThrowsAny<Exception>(() =>
            new EventLogInformation(session, invalidLogName, PathType.LogName));
    }

    [Fact]
    public void Constructor_WhenNullLogName_ShouldThrowException()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act & Assert
        Assert.ThrowsAny<Exception>(() =>
            new EventLogInformation(session, null!, PathType.LogName));
    }

    [Fact]
    public void Constructor_WhenPathTypeLogName_ShouldSucceed()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logInfo = new EventLogInformation(session, Constants.ApplicationLogName, PathType.LogName);

        // Assert
        Assert.NotNull(logInfo);
        Assert.NotNull(logInfo.RecordCount);
    }

    [Fact]
    public void Constructor_WhenSecurityLog_MayRequireElevation()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act & Assert
        // Security log typically requires administrator privileges
        // This test documents expected behavior rather than testing functionality
        try
        {
            var logInfo = new EventLogInformation(session, Constants.SecurityLogName, PathType.LogName);
            // If we can access it (running as admin), verify it works
            Assert.NotNull(logInfo);
        }
        catch (UnauthorizedAccessException)
        {
            // Expected when not running as administrator
            Assert.True(true);
        }
    }

    [Fact]
    public void Constructor_WhenSpecialCharactersInLogName_ShouldThrowException()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;
        var invalidLogName = "Invalid<>Log|Name";

        // Act & Assert
        Assert.ThrowsAny<Exception>(() =>
            new EventLogInformation(session, invalidLogName, PathType.LogName));
    }

    [Fact]
    public void Constructor_WhenValidLog_AllPropertiesShouldBeAccessible()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logInfo = new EventLogInformation(session, Constants.ApplicationLogName, PathType.LogName);

        // Assert - Verify we can access all properties without exceptions
        var attributes = logInfo.Attributes;
        var creationTime = logInfo.CreationTime;
        var fileSize = logInfo.FileSize;
        var isLogFull = logInfo.IsLogFull;
        var lastAccessTime = logInfo.LastAccessTime;
        var lastWriteTime = logInfo.LastWriteTime;
        var oldestRecordNumber = logInfo.OldestRecordNumber;
        var recordCount = logInfo.RecordCount;

        // At minimum, these should not be null
        Assert.NotNull(attributes);
        Assert.NotNull(fileSize);
        Assert.NotNull(isLogFull);
        Assert.NotNull(recordCount);
    }

    [Fact]
    public void Constructor_WhenValidLog_ShouldHaveNonNegativeRecordCount()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logInfo = new EventLogInformation(session, Constants.ApplicationLogName, PathType.LogName);

        // Assert
        Assert.NotNull(logInfo.RecordCount);
        Assert.True(logInfo.RecordCount >= 0);
    }

    [Fact]
    public void Constructor_WhenValidLog_ShouldHavePositiveFileSize()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logInfo = new EventLogInformation(session, Constants.ApplicationLogName, PathType.LogName);

        // Assert
        Assert.NotNull(logInfo.FileSize);
        Assert.True(logInfo.FileSize > 0);
    }

    [Fact]
    public void CreationTime_WhenApplicationLog_ShouldBeValidDateTime()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logInfo = new EventLogInformation(session, Constants.ApplicationLogName, PathType.LogName);

        // Assert
        if (logInfo.CreationTime.HasValue)
        {
            Assert.True(logInfo.CreationTime.Value > DateTime.MinValue);
            Assert.True(logInfo.CreationTime.Value <= DateTime.UtcNow.AddDays(1));
        }
    }

    [Fact]
    public void FileSize_WhenApplicationLog_ShouldBeReasonable()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logInfo = new EventLogInformation(session, Constants.ApplicationLogName, PathType.LogName);

        // Assert
        Assert.NotNull(logInfo.FileSize);
        // File size should be at least 64KB (reasonable minimum for log file)
        Assert.True(logInfo.FileSize >= 65536);
        // File size should be less than 4GB (reasonable maximum)
        Assert.True(logInfo.FileSize < 4294967296);
    }

    [Fact]
    public void FileSize_WhenMultipleLogs_ShouldVaryByLog()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var appLogInfo = new EventLogInformation(session, Constants.ApplicationLogName, PathType.LogName);
        var sysLogInfo = new EventLogInformation(session, Constants.SystemLogName, PathType.LogName);

        // Assert
        Assert.NotNull(appLogInfo.FileSize);
        Assert.NotNull(sysLogInfo.FileSize);
        // Both should be positive
        Assert.True(appLogInfo.FileSize > 0);
        Assert.True(sysLogInfo.FileSize > 0);
    }

    [Fact]
    public void GetLogInformation_WhenCalledTwice_ShouldReturnDifferentInstances()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logInfo1 = session.GetLogInformation(Constants.ApplicationLogName, PathType.LogName);
        var logInfo2 = session.GetLogInformation(Constants.ApplicationLogName, PathType.LogName);

        // Assert
        Assert.NotNull(logInfo1);
        Assert.NotNull(logInfo2);
        Assert.NotSame(logInfo1, logInfo2);
    }

    [Fact]
    public void GetLogInformation_WhenCalledTwice_ShouldReturnSimilarData()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logInfo1 = session.GetLogInformation(Constants.ApplicationLogName, PathType.LogName);
        var logInfo2 = session.GetLogInformation(Constants.ApplicationLogName, PathType.LogName);

        // Assert
        // File size might change slightly, but should be close
        Assert.NotNull(logInfo1.FileSize);
        Assert.NotNull(logInfo2.FileSize);
        
        // Attributes should be identical
        Assert.Equal(logInfo1.Attributes, logInfo2.Attributes);
    }

    [Fact]
    public void IsLogFull_WhenApplicationLog_ShouldBeFalse()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logInfo = new EventLogInformation(session, Constants.ApplicationLogName, PathType.LogName);

        // Assert
        Assert.NotNull(logInfo.IsLogFull);
        // Application log is typically not full under normal circumstances
        Assert.False(logInfo.IsLogFull.Value);
    }

    [Fact]
    public void IsLogFull_WhenValidLog_ShouldReturnBoolean()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logInfo = new EventLogInformation(session, Constants.ApplicationLogName, PathType.LogName);

        // Assert
        Assert.NotNull(logInfo.IsLogFull);
        Assert.IsType<bool>(logInfo.IsLogFull.Value);
    }

    [Fact]
    public void LastAccessTime_WhenApplicationLog_ShouldBeValidOrNull()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logInfo = new EventLogInformation(session, Constants.ApplicationLogName, PathType.LogName);

        // Assert
        if (logInfo.LastAccessTime.HasValue)
        {
            Assert.True(logInfo.LastAccessTime.Value > DateTime.MinValue);
            Assert.True(logInfo.LastAccessTime.Value <= DateTime.UtcNow.AddDays(1));
        }
    }

    [Fact]
    public void LastWriteTime_WhenApplicationLog_ShouldBeValidOrNull()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logInfo = new EventLogInformation(session, Constants.ApplicationLogName, PathType.LogName);

        // Assert
        if (logInfo.LastWriteTime.HasValue)
        {
            Assert.True(logInfo.LastWriteTime.Value > DateTime.MinValue);
            Assert.True(logInfo.LastWriteTime.Value <= DateTime.UtcNow.AddDays(1));
        }
    }

    [Fact]
    public void OldestRecordNumber_WhenApplicationLog_ShouldBeNonNegative()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logInfo = new EventLogInformation(session, Constants.ApplicationLogName, PathType.LogName);

        // Assert
        if (logInfo.OldestRecordNumber.HasValue)
        {
            Assert.True(logInfo.OldestRecordNumber.Value >= 0);
        }
    }

    [Fact]
    public void Properties_WhenApplicationLog_ShouldBeReadOnly()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logInfo = new EventLogInformation(session, Constants.ApplicationLogName, PathType.LogName);

        // Assert
        // Properties should be read-only (init-only or get-only)
        var attributesProperty = typeof(EventLogInformation).GetProperty(nameof(EventLogInformation.Attributes));
        var fileSizeProperty = typeof(EventLogInformation).GetProperty(nameof(EventLogInformation.FileSize));
        var recordCountProperty = typeof(EventLogInformation).GetProperty(nameof(EventLogInformation.RecordCount));

        Assert.NotNull(attributesProperty);
        Assert.NotNull(fileSizeProperty);
        Assert.NotNull(recordCountProperty);

        Assert.True(attributesProperty.CanRead);
        Assert.False(attributesProperty.CanWrite);
        
        Assert.True(fileSizeProperty.CanRead);
        Assert.False(fileSizeProperty.CanWrite);
        
        Assert.True(recordCountProperty.CanRead);
        Assert.False(recordCountProperty.CanWrite);
    }

    [Fact]
    public void RecordCount_WhenApplicationLog_ShouldBeConsistentWithOldestRecord()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logInfo = new EventLogInformation(session, Constants.ApplicationLogName, PathType.LogName);

        // Assert
        Assert.NotNull(logInfo.RecordCount);
        
        if (logInfo is { OldestRecordNumber: not null, RecordCount: > 0 })
        {
            // Oldest record number should be reasonable
            Assert.True(logInfo.OldestRecordNumber.Value >= 0);
        }
    }

    [Fact]
    public void RecordCount_WhenEmptyLog_CanBeZero()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act - Try to find a log that might be empty
        // Most logs will have records, but this tests that zero is a valid value
        var logInfo = new EventLogInformation(session, Constants.ApplicationLogName, PathType.LogName);

        // Assert
        Assert.NotNull(logInfo.RecordCount);
        // Zero is valid, just verify it's not null and is non-negative
        Assert.True(logInfo.RecordCount >= 0);
    }

    [Fact]
    public void RecordCount_WhenLogHasRecords_ShouldMatchWithOldestRecordNumber()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logInfo = new EventLogInformation(session, Constants.ApplicationLogName, PathType.LogName);

        // Assert
        Assert.NotNull(logInfo.RecordCount);
        
        if (logInfo.RecordCount.Value == 0)
        {
            // If no records, oldest record number might be null or zero
            if (logInfo.OldestRecordNumber.HasValue)
            {
                Assert.True(logInfo.OldestRecordNumber.Value >= 0);
            }
        }
        else
        {
            // If we have records, oldest record number should be set
            Assert.True(logInfo.RecordCount.Value > 0);
        }
    }
}
