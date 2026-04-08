// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Services;

public sealed class DatabaseServiceTests
{
    [Fact]
    public void DisabledDatabases_AfterUpdate_ShouldReturnUpdatedValues()
    {
        // Arrange
        var mockPreferencesProvider = Substitute.For<IPreferencesProvider>();
        mockPreferencesProvider.DisabledDatabasesPreference.Returns([Constants.InitialDisabled]);

        var mockEnabledDatabaseCollectionProvider = Substitute.For<IEnabledDatabaseCollectionProvider>();
        mockEnabledDatabaseCollectionProvider.GetEnabledDatabases().Returns([]);

        var databaseService = CreateDatabaseService(
            mockEnabledDatabaseCollectionProvider,
            mockPreferencesProvider);

        // Act
        databaseService.UpdateDisabledDatabases([Constants.NewDisabled1, Constants.NewDisabled2]);
        var result = databaseService.DisabledDatabases.ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(Constants.NewDisabled1, result);
        Assert.Contains(Constants.NewDisabled2, result);
    }

    [Fact]
    public void DisabledDatabases_WhenAccessed_ShouldReturnFromPreferences()
    {
        // Arrange
        var mockPreferencesProvider = Substitute.For<IPreferencesProvider>();
        mockPreferencesProvider.DisabledDatabasesPreference.Returns([Constants.DisabledDb1, Constants.DisabledDb2]);

        var databaseService = CreateDatabaseService(preferencesProvider: mockPreferencesProvider);

        // Act
        var result = databaseService.DisabledDatabases.ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(Constants.DisabledDb1, result);
        Assert.Contains(Constants.DisabledDb2, result);
    }

    [Fact]
    public void DisabledDatabases_WhenAccessedMultipleTimes_ShouldCacheResult()
    {
        // Arrange
        var mockPreferencesProvider = Substitute.For<IPreferencesProvider>();
        mockPreferencesProvider.DisabledDatabasesPreference.Returns([Constants.DisabledDb]);

        var databaseService = CreateDatabaseService(preferencesProvider: mockPreferencesProvider);

        // Act
        _ = databaseService.DisabledDatabases;
        _ = databaseService.DisabledDatabases;
        _ = databaseService.DisabledDatabases;

        // Assert
        _ = mockPreferencesProvider.Received(1).DisabledDatabasesPreference;
    }

    [Fact]
    public void LoadDatabases_WhenCalled_ShouldInvokeLoadedDatabasesChanged()
    {
        // Arrange
        var mockEnabledDatabaseCollectionProvider = Substitute.For<IEnabledDatabaseCollectionProvider>();
        mockEnabledDatabaseCollectionProvider.GetEnabledDatabases().Returns([Constants.DatabaseA]);

        var databaseService = CreateDatabaseService(mockEnabledDatabaseCollectionProvider);

        IEnumerable<string>? receivedDatabases = null;
        object? receivedSender = null;

        databaseService.LoadedDatabasesChanged += (sender, databases) =>
        {
            receivedSender = sender;
            receivedDatabases = databases;
        };

        // Act
        databaseService.LoadDatabases();

        // Assert
        Assert.NotNull(receivedDatabases);
        Assert.Same(databaseService, receivedSender);
        Assert.Contains(Constants.DatabaseA, receivedDatabases);
    }

    [Fact]
    public void LoadDatabases_WhenCalled_ShouldRefreshLoadedDatabases()
    {
        // Arrange
        var mockEnabledDatabaseCollectionProvider = Substitute.For<IEnabledDatabaseCollectionProvider>();

        mockEnabledDatabaseCollectionProvider.GetEnabledDatabases()
            .Returns([Constants.DatabaseA], [Constants.DatabaseB, Constants.DatabaseC]);

        var databaseService = CreateDatabaseService(mockEnabledDatabaseCollectionProvider);

        // Act
        var initialResult = databaseService.LoadedDatabases.ToList();
        databaseService.LoadDatabases();
        var refreshedResult = databaseService.LoadedDatabases.ToList();

        // Assert
        Assert.Single(initialResult);
        Assert.Equal(Constants.DatabaseA, initialResult[0]);
        Assert.Equal(2, refreshedResult.Count);
        Assert.Contains(Constants.DatabaseB, refreshedResult);
        Assert.Contains(Constants.DatabaseC, refreshedResult);
    }

    [Fact]
    public void LoadDatabases_WhenNoEventHandler_ShouldNotThrow()
    {
        // Arrange
        var mockEnabledDatabaseCollectionProvider = Substitute.For<IEnabledDatabaseCollectionProvider>();
        mockEnabledDatabaseCollectionProvider.GetEnabledDatabases().Returns([Constants.DatabaseA]);

        var databaseService = CreateDatabaseService(mockEnabledDatabaseCollectionProvider);

        // Act & Assert
        var exception = Record.Exception(() => databaseService.LoadDatabases());
        Assert.Null(exception);
    }

