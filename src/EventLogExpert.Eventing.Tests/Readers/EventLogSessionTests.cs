// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Tests.TestUtils.Constants;

namespace EventLogExpert.Eventing.Tests.Readers;

public sealed class EventLogSessionTests
{
    [Fact]
    public void GetLogInformation_WhenApplicationLog_ShouldReturnInformation()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logInfo = session.GetLogInformation(Constants.ApplicationLogName, PathType.LogName);

        // Assert
        Assert.NotNull(logInfo);
        Assert.NotNull(logInfo.RecordCount);
    }

    [Fact]
    public void GetLogInformation_WhenCalledMultipleTimes_ShouldReturnDifferentInstances()
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

    [Theory]
    [InlineData(Constants.ApplicationLogName)]
    [InlineData(Constants.SystemLogName)]
    public void GetLogInformation_WhenCommonLogs_ShouldReturnInformation(string logName)
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logInfo = session.GetLogInformation(logName, PathType.LogName);

        // Assert
        Assert.NotNull(logInfo);
        Assert.NotNull(logInfo.FileSize);
    }

    [Fact]
    public async Task GetLogInformation_WhenConcurrentAccess_ShouldHandleMultipleThreads()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var tasks = new[]
        {
            Task.Run(() => session.GetLogInformation(Constants.ApplicationLogName, PathType.LogName)),
            Task.Run(() => session.GetLogInformation(Constants.SystemLogName, PathType.LogName)),
            Task.Run(() => session.GetLogInformation(Constants.ApplicationLogName, PathType.LogName))
        };

        await Task.WhenAll(tasks);

        // Assert
        Assert.All(tasks, task =>
        {
            Assert.True(task.IsCompletedSuccessfully);
            Assert.NotNull(task.Result);
        });
    }

    [Fact]
    public void GetLogInformation_WhenInvalidLogName_ShouldThrowException()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;
        var invalidLogName = "NonExistentLog_" + Guid.NewGuid();

        // Act & Assert
        Assert.ThrowsAny<Exception>(() =>
            session.GetLogInformation(invalidLogName, PathType.LogName));
    }

    [Fact]
    public void GetLogNames_AfterGetProviderNames_ShouldStillWork()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var providers = session.GetProviderNames();
        var logNames = session.GetLogNames();

        // Assert
        Assert.NotEmpty(providers);
        Assert.NotEmpty(logNames);
    }

    [Fact]
    public void GetLogNames_WhenCalled_AllNamesShouldBeNonEmpty()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logNames = session.GetLogNames().ToList();

        // Assert
        Assert.All(logNames, name =>
        {
            Assert.False(string.IsNullOrWhiteSpace(name));
        });
    }

    [Fact]
    public void GetLogNames_WhenCalled_ShouldContainApplicationLog()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logNames = session.GetLogNames().ToList();

        // Assert
        Assert.Contains(Constants.ApplicationLogName, logNames);
    }

    [Fact]
    public void GetLogNames_WhenCalled_ShouldContainCommonLogs()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logNames = session.GetLogNames().ToList();

        // Assert
        Assert.Contains(Constants.ApplicationLogName, logNames);
        Assert.Contains(Constants.SystemLogName, logNames);
        Assert.Contains(Constants.SecurityLogName, logNames);
    }

    [Fact]
    public void GetLogNames_WhenCalled_ShouldNotContainDuplicates()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logNames = session.GetLogNames().ToList();

        // Assert
        var uniqueNames = logNames.Distinct().ToList();
        Assert.Equal(logNames.Count, uniqueNames.Count);
    }

    [Fact]
    public void GetLogNames_WhenCalled_ShouldReturnAtLeast50Logs()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logNames = session.GetLogNames().ToList();

        // Assert
        // Modern Windows systems have many logs
        Assert.True(logNames.Count >= 50, $"Expected at least 50 logs, but got {logNames.Count}");
    }

    [Fact]
    public void GetLogNames_WhenCalled_ShouldReturnEnumerable()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logNames = session.GetLogNames();

        // Assert
        Assert.NotNull(logNames);
        Assert.IsAssignableFrom<IEnumerable<string>>(logNames);
    }

    [Fact]
    public void GetLogNames_WhenCalled_ShouldReturnLogNames()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logNames = session.GetLogNames().ToList();

        // Assert
        Assert.NotNull(logNames);
        Assert.NotEmpty(logNames);
    }

    [Fact]
    public void GetLogNames_WhenCalled_ShouldReturnOrderedList()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logNames = session.GetLogNames().ToList();

        // Assert
        Assert.NotEmpty(logNames);
        
        // Verify the list is sorted
        var sortedNames = logNames.OrderBy(x => x).ToList();
        Assert.Equal(sortedNames, logNames);
    }

    [Fact]
    public void GetLogNames_WhenCalledMultipleTimes_ShouldReturnConsistentResults()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logNames1 = session.GetLogNames().ToList();
        var logNames2 = session.GetLogNames().ToList();

        // Assert
        Assert.Equal(logNames1.Count, logNames2.Count);
        
        // All names from first call should be in second call
        foreach (var name in logNames1)
        {
            Assert.Contains(name, logNames2);
        }
    }

    [Fact]
    public async Task GetLogNames_WhenConcurrentAccess_ShouldHandleMultipleThreads()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var tasks = new[]
        {
            Task.Run(() => session.GetLogNames().ToList()),
            Task.Run(() => session.GetLogNames().ToList()),
            Task.Run(() => session.GetLogNames().ToList())
        };

        await Task.WhenAll(tasks);

        // Assert
        Assert.All(tasks, task =>
        {
            Assert.True(task.IsCompletedSuccessfully);
            Assert.NotEmpty(task.Result);
        });
    }

    [Fact]
    public void GetProviderNames_AfterGetLogNames_ShouldStillWork()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logNames = session.GetLogNames();
        var providers = session.GetProviderNames();

        // Assert
        Assert.NotEmpty(logNames);
        Assert.NotEmpty(providers);
    }

    [Fact]
    public void GetProviderNames_WhenCalled_AllNamesShouldBeNonEmpty()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var providers = session.GetProviderNames();

        // Assert
        Assert.All(providers, name =>
        {
            Assert.False(string.IsNullOrWhiteSpace(name));
        });
    }

    [Fact]
    public void GetProviderNames_WhenCalled_ShouldContainCommonProviders()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var providers = session.GetProviderNames();

        // Assert
        // Check for some common Windows providers
        Assert.Contains(Constants.SecurityAuditingLogName, providers);
        Assert.Contains(Constants.KernelGeneralLogName, providers);
    }

    [Fact]
    public void GetProviderNames_WhenCalled_ShouldNotContainDuplicates()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var providers = session.GetProviderNames();

        // Assert
        // HashSet inherently doesn't contain duplicates, but verify count
        var providersList = providers.ToList();
        var uniqueProviders = providersList.Distinct().ToList();
        Assert.Equal(providersList.Count, uniqueProviders.Count);
    }

    [Fact]
    public void GetProviderNames_WhenCalled_ShouldReturnHashSet()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var providers = session.GetProviderNames();

        // Assert
        Assert.NotNull(providers);
        Assert.IsAssignableFrom<HashSet<string>>(providers);
    }

    [Fact]
    public void GetProviderNames_WhenCalled_ShouldReturnManyProviders()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var providers = session.GetProviderNames();

        // Assert
        // Windows should have at least 100 providers
        Assert.True(providers.Count >= 100, $"Expected at least 100 providers, but got {providers.Count}");
    }

    [Fact]
    public void GetProviderNames_WhenCalled_ShouldReturnProviderNames()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var providers = session.GetProviderNames();

        // Assert
        Assert.NotNull(providers);
        Assert.NotEmpty(providers);
    }

    [Fact]
    public void GetProviderNames_WhenCalledMultipleTimes_ShouldReturnConsistentResults()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var providers1 = session.GetProviderNames();
        var providers2 = session.GetProviderNames();

        // Assert
        Assert.Equal(providers1.Count, providers2.Count);
        
        // All providers from first call should be in second call
        foreach (var provider in providers1)
        {
            Assert.Contains(provider, providers2);
        }
    }

    [Fact]
    public async Task GetProviderNames_WhenConcurrentAccess_ShouldHandleMultipleThreads()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var tasks = new[]
        {
            Task.Run(() => session.GetProviderNames()),
            Task.Run(() => session.GetProviderNames()),
            Task.Run(() => session.GetProviderNames())
        };

        await Task.WhenAll(tasks);

        // Assert
        Assert.All(tasks, task =>
        {
            Assert.True(task.IsCompletedSuccessfully);
            Assert.NotEmpty(task.Result);
        });
    }

    [Fact]
    public void GlobalSession_WhenAccessed_ShouldNotBeNull()
    {
        // Arrange & Act
        var session = EventLogSession.GlobalSession;

        // Assert
        Assert.NotNull(session);
    }

    [Fact]
    public void GlobalSession_WhenAccessedMultipleTimes_ShouldReturnSameInstance()
    {
        // Arrange & Act
        var session1 = EventLogSession.GlobalSession;
        var session2 = EventLogSession.GlobalSession;

        // Assert
        Assert.NotNull(session1);
        Assert.NotNull(session2);
        Assert.Same(session1, session2);
    }

    [Fact]
    public void GlobalSession_WhenUsedByMultipleMethods_ShouldWorkCorrectly()
    {
        // Arrange
        var session = EventLogSession.GlobalSession;

        // Act
        var logNames = session.GetLogNames().ToList();
        var providers = session.GetProviderNames();
        var logInfo = session.GetLogInformation(Constants.ApplicationLogName, PathType.LogName);

        // Assert
        Assert.NotEmpty(logNames);
        Assert.NotEmpty(providers);
        Assert.NotNull(logInfo);
    }
}
