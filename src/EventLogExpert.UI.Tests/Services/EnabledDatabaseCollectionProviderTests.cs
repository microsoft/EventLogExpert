// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Options;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Services;

public sealed class EnabledDatabaseCollectionProviderTests : IDisposable
{
    private readonly string _testDirectory;

    public EnabledDatabaseCollectionProviderTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"EnabledDatabaseCollectionProviderTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
    }

    [Fact]
    public void Constructor_WhenCalled_ShouldSetActiveDatabases()
    {
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);
        CreateDatabaseFile(databasePath, Constants.TestDb2);

        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockPreferencesProvider = Substitute.For<IPreferencesProvider>();
        mockPreferencesProvider.DisabledDatabasesPreference.Returns([]);
        var mockTraceLogger = Substitute.For<ITraceLogger>();

        // Act
        var provider = new EnabledDatabaseCollectionProvider(
            fileLocationOptions,
            mockPreferencesProvider,
            mockTraceLogger);

        // Assert
        Assert.Equal(2, provider.ActiveDatabases.Count);
        Assert.Contains(provider.ActiveDatabases, p => p.Contains(Constants.TestDb1));
        Assert.Contains(provider.ActiveDatabases, p => p.Contains(Constants.TestDb2));
    }

    [Fact]
    public void Constructor_WhenCalled_ShouldTraceMessage()
    {
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockPreferencesProvider = Substitute.For<IPreferencesProvider>();
        mockPreferencesProvider.DisabledDatabasesPreference.Returns([]);
        var mockTraceLogger = Substitute.For<ITraceLogger>();

        // Act
        _ = new EnabledDatabaseCollectionProvider(
            fileLocationOptions,
            mockPreferencesProvider,
            mockTraceLogger);

        // Assert
        mockTraceLogger.Received(1).Debug(
            Arg.Is<DebugLogHandler>(h => h.ToString().Contains(nameof(EnabledDatabaseCollectionProvider.SetActiveDatabases))));
    }

    [Fact]
    public void Constructor_WhenDatabaseDirectoryDoesNotExist_ShouldSetEmptyActiveDatabases()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockPreferencesProvider = Substitute.For<IPreferencesProvider>();
        mockPreferencesProvider.DisabledDatabasesPreference.Returns([]);
        var mockTraceLogger = Substitute.For<ITraceLogger>();

        // Act
        var provider = new EnabledDatabaseCollectionProvider(
            fileLocationOptions,
            mockPreferencesProvider,
            mockTraceLogger);

        // Assert
        Assert.Empty(provider.ActiveDatabases);
    }

    [Fact]
    public void Constructor_WhenDisabledDatabasesExist_ShouldExcludeFromActiveDatabases()
    {
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);
        CreateDatabaseFile(databasePath, Constants.TestDb2);

        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockPreferencesProvider = Substitute.For<IPreferencesProvider>();
        mockPreferencesProvider.DisabledDatabasesPreference.Returns([Constants.TestDb1]);
        var mockTraceLogger = Substitute.For<ITraceLogger>();

        // Act
        var provider = new EnabledDatabaseCollectionProvider(
            fileLocationOptions,
            mockPreferencesProvider,
            mockTraceLogger);

        // Assert
        Assert.Single(provider.ActiveDatabases);
        Assert.Contains(provider.ActiveDatabases, p => p.Contains(Constants.TestDb2));
        Assert.DoesNotContain(provider.ActiveDatabases, p => p.Contains(Constants.TestDb1));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }
        catch
        {
            // Ignore cleanup errors in tests
        }
    }

    [Fact]
    public void GetEnabledDatabases_WhenAllDatabasesDisabled_ShouldReturnEmpty()
    {
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockPreferencesProvider = Substitute.For<IPreferencesProvider>();
        mockPreferencesProvider.DisabledDatabasesPreference.Returns([Constants.TestDb1]);
        var mockTraceLogger = Substitute.For<ITraceLogger>();

        var provider = new EnabledDatabaseCollectionProvider(
            fileLocationOptions,
            mockPreferencesProvider,
            mockTraceLogger);

        // Act
        var result = provider.GetEnabledDatabases();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetEnabledDatabases_WhenCaseInsensitiveMatch_ShouldExcludeDisabled()
    {
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockPreferencesProvider = Substitute.For<IPreferencesProvider>();
        // Use different case for disabled database name
        mockPreferencesProvider.DisabledDatabasesPreference.Returns([Constants.TestDb1.ToUpper()]);
        var mockTraceLogger = Substitute.For<ITraceLogger>();

        var provider = new EnabledDatabaseCollectionProvider(
            fileLocationOptions,
            mockPreferencesProvider,
            mockTraceLogger);

        // Act
        var result = provider.GetEnabledDatabases();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetEnabledDatabases_WhenDatabaseDirectoryDoesNotExist_ShouldReturnEmpty()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockPreferencesProvider = Substitute.For<IPreferencesProvider>();
        mockPreferencesProvider.DisabledDatabasesPreference.Returns([]);
        var mockTraceLogger = Substitute.For<ITraceLogger>();

        var provider = new EnabledDatabaseCollectionProvider(
            fileLocationOptions,
            mockPreferencesProvider,
            mockTraceLogger);

        // Act
        var result = provider.GetEnabledDatabases();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetEnabledDatabases_WhenDatabasesExist_ShouldReturnOnlyDbFiles()
    {
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);
        CreateDatabaseFile(databasePath, Constants.TestDb2);
        File.WriteAllText(Path.Combine(databasePath, "notadatabase.txt"), "test");

        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockPreferencesProvider = Substitute.For<IPreferencesProvider>();
        mockPreferencesProvider.DisabledDatabasesPreference.Returns([]);
        var mockTraceLogger = Substitute.For<ITraceLogger>();

        var provider = new EnabledDatabaseCollectionProvider(
            fileLocationOptions,
            mockPreferencesProvider,
            mockTraceLogger);

        // Act
        var result = provider.GetEnabledDatabases();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(Constants.TestDb1, result);
        Assert.Contains(Constants.TestDb2, result);
        Assert.DoesNotContain("notadatabase.txt", result);
    }

    [Fact]
    public void GetEnabledDatabases_WhenNoDisabledDatabases_ShouldReturnAll()
    {
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);
        CreateDatabaseFile(databasePath, Constants.TestDb2);
        CreateDatabaseFile(databasePath, Constants.TestDb3);

        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockPreferencesProvider = Substitute.For<IPreferencesProvider>();
        mockPreferencesProvider.DisabledDatabasesPreference.Returns([]);
        var mockTraceLogger = Substitute.For<ITraceLogger>();

        var provider = new EnabledDatabaseCollectionProvider(
            fileLocationOptions,
            mockPreferencesProvider,
            mockTraceLogger);

        // Act
        var result = provider.GetEnabledDatabases();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Contains(Constants.TestDb1, result);
        Assert.Contains(Constants.TestDb2, result);
        Assert.Contains(Constants.TestDb3, result);
    }

    [Fact]
    public void GetEnabledDatabases_WhenSomeDatabasesDisabled_ShouldReturnOnlyEnabled()
    {
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);
        CreateDatabaseFile(databasePath, Constants.TestDb2);
        CreateDatabaseFile(databasePath, Constants.TestDb3);

        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockPreferencesProvider = Substitute.For<IPreferencesProvider>();
        mockPreferencesProvider.DisabledDatabasesPreference.Returns([Constants.TestDb2]);
        var mockTraceLogger = Substitute.For<ITraceLogger>();

        var provider = new EnabledDatabaseCollectionProvider(
            fileLocationOptions,
            mockPreferencesProvider,
            mockTraceLogger);

        // Act
        var result = provider.GetEnabledDatabases();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(Constants.TestDb1, result);
        Assert.Contains(Constants.TestDb3, result);
        Assert.DoesNotContain(Constants.TestDb2, result);
    }

    [Fact]
    public void SetActiveDatabases_WhenCalled_ShouldTraceMessage()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockPreferencesProvider = Substitute.For<IPreferencesProvider>();
        mockPreferencesProvider.DisabledDatabasesPreference.Returns([]);
        var mockTraceLogger = Substitute.For<ITraceLogger>();

        var provider = new EnabledDatabaseCollectionProvider(
            fileLocationOptions,
            mockPreferencesProvider,
            mockTraceLogger);

        mockTraceLogger.ClearReceivedCalls();

        // Act
        provider.SetActiveDatabases([Constants.TestDbPath1, Constants.TestDbPath2]);

        // Assert
        mockTraceLogger.Received(1).Debug(
            Arg.Is<DebugLogHandler>(h =>
                h.ToString().Contains(nameof(EnabledDatabaseCollectionProvider.SetActiveDatabases)) &&
                h.ToString().Contains('2')));
    }

    [Fact]
    public void SetActiveDatabases_WhenCalled_ShouldUpdateActiveDatabases()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockPreferencesProvider = Substitute.For<IPreferencesProvider>();
        mockPreferencesProvider.DisabledDatabasesPreference.Returns([]);
        var mockTraceLogger = Substitute.For<ITraceLogger>();

        var provider = new EnabledDatabaseCollectionProvider(
            fileLocationOptions,
            mockPreferencesProvider,
            mockTraceLogger);

        var newDatabases = new[] { Constants.TestDbPath1, Constants.TestDbPath2 };

        // Act
        provider.SetActiveDatabases(newDatabases);

        // Assert
        Assert.Equal(2, provider.ActiveDatabases.Count);
        Assert.Contains(Constants.TestDbPath1, provider.ActiveDatabases);
        Assert.Contains(Constants.TestDbPath2, provider.ActiveDatabases);
    }

    [Fact]
    public void SetActiveDatabases_WhenCalledMultipleTimes_ShouldReplaceActiveDatabases()
    {
        // Arrange
        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockPreferencesProvider = Substitute.For<IPreferencesProvider>();
        mockPreferencesProvider.DisabledDatabasesPreference.Returns([]);
        var mockTraceLogger = Substitute.For<ITraceLogger>();

        var provider = new EnabledDatabaseCollectionProvider(
            fileLocationOptions,
            mockPreferencesProvider,
            mockTraceLogger);

        // Act
        provider.SetActiveDatabases([Constants.TestDbPath1]);
        provider.SetActiveDatabases([Constants.TestDbPath2, Constants.TestDbPath3]);

        // Assert
        Assert.Equal(2, provider.ActiveDatabases.Count);
        Assert.DoesNotContain(Constants.TestDbPath1, provider.ActiveDatabases);
        Assert.Contains(Constants.TestDbPath2, provider.ActiveDatabases);
        Assert.Contains(Constants.TestDbPath3, provider.ActiveDatabases);
    }

    [Fact]
    public void SetActiveDatabases_WhenCalledWithEmptyCollection_ShouldClearActiveDatabases()
    {
        // Arrange
        var databasePath = CreateDatabaseDirectory();
        CreateDatabaseFile(databasePath, Constants.TestDb1);

        var fileLocationOptions = new FileLocationOptions(_testDirectory);
        var mockPreferencesProvider = Substitute.For<IPreferencesProvider>();
        mockPreferencesProvider.DisabledDatabasesPreference.Returns([]);
        var mockTraceLogger = Substitute.For<ITraceLogger>();

        var provider = new EnabledDatabaseCollectionProvider(
            fileLocationOptions,
            mockPreferencesProvider,
            mockTraceLogger);

        Assert.Single(provider.ActiveDatabases); // Verify initial state

        // Act
        provider.SetActiveDatabases([]);

        // Assert
        Assert.Empty(provider.ActiveDatabases);
    }

    private static void CreateDatabaseFile(string databasePath, string fileName)
    {
        File.WriteAllText(Path.Combine(databasePath, fileName), string.Empty);
    }

    private string CreateDatabaseDirectory()
    {
        var databasePath = Path.Combine(_testDirectory, "Databases");
        Directory.CreateDirectory(databasePath);
        return databasePath;
    }
}