    [Fact]
    public void LoadedDatabases_WhenAccessed_ShouldReturnSortedDatabases()
    {
        // Arrange
        // Names with pattern "Name Version" sort by name asc, then version desc
        var mockEnabledDatabaseCollectionProvider = Substitute.For<IEnabledDatabaseCollectionProvider>();

        mockEnabledDatabaseCollectionProvider.GetEnabledDatabases()
            .Returns([Constants.DatabaseC, Constants.DatabaseA, Constants.DatabaseB]);

        var databaseService = CreateDatabaseService(mockEnabledDatabaseCollectionProvider);

        // Act
        var result = databaseService.LoadedDatabases.ToList();

        // Assert
        // "Database X" splits to FirstPart="Database " + SecondPart="X"
        // Sorted by FirstPart asc, then SecondPart desc: C, B, A
        Assert.Equal(3, result.Count);
        Assert.Equal(Constants.DatabaseC, result[0]);
        Assert.Equal(Constants.DatabaseB, result[1]);
        Assert.Equal(Constants.DatabaseA, result[2]);
    }

    [Fact]
    public void LoadedDatabases_WhenAccessedMultipleTimes_ShouldCacheResult()
    {
        // Arrange
        var mockEnabledDatabaseCollectionProvider = Substitute.For<IEnabledDatabaseCollectionProvider>();

        mockEnabledDatabaseCollectionProvider.GetEnabledDatabases()
            .Returns([Constants.DatabaseA]);

        var databaseService = CreateDatabaseService(mockEnabledDatabaseCollectionProvider);

        // Act
        _ = databaseService.LoadedDatabases;
        _ = databaseService.LoadedDatabases;
        _ = databaseService.LoadedDatabases;

        // Assert
        mockEnabledDatabaseCollectionProvider.Received(1).GetEnabledDatabases();
    }

