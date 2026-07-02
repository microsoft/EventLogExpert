// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.Runtime.Tests.TestUtils.Constants;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.Settings;

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
    public void CopyFormat_WhenAccessedMultipleTimes_ShouldCacheValue()
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
        mockPreferences.KeyboardCopyFormatPreference.Returns(EventCopyFormat.Full);

        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        _ = settingsService.CopyFormat;
        _ = settingsService.CopyFormat;
        _ = settingsService.CopyFormat;

        // Assert
        _ = mockPreferences.Received(1).KeyboardCopyFormatPreference;
    }

    [Fact]
    public void CopyFormat_WhenFirstAccessed_ShouldReturnFromPreferences()
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
        mockPreferences.KeyboardCopyFormatPreference.Returns(EventCopyFormat.Xml);

        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        var result = settingsService.CopyFormat;

        // Assert
        Assert.Equal(EventCopyFormat.Xml, result);
    }

    [Fact]
    public void CopyFormat_WhenPreferenceIsNull_ShouldReturnDefault()
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();

        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        var result = settingsService.CopyFormat;

        // Assert
        Assert.Equal(EventCopyFormat.Default, result);
    }

    [Fact]
    public void CopyFormat_WhenSet_ShouldUpdatePreferences()
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        settingsService.CopyFormat = EventCopyFormat.Full;

        // Assert
        mockPreferences.Received(1).KeyboardCopyFormatPreference = EventCopyFormat.Full;
    }

    [Fact]
    public void CopyFormat_WhenSetToDifferentValue_ShouldInvokeChangedEvent()
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
        var settingsService = CreateSettingsService(mockPreferences);

        var eventInvoked = false;
        settingsService.CopyFormatChanged = () => eventInvoked = true;

        // Act
        settingsService.CopyFormat = EventCopyFormat.Simple;

        // Assert
        Assert.True(eventInvoked);
    }

    [Theory]
    [InlineData(EventCopyFormat.Default)]
    [InlineData(EventCopyFormat.Simple)]
    [InlineData(EventCopyFormat.Xml)]
    [InlineData(EventCopyFormat.Full)]
    public void CopyFormat_WhenSetToEachValue_ShouldPersistCorrectly(EventCopyFormat copyFormat)
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        settingsService.CopyFormat = copyFormat;

        // Assert
        mockPreferences.Received(1).KeyboardCopyFormatPreference = copyFormat;
    }

    [Fact]
    public void CopyFormat_WhenSetToSameValue_ShouldNotInvokeChangedEvent()
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
        mockPreferences.KeyboardCopyFormatPreference.Returns(EventCopyFormat.Xml);

        var settingsService = CreateSettingsService(mockPreferences);
        _ = settingsService.CopyFormat; // Cache the value

        var eventInvoked = false;
        settingsService.CopyFormatChanged = () => eventInvoked = true;

        // Act
        settingsService.CopyFormat = EventCopyFormat.Xml;

        // Assert
        Assert.False(eventInvoked);
    }

    [Fact]
    public void CopyFormat_WhenSetToSameValue_ShouldNotUpdatePreferences()
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
        mockPreferences.KeyboardCopyFormatPreference.Returns(EventCopyFormat.Xml);

        var settingsService = CreateSettingsService(mockPreferences);

        // First access caches the value
        _ = settingsService.CopyFormat;
        mockPreferences.ClearReceivedCalls();

        // Act
        settingsService.CopyFormat = EventCopyFormat.Xml;

        // Assert
        mockPreferences.DidNotReceive().KeyboardCopyFormatPreference = Arg.Any<EventCopyFormat>();
    }

    [Fact]
    public void CopyType_WhenSet_ShouldUpdatePreferences()
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        settingsService.CopyFormat = EventCopyFormat.Full;

        // Assert
        mockPreferences.Received(1).KeyboardCopyFormatPreference = EventCopyFormat.Full;
    }

    [Fact]
    public void HasEverEnabledPreRelease_WhenAccessedMultipleTimes_ShouldCacheValue()
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
        mockPreferences.HasEverEnabledPreReleasePreference.Returns(true);

        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        _ = settingsService.HasEverEnabledPreRelease;
        _ = settingsService.HasEverEnabledPreRelease;
        _ = settingsService.HasEverEnabledPreRelease;

        // Assert
        _ = mockPreferences.Received(1).HasEverEnabledPreReleasePreference;
    }

    [Fact]
    public void HasEverEnabledPreRelease_WhenFirstAccessed_ShouldReturnFromPreferences()
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
        mockPreferences.HasEverEnabledPreReleasePreference.Returns(true);

        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        var result = settingsService.HasEverEnabledPreRelease;

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasEverEnabledPreRelease_WhenPreferenceIsDefault_ShouldReturnFalse()
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();

        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        var result = settingsService.HasEverEnabledPreRelease;

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsPreReleaseEnabled_WhenAccessedMultipleTimes_ShouldCacheValue()
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
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
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
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
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();

        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        var result = settingsService.IsPreReleaseEnabled;

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsPreReleaseEnabled_WhenReadAsTrueAndStickyNotSet_ShouldCascadeStickyBit()
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
        mockPreferences.PreReleasePreference.Returns(true);
        mockPreferences.HasEverEnabledPreReleasePreference.Returns(false);

        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        var result = settingsService.IsPreReleaseEnabled;

        // Assert
        Assert.True(result);
        mockPreferences.Received(1).HasEverEnabledPreReleasePreference = true;
    }

    [Fact]
    public void IsPreReleaseEnabled_WhenSet_ShouldUpdatePreferences()
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        settingsService.IsPreReleaseEnabled = true;

        // Assert
        mockPreferences.Received(1).PreReleasePreference = true;
    }

    [Fact]
    public void IsPreReleaseEnabled_WhenSetToFalse_ShouldNotChangeStickyBit()
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
        mockPreferences.PreReleasePreference.Returns(true);

        var settingsService = CreateSettingsService(mockPreferences);
        _ = settingsService.IsPreReleaseEnabled; // Cache the value
        mockPreferences.ClearReceivedCalls();

        // Act
        settingsService.IsPreReleaseEnabled = false;

        // Assert
        mockPreferences.Received(1).PreReleasePreference = false;
        mockPreferences.DidNotReceive().HasEverEnabledPreReleasePreference = Arg.Any<bool>();
    }

    [Fact]
    public void IsPreReleaseEnabled_WhenSetToSameValue_ShouldNotUpdatePreferences()
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
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
    public void IsPreReleaseEnabled_WhenSetToTrueAndChannelNeverEnabled_ShouldCascadeStickyBit()
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        settingsService.IsPreReleaseEnabled = true;

        // Assert
        mockPreferences.Received(1).PreReleasePreference = true;
        mockPreferences.Received(1).HasEverEnabledPreReleasePreference = true;
    }

    [Fact]
    public void IsPreReleaseEnabled_WhenSetToTrueAndChannelPreviouslyEnabled_ShouldNotRewriteStickyBit()
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
        mockPreferences.HasEverEnabledPreReleasePreference.Returns(true);

        var settingsService = CreateSettingsService(mockPreferences);
        _ = settingsService.HasEverEnabledPreRelease; // Cache the value
        mockPreferences.ClearReceivedCalls();

        // Act
        settingsService.IsPreReleaseEnabled = true;

        // Assert
        mockPreferences.Received(1).PreReleasePreference = true;
        mockPreferences.DidNotReceive().HasEverEnabledPreReleasePreference = Arg.Any<bool>();
    }

    [Fact]
    public void LogLevel_WhenAccessedMultipleTimes_ShouldCacheValue()
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
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
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
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
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();

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
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
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
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
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
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
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
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
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
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
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
    public void Theme_WhenAccessedMultipleTimes_ShouldCacheValue()
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
        mockPreferences.ThemePreference.Returns(Theme.Light);

        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        _ = settingsService.Theme;
        _ = settingsService.Theme;
        _ = settingsService.Theme;

        // Assert
        _ = mockPreferences.Received(1).ThemePreference;
    }

    [Fact]
    public void Theme_WhenFirstAccessed_ShouldReturnFromPreferences()
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
        mockPreferences.ThemePreference.Returns(Theme.Light);

        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        var result = settingsService.Theme;

        // Assert
        Assert.Equal(Theme.Light, result);
    }

    [Fact]
    public void Theme_WhenPreferenceIsDefault_ShouldReturnSystem()
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();

        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        var result = settingsService.Theme;

        // Assert
        Assert.Equal(Theme.System, result);
    }

    [Fact]
    public void Theme_WhenSet_ShouldUpdatePreferences()
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        settingsService.Theme = Theme.Dark;

        // Assert
        mockPreferences.Received(1).ThemePreference = Theme.Dark;
    }

    [Fact]
    public void Theme_WhenSetToDifferentValue_ShouldInvokeChangedEvent()
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
        var settingsService = CreateSettingsService(mockPreferences);

        var eventInvoked = false;
        settingsService.ThemeChanged = () => eventInvoked = true;

        // Act
        settingsService.Theme = Theme.Light;

        // Assert
        Assert.True(eventInvoked);
    }

    [Theory]
    [InlineData(Theme.System)]
    [InlineData(Theme.Light)]
    [InlineData(Theme.Dark)]
    public void Theme_WhenSetToEachValue_ShouldPersistCorrectly(Theme theme)
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        settingsService.Theme = theme;

        // Assert
        mockPreferences.Received(1).ThemePreference = theme;
    }

    [Fact]
    public void Theme_WhenSetToSameValue_ShouldNotInvokeChangedEvent()
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
        mockPreferences.ThemePreference.Returns(Theme.Light);

        var settingsService = CreateSettingsService(mockPreferences);
        _ = settingsService.Theme; // Cache the value

        var eventInvoked = false;
        settingsService.ThemeChanged = () => eventInvoked = true;

        // Act
        settingsService.Theme = Theme.Light;

        // Assert
        Assert.False(eventInvoked);
    }

    [Fact]
    public void Theme_WhenSetToSameValue_ShouldNotUpdatePreferences()
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
        mockPreferences.ThemePreference.Returns(Theme.Dark);

        var settingsService = CreateSettingsService(mockPreferences);
        _ = settingsService.Theme; // Cache the value
        mockPreferences.ClearReceivedCalls();

        // Act
        settingsService.Theme = Theme.Dark;

        // Assert
        mockPreferences.DidNotReceive().ThemePreference = Arg.Any<Theme>();
    }

    [Fact]
    public void TimeZoneId_WhenAccessedMultipleTimes_ShouldCacheValue()
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
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
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
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
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
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
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
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
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
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
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
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
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
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
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
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
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
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
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
        mockPreferences.TimeZonePreference.Returns(Constants.TimeZoneUtc);

        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        var result = settingsService.TimeZoneInfo;

        // Assert
        Assert.Equal(TimeZoneInfo.Utc, result);
    }

    [Fact]
    public void VerboseResolution_WhenFirstAccessed_ShouldReturnFromPreferences()
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
        mockPreferences.VerboseResolutionPreference.Returns(true);

        var settingsService = CreateSettingsService(mockPreferences);

        // Act & Assert
        Assert.True(settingsService.VerboseResolution);
    }

    [Fact]
    public void VerboseResolution_WhenPreferenceUnset_ShouldDefaultToFalse()
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();

        var settingsService = CreateSettingsService(mockPreferences);

        // Act & Assert
        Assert.False(settingsService.VerboseResolution);
    }

    [Fact]
    public void VerboseResolution_WhenSet_ShouldUpdatePreferences()
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
        var settingsService = CreateSettingsService(mockPreferences);

        // Act
        settingsService.VerboseResolution = true;

        // Assert
        mockPreferences.Received(1).VerboseResolutionPreference = true;
    }

    [Fact]
    public void VerboseResolution_WhenSetToDifferentValue_ShouldInvokeChangedEvent()
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
        var settingsService = CreateSettingsService(mockPreferences);

        var eventInvoked = false;
        settingsService.VerboseResolutionChanged = () => eventInvoked = true;

        // Act
        settingsService.VerboseResolution = true;

        // Assert
        Assert.True(eventInvoked);
    }

    [Fact]
    public void VerboseResolution_WhenSetToSameValue_ShouldNotInvokeChangedEvent()
    {
        // Arrange
        var mockPreferences = Substitute.For<ISettingsPreferencesProvider>();
        mockPreferences.VerboseResolutionPreference.Returns(true);

        var settingsService = CreateSettingsService(mockPreferences);
        _ = settingsService.VerboseResolution; // Cache the value

        var eventInvoked = false;
        settingsService.VerboseResolutionChanged = () => eventInvoked = true;

        // Act
        settingsService.VerboseResolution = true;

        // Assert
        Assert.False(eventInvoked);
    }

    private static SettingsService CreateSettingsService(ISettingsPreferencesProvider? preferencesProvider = null) =>
        new(preferencesProvider ?? Substitute.For<ISettingsPreferencesProvider>());
}
