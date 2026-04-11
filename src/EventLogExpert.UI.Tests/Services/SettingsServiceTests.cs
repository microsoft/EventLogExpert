// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Services;

public sealed class SettingsServiceTests
{
    [Fact]
    public void Constructor_WhenCalled_ShouldNotThrow()
    {
        // Arrange & Act
        var exception = Record.Exception(() => CreateSettingsService());

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void CopyType_WhenAccessedMultipleTimes_ShouldCacheValue()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();
        mockPreferences.KeyboardCopyTypePreference.Returns(CopyType.Full);

        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        _ = settingsService.CopyType;
        _ = settingsService.CopyType;
        _ = settingsService.CopyType;

        // Assert
        _ = mockPreferences.Received(1).KeyboardCopyTypePreference;
    }

    [Fact]
    public void CopyType_WhenFirstAccessed_ShouldReturnFromPreferences()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();
        mockPreferences.KeyboardCopyTypePreference.Returns(CopyType.Xml);

        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        var result = settingsService.CopyType;

        // Assert
        Assert.Equal(CopyType.Xml, result);
    }

    [Fact]
    public void CopyType_WhenPreferenceIsNull_ShouldReturnDefault()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();

        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        var result = settingsService.CopyType;

        // Assert
        Assert.Equal(CopyType.Default, result);
    }

    [Fact]
    public void CopyType_WhenSet_ShouldUpdatePreferences()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();
        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        settingsService.CopyType = CopyType.Full;

        // Assert
        mockPreferences.Received(1).KeyboardCopyTypePreference = CopyType.Full;
    }

    [Fact]
    public void CopyType_WhenSetToDifferentValue_ShouldInvokeChangedEvent()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();
        var settingsService = CreateSettingsService(mockPreferences);

        var eventInvoked = false;
        settingsService.CopyTypeChanged = () => eventInvoked = true;

        // Act
        settingsService.CopyType = CopyType.Simple;

        // Assert
        Assert.True(eventInvoked);
    }

    [Theory]
    [InlineData(CopyType.Default)]
    [InlineData(CopyType.Simple)]
    [InlineData(CopyType.Xml)]
    [InlineData(CopyType.Full)]
    public void CopyType_WhenSetToEachValue_ShouldPersistCorrectly(CopyType copyType)
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();
        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        settingsService.CopyType = copyType;

        // Assert
        mockPreferences.Received(1).KeyboardCopyTypePreference = copyType;
    }

    [Fact]
    public void CopyType_WhenSetToSameValue_ShouldNotInvokeChangedEvent()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();
        mockPreferences.KeyboardCopyTypePreference.Returns(CopyType.Xml);

        var settingsService = CreateSettingsService(mockPreferences);
        _ = settingsService.CopyType; // Cache the value

        var eventInvoked = false;
        settingsService.CopyTypeChanged = () => eventInvoked = true;

        // Act
        settingsService.CopyType = CopyType.Xml;

        // Assert
        Assert.False(eventInvoked);
    }

    [Fact]
    public void CopyType_WhenSetToSameValue_ShouldNotUpdatePreferences()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();
        mockPreferences.KeyboardCopyTypePreference.Returns(CopyType.Xml);

        var settingsService = CreateSettingsService(mockPreferences);

        // First access caches the value
        _ = settingsService.CopyType;
        mockPreferences.ClearReceivedCalls();

        // Act
        settingsService.CopyType = CopyType.Xml;

        // Assert
        mockPreferences.DidNotReceive().KeyboardCopyTypePreference = Arg.Any<CopyType>();
    }

    [Fact]
    public void IsPreReleaseEnabled_WhenAccessedMultipleTimes_ShouldCacheValue()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();
        mockPreferences.PreReleasePreference.Returns(true);

        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        _ = settingsService.IsPreReleaseEnabled;
        _ = settingsService.IsPreReleaseEnabled;
        _ = settingsService.IsPreReleaseEnabled;

        // Assert
        _ = mockPreferences.Received(1).PreReleasePreference;
    }

    [Fact]
    public void IsPreReleaseEnabled_WhenFirstAccessed_ShouldReturnFromPreferences()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();
        mockPreferences.PreReleasePreference.Returns(true);

        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        var result = settingsService.IsPreReleaseEnabled;

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsPreReleaseEnabled_WhenPreferenceIsDefault_ShouldReturnFalse()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();

        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        var result = settingsService.IsPreReleaseEnabled;

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsPreReleaseEnabled_WhenSet_ShouldUpdatePreferences()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();
        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        settingsService.IsPreReleaseEnabled = true;

        // Assert
        mockPreferences.Received(1).PreReleasePreference = true;
    }

    [Fact]
    public void IsPreReleaseEnabled_WhenSetToSameValue_ShouldNotUpdatePreferences()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();
        mockPreferences.PreReleasePreference.Returns(true);

        var settingsService = CreateSettingsService(mockPreferences);
        _ = settingsService.IsPreReleaseEnabled; // Cache the value
        mockPreferences.ClearReceivedCalls();

        // Act
        settingsService.IsPreReleaseEnabled = true;

        // Assert
        mockPreferences.DidNotReceive().PreReleasePreference = Arg.Any<bool>();
    }

    [Fact]
    public void Load_WhenCalled_ShouldInvokeLoadedEvent()
    {
        // Arrange
        var settingsService = CreateSettingsService();

        var eventInvoked = false;
        settingsService.Loaded += () => eventInvoked = true;

        // Act
        settingsService.Load();

        // Assert
        Assert.True(eventInvoked);
    }

    [Fact]
    public void Load_WhenNoSubscribers_ShouldNotThrow()
    {
        // Arrange
        var settingsService = CreateSettingsService();

        // Act - no subscribers attached
        var exception = Record.Exception(settingsService.Load);

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void LogLevel_WhenAccessedMultipleTimes_ShouldCacheValue()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();
        mockPreferences.LogLevelPreference.Returns(LogLevel.Trace);

        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        _ = settingsService.LogLevel;
        _ = settingsService.LogLevel;
        _ = settingsService.LogLevel;

        // Assert
        _ = mockPreferences.Received(1).LogLevelPreference;
    }

    [Fact]
    public void LogLevel_WhenFirstAccessed_ShouldReturnFromPreferences()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();
        mockPreferences.LogLevelPreference.Returns(LogLevel.Debug);

        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        var result = settingsService.LogLevel;

        // Assert
        Assert.Equal(LogLevel.Debug, result);
    }

    [Fact]
    public void LogLevel_WhenPreferenceIsDefault_ShouldReturnTrace()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();

        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        var result = settingsService.LogLevel;

        // Assert
        Assert.Equal(LogLevel.Trace, result);
    }

    [Fact]
    public void LogLevel_WhenSet_ShouldUpdatePreferences()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();
        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        settingsService.LogLevel = LogLevel.Warning;

        // Assert
        mockPreferences.Received(1).LogLevelPreference = LogLevel.Warning;
    }

    [Fact]
    public void LogLevel_WhenSetToDifferentValue_ShouldInvokeChangedEvent()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();
        var settingsService = CreateSettingsService(mockPreferences);

        var eventInvoked = false;
        settingsService.LogLevelChanged = () => eventInvoked = true;

        // Act
        settingsService.LogLevel = LogLevel.Error;

        // Assert
        Assert.True(eventInvoked);
    }

    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.Critical)]
    public void LogLevel_WhenSetToEachValue_ShouldPersistCorrectly(LogLevel logLevel)
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();
        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        settingsService.LogLevel = logLevel;

        // Assert
        mockPreferences.Received(1).LogLevelPreference = logLevel;
    }

    [Fact]
    public void LogLevel_WhenSetToSameValue_ShouldNotInvokeChangedEvent()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();
        mockPreferences.LogLevelPreference.Returns(LogLevel.Warning);

        var settingsService = CreateSettingsService(mockPreferences);
        _ = settingsService.LogLevel; // Cache the value

        var eventInvoked = false;
        settingsService.LogLevelChanged = () => eventInvoked = true;

        // Act
        settingsService.LogLevel = LogLevel.Warning;

        // Assert
        Assert.False(eventInvoked);
    }

    [Fact]
    public void LogLevel_WhenSetToSameValue_ShouldNotUpdatePreferences()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();
        mockPreferences.LogLevelPreference.Returns(LogLevel.Debug);

        var settingsService = CreateSettingsService(mockPreferences);
        _ = settingsService.LogLevel; // Cache the value
        mockPreferences.ClearReceivedCalls();

        // Act
        settingsService.LogLevel = LogLevel.Debug;

        // Assert
        mockPreferences.DidNotReceive().LogLevelPreference = Arg.Any<LogLevel>();
    }

    [Fact]
    public void ShowDisplayPaneOnSelectionChange_WhenAccessedMultipleTimes_ShouldCacheValue()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();
        mockPreferences.DisplayPaneSelectionPreference.Returns(true);

        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        _ = settingsService.ShowDisplayPaneOnSelectionChange;
        _ = settingsService.ShowDisplayPaneOnSelectionChange;
        _ = settingsService.ShowDisplayPaneOnSelectionChange;

        // Assert
        _ = mockPreferences.Received(1).DisplayPaneSelectionPreference;
    }

    [Fact]
    public void ShowDisplayPaneOnSelectionChange_WhenFirstAccessed_ShouldReturnFromPreferences()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();
        mockPreferences.DisplayPaneSelectionPreference.Returns(true);

        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        var result = settingsService.ShowDisplayPaneOnSelectionChange;

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ShowDisplayPaneOnSelectionChange_WhenPreferenceIsDefault_ShouldReturnFalse()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();

        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        var result = settingsService.ShowDisplayPaneOnSelectionChange;

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShowDisplayPaneOnSelectionChange_WhenSet_ShouldUpdatePreferences()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();
        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        settingsService.ShowDisplayPaneOnSelectionChange = true;

        // Assert
        mockPreferences.Received(1).DisplayPaneSelectionPreference = true;
    }

    [Fact]
    public void ShowDisplayPaneOnSelectionChange_WhenSetToSameValue_ShouldNotUpdatePreferences()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();
        mockPreferences.DisplayPaneSelectionPreference.Returns(true);

        var settingsService = CreateSettingsService(mockPreferences);
        _ = settingsService.ShowDisplayPaneOnSelectionChange; // Cache the value
        mockPreferences.ClearReceivedCalls();

        // Act
        settingsService.ShowDisplayPaneOnSelectionChange = true;

        // Assert
        mockPreferences.DidNotReceive().DisplayPaneSelectionPreference = Arg.Any<bool>();
    }

    [Fact]
    public void TimeZoneId_WhenAccessedMultipleTimes_ShouldCacheValue()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();
        mockPreferences.TimeZonePreference.Returns(Constants.TimeZoneUtc);

        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        _ = settingsService.TimeZoneId;
        _ = settingsService.TimeZoneId;
        _ = settingsService.TimeZoneId;

        // Assert
        _ = mockPreferences.Received(1).TimeZonePreference;
    }

    [Fact]
    public void TimeZoneId_WhenFirstAccessed_ShouldReturnFromPreferences()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();
        mockPreferences.TimeZonePreference.Returns(Constants.TimeZonePacific);

        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        var result = settingsService.TimeZoneId;

        // Assert
        Assert.Equal(Constants.TimeZonePacific, result);
    }

    [Fact]
    public void TimeZoneId_WhenPreferenceIsNull_ShouldReturnLocalTimeZone()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();
        mockPreferences.TimeZonePreference.Returns((string)null!);

        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        var result = settingsService.TimeZoneId;

        // Assert
        Assert.Equal(TimeZoneInfo.Local.Id, result);
    }

    [Fact]
    public void TimeZoneId_WhenSet_ShouldUpdatePreferences()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();
        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        settingsService.TimeZoneId = Constants.TimeZoneEastern;

        // Assert
        mockPreferences.Received(1).TimeZonePreference = Constants.TimeZoneEastern;
    }

    [Fact]
    public void TimeZoneId_WhenSetToDifferentValue_ShouldInvokeChangedEvent()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();
        var settingsService = CreateSettingsService(mockPreferences);

        object? receivedSender = null;
        TimeZoneInfo? receivedTimeZone = null;

        settingsService.TimeZoneChanged = (sender, timeZone) =>
        {
            receivedSender = sender;
            receivedTimeZone = timeZone;
        };

        // Act
        settingsService.TimeZoneId = Constants.TimeZoneUtc;

        // Assert
        Assert.Same(settingsService, receivedSender);
        Assert.NotNull(receivedTimeZone);
        Assert.Equal(Constants.TimeZoneUtc, receivedTimeZone.Id);
    }

    [Fact]
    public void TimeZoneId_WhenSetToSameValue_ShouldNotInvokeChangedEvent()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();
        mockPreferences.TimeZonePreference.Returns(Constants.TimeZoneUtc);

        var settingsService = CreateSettingsService(mockPreferences);
        _ = settingsService.TimeZoneId; // Cache the value

        var eventInvoked = false;
        settingsService.TimeZoneChanged = (_, _) => eventInvoked = true;

        // Act
        settingsService.TimeZoneId = Constants.TimeZoneUtc;

        // Assert
        Assert.False(eventInvoked);
    }

    [Fact]
    public void TimeZoneId_WhenSetToSameValue_ShouldNotUpdatePreferences()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();
        mockPreferences.TimeZonePreference.Returns(Constants.TimeZoneUtc);

        var settingsService = CreateSettingsService(mockPreferences);
        _ = settingsService.TimeZoneId; // Cache the value
        mockPreferences.ClearReceivedCalls();

        // Act
        settingsService.TimeZoneId = Constants.TimeZoneUtc;

        // Assert
        mockPreferences.DidNotReceive().TimeZonePreference = Arg.Any<string>();
    }

    [Fact]
    public void TimeZoneInfo_WhenTimeZoneIdChanges_ShouldReturnUpdatedTimeZone()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();
        mockPreferences.TimeZonePreference.Returns(Constants.TimeZoneUtc);

        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        _ = settingsService.TimeZoneInfo; // Initial access
        settingsService.TimeZoneId = Constants.TimeZonePacific;
        var result = settingsService.TimeZoneInfo;

        // Assert
        Assert.Equal(Constants.TimeZonePacific, result.Id);
    }

    [Fact]
    public void TimeZoneInfo_WhenTimeZoneIdIsLocal_ShouldReturnLocalTimeZone()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();
        mockPreferences.TimeZonePreference.Returns(TimeZoneInfo.Local.Id);

        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        var result = settingsService.TimeZoneInfo;

        // Assert
        Assert.Equal(TimeZoneInfo.Local, result);
    }

    [Fact]
    public void TimeZoneInfo_WhenTimeZoneIdIsValid_ShouldReturnCorrectTimeZone()
    {
        // Arrange
        var mockPreferences = Substitute.For<IPreferencesProvider>();
        mockPreferences.TimeZonePreference.Returns(Constants.TimeZoneUtc);

        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        var result = settingsService.TimeZoneInfo;

        // Assert
        Assert.Equal(TimeZoneInfo.Utc, result);
    }

    private static SettingsService CreateSettingsService(IPreferencesProvider? preferencesProvider = null) =>
        new(preferencesProvider ?? Substitute.For<IPreferencesProvider>());
}