    [Fact]
    public void LoadedDatabases_WhenEmpty_ShouldReturnEmptyCollection()
    {
        // Arrange
        var mockEnabledDatabaseCollectionProvider = Substitute.For<IEnabledDatabaseCollectionProvider>();
        mockEnabledDatabaseCollectionProvider.GetEnabledDatabases().Returns([]);

        var databaseService = CreateDatabaseService(mockEnabledDatabaseCollectionProvider);

        // Act
        var result = databaseService.LoadedDatabases;

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void LoadedDatabases_WhenVersionedDatabases_ShouldSortByNameThenVersionDescending()
    {
        // Arrange
        var mockEnabledDatabaseCollectionProvider = Substitute.For<IEnabledDatabaseCollectionProvider>();

        mockEnabledDatabaseCollectionProvider.GetEnabledDatabases()
            .Returns([Constants.Windows10, Constants.Windows11, Constants.Windows9, Constants.Linux1]);

        var databaseService = CreateDatabaseService(mockEnabledDatabaseCollectionProvider);

        // Act
        var result = databaseService.LoadedDatabases.ToList();

        // Assert - Numeric sorting: 11 > 10 > 9, 1
        Assert.Equal(4, result.Count);
        Assert.Equal(Constants.Linux1, result[0]);
        Assert.Equal(Constants.Windows11, result[1]);
        Assert.Equal(Constants.Windows10, result[2]);
        Assert.Equal(Constants.Windows9, result[3]);
    }

    [Fact]
    public void LoadedDatabases_WhenNumericVersions_ShouldSortNumericallyNotLexicographically()
    {
        // Arrange
        // This test ensures "10" sorts after "2" (numeric) rather than before it (lexicographic)
        var mockEnabledDatabaseCollectionProvider = Substitute.For<IEnabledDatabaseCollectionProvider>();

        mockEnabledDatabaseCollectionProvider.GetEnabledDatabases()
            .Returns([Constants.Server2, Constants.Server10, Constants.Server1, Constants.Server20]);

        var databaseService = CreateDatabaseService(mockEnabledDatabaseCollectionProvider);

        // Act
        var result = databaseService.LoadedDatabases.ToList();

        // Assert - Numeric descending: 20, 10, 2, 1
        Assert.Equal(4, result.Count);
        Assert.Equal(Constants.Server20, result[0]);
        Assert.Equal(Constants.Server10, result[1]);
        Assert.Equal(Constants.Server2, result[2]);
        Assert.Equal(Constants.Server1, result[3]);
    }

    [Fact]
    public void LoadedDatabasesChanged_WhenSetToNull_ShouldNotThrowOnLoadDatabases()
    {
        // Arrange
        var mockEnabledDatabaseCollectionProvider = Substitute.For<IEnabledDatabaseCollectionProvider>();
        mockEnabledDatabaseCollectionProvider.GetEnabledDatabases().Returns([Constants.DatabaseA]);

        var databaseService = CreateDatabaseService(mockEnabledDatabaseCollectionProvider);
        databaseService.LoadedDatabasesChanged = null;

        // Act & Assert
        var exception = Record.Exception(() => databaseService.LoadDatabases());
        Assert.Null(exception);
    }

    [Fact]
    public void SortDatabases_WhenMixedVersionedAndNonVersioned_ShouldSortCorrectly()
    {
        // Arrange
        var mockEnabledDatabaseCollectionProvider = Substitute.For<IEnabledDatabaseCollectionProvider>();

        mockEnabledDatabaseCollectionProvider.GetEnabledDatabases()
            .Returns([Constants.Windows10, Constants.SimpleDatabase, Constants.Windows11, Constants.AnotherDb]);

        var databaseService = CreateDatabaseService(mockEnabledDatabaseCollectionProvider);

        // Act
        var result = databaseService.LoadedDatabases.ToList();

        // Assert - Non-versioned first (no space pattern), then versioned with numeric sort
        Assert.Equal(4, result.Count);
        Assert.Equal(Constants.AnotherDb, result[0]);
        Assert.Equal(Constants.SimpleDatabase, result[1]);
        Assert.Equal(Constants.Windows11, result[2]);
        Assert.Equal(Constants.Windows10, result[3]);
    }

    [Fact]
    public void UpdateDisabledDatabases_WhenCalled_ShouldInvokeLoadedDatabasesChanged()
    {
        // Arrange
        var mockEnabledDatabaseCollectionProvider = Substitute.For<IEnabledDatabaseCollectionProvider>();
        mockEnabledDatabaseCollectionProvider.GetEnabledDatabases().Returns([Constants.DatabaseA]);

        var databaseService = CreateDatabaseService(mockEnabledDatabaseCollectionProvider);

        var eventInvoked = false;
        databaseService.LoadedDatabasesChanged += (_, _) => eventInvoked = true;

        // Act
        databaseService.UpdateDisabledDatabases([Constants.DisabledDb]);

        // Assert
        Assert.True(eventInvoked);
    }

    [Fact]
    public void UpdateDisabledDatabases_WhenCalled_ShouldReloadDatabases()
    {
        // Arrange
        var mockEnabledDatabaseCollectionProvider = Substitute.For<IEnabledDatabaseCollectionProvider>();
        mockEnabledDatabaseCollectionProvider.GetEnabledDatabases().Returns([Constants.DatabaseA]);

        var databaseService = CreateDatabaseService(mockEnabledDatabaseCollectionProvider);

        // Act
        databaseService.UpdateDisabledDatabases([Constants.DisabledDb]);

        // Assert - GetEnabledDatabases should be called once for reload (LoadedDatabases not accessed before)
        mockEnabledDatabaseCollectionProvider.Received(1).GetEnabledDatabases();
    }

    [Fact]
    public void UpdateDisabledDatabases_WhenCalled_ShouldUpdatePreferences()
    {
        // Arrange
        var mockPreferencesProvider = Substitute.For<IPreferencesProvider>();
        var mockEnabledDatabaseCollectionProvider = Substitute.For<IEnabledDatabaseCollectionProvider>();
        mockEnabledDatabaseCollectionProvider.GetEnabledDatabases().Returns([]);

        var databaseService = CreateDatabaseService(
            mockEnabledDatabaseCollectionProvider,
            mockPreferencesProvider);

        var newDisabledDatabases = new List<string> { Constants.NewDisabled1, Constants.NewDisabled2 };

        // Act
        databaseService.UpdateDisabledDatabases(newDisabledDatabases);

        // Assert
        mockPreferencesProvider.Received(1).DisabledDatabasesPreference =
            Arg.Is<IEnumerable<string>>(x => x.Contains(Constants.NewDisabled1) && x.Contains(Constants.NewDisabled2));
    }

    [Fact]
    public void UpdateDisabledDatabases_WhenCalledWithEmptyList_ShouldUpdatePreferences()
    {
        // Arrange
        var mockPreferencesProvider = Substitute.For<IPreferencesProvider>();
        var mockEnabledDatabaseCollectionProvider = Substitute.For<IEnabledDatabaseCollectionProvider>();
        mockEnabledDatabaseCollectionProvider.GetEnabledDatabases().Returns([]);

        var databaseService = CreateDatabaseService(
            mockEnabledDatabaseCollectionProvider,
            mockPreferencesProvider);

        // Act
        databaseService.UpdateDisabledDatabases([]);

        // Assert
        mockPreferencesProvider.Received(1).DisabledDatabasesPreference =
            Arg.Is<IEnumerable<string>>(x => !x.Any());
    }

    private static DatabaseService CreateDatabaseService(
        IEnabledDatabaseCollectionProvider? enabledDatabaseCollectionProvider = null,
        IPreferencesProvider? preferencesProvider = null)
    {
        return new DatabaseService(
            enabledDatabaseCollectionProvider ?? Substitute.For<IEnabledDatabaseCollectionProvider>(),
            preferencesProvider ?? Substitute.For<IPreferencesProvider>());
    }
}
