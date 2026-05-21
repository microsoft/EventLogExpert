// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Providers;
using EventLogExpert.Eventing.TestUtils;
using EventLogExpert.Eventing.TestUtils.Constants;

namespace EventLogExpert.Eventing.Tests.Providers;

public sealed class ProviderDetailsTests
{
    [Fact]
    public void IsEmpty_WhenAllCollectionsEmptyAndNoFallback_ReturnsTrue()
    {
        // Arrange
        var details = EventUtils.CreateProvider(Constants.TestProviderLongName);

        // Act + Assert
        Assert.True(details.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenEventsPopulated_ReturnsFalse()
    {
        // Arrange
        var details = EventUtils.CreateProvider(Constants.TestProviderLongName,
            events: [EventUtils.CreateEventModel(1, logName: Constants.ApplicationLogName)]);

        // Act + Assert
        Assert.False(details.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenKeywordsPopulated_ReturnsFalse()
    {
        // Arrange
        var details = EventUtils.CreateProvider(Constants.TestProviderLongName,
            keywords: new Dictionary<long, string> { { 1, "kw" } });

        // Act + Assert
        Assert.False(details.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenMessagesPopulated_ReturnsFalse()
    {
        // Arrange
        var details = EventUtils.CreateProvider(Constants.TestProviderLongName,
            [new MessageModel { ShortId = 1, RawId = 1, Text = "x" }]);

        // Act + Assert
        Assert.False(details.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenOpcodesPopulated_ReturnsFalse()
    {
        // Arrange
        var details = EventUtils.CreateProvider(Constants.TestProviderLongName,
            opcodes: new Dictionary<int, string> { { 1, "op" } });

        // Act + Assert
        Assert.False(details.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenParametersPopulated_ReturnsFalse()
    {
        // Arrange
        var details = new ProviderDetails
        {
            ProviderName = Constants.TestProviderLongName,
            Parameters = [new MessageModel { ShortId = 1, RawId = 1, Text = "p" }]
        };

        // Act + Assert
        Assert.False(details.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenResolvedFromOwningPublisherSet_ReturnsFalse()
    {
        // Arrange
        var details = EventUtils.CreateProvider("Channel/Operational",
            resolvedFromOwningPublisher: "Microsoft-Windows-OwningPublisher");

        // Act + Assert
        Assert.False(details.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenTasksPopulated_ReturnsFalse()
    {
        // Arrange
        var details = EventUtils.CreateProvider(Constants.TestProviderLongName,
            tasks: new Dictionary<int, string> { { 1, "task" } });

        // Act + Assert
        Assert.False(details.IsEmpty);
    }

    [Fact]
    public void GetEventsById_WhenIdExists_ReturnsMatchingEvents()
    {
        // Arrange
        var details = EventUtils.CreateProvider(
            Constants.TestProviderLongName,
            events:
            [
                EventUtils.CreateEventModel(100, logName: Constants.ApplicationLogName),
                EventUtils.CreateEventModel(200, logName: Constants.ApplicationLogName)
            ]);

        // Act
        var result = details.GetEventsById(100);

        // Assert
        Assert.Single(result);
        Assert.Equal(100, result[0].Id);
    }

    [Fact]
    public void GetEventsById_WhenIdDoesNotExist_ReturnsEmptyList()
    {
        // Arrange
        var details = EventUtils.CreateProvider(
            Constants.TestProviderLongName,
            events: [EventUtils.CreateEventModel(100, logName: Constants.ApplicationLogName)]);

        // Act
        var result = details.GetEventsById(999);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetEventsById_WhenMultipleEventsShareId_ReturnsAll()
    {
        // Arrange
        var details = EventUtils.CreateProvider(
            Constants.TestProviderLongName,
            events:
            [
                EventUtils.CreateEventModel(100, version: 0, logName: Constants.ApplicationLogName),
                EventUtils.CreateEventModel(100, version: 1, logName: Constants.ApplicationLogName)
            ]);

        // Act
        var result = details.GetEventsById(100);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, e => Assert.Equal(100, e.Id));
        Assert.Contains(result, e => e.Version == 0);
        Assert.Contains(result, e => e.Version == 1);
    }

    [Fact]
    public void GetMessagesByShortId_WhenPositiveShortIdExists_ReturnsMatchingMessage()
    {
        // Arrange
        var details = EventUtils.CreateProvider(
            Constants.TestProviderLongName,
            [new MessageModel { ShortId = 5, RawId = 5, Text = "positive" }]);

        // Act
        var result = details.GetMessagesByShortId(5);

        // Assert
        Assert.Single(result);
        Assert.Equal("positive", result[0].Text);
    }

    [Fact]
    public void GetMessagesByShortId_WhenNegativeShortId_MatchesViaUnsignedPromotion()
    {
        // Arrange — ShortId is signed short; (ushort)(-1) == 65535
        var details = EventUtils.CreateProvider(
            Constants.TestProviderLongName,
            [new MessageModel { ShortId = -1, RawId = 1, Text = "negative" }]);

        // Act — callers use ushort-to-int promotion: (int)(ushort)(-1) == 65535
        var result = details.GetMessagesByShortId(65535);

        // Assert
        Assert.Single(result);
        Assert.Equal("negative", result[0].Text);
    }

    [Fact]
    public void GetMessagesByShortId_WhenIdDoesNotExist_ReturnsEmptyList()
    {
        // Arrange
        var details = EventUtils.CreateProvider(
            Constants.TestProviderLongName,
            [new MessageModel { ShortId = 1, RawId = 1, Text = "x" }]);

        // Act
        var result = details.GetMessagesByShortId(999);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void EventsSetter_InvalidatesCachedLookup()
    {
        // Arrange
        var details = EventUtils.CreateProvider(
            Constants.TestProviderLongName,
            events: [EventUtils.CreateEventModel(100, logName: Constants.ApplicationLogName)]);

        var beforeReplace = details.GetEventsById(100);
        Assert.Single(beforeReplace);

        // Act — replace Events, which should invalidate the cached lookup
        details.Events = [EventUtils.CreateEventModel(200, logName: Constants.ApplicationLogName)];

        // Assert — old ID no longer found, new ID found
        Assert.Empty(details.GetEventsById(100));
        Assert.Single(details.GetEventsById(200));
    }

    [Fact]
    public void MessagesSetter_InvalidatesCachedLookup()
    {
        // Arrange
        var details = EventUtils.CreateProvider(
            Constants.TestProviderLongName,
            [new MessageModel { ShortId = 1, RawId = 1, Text = "original" }]);

        var beforeReplace = details.GetMessagesByShortId(1);
        Assert.Single(beforeReplace);

        // Act — replace Messages, which should invalidate the cached lookup
        details.Messages = [new MessageModel { ShortId = 2, RawId = 2, Text = "replaced" }];

        // Assert — old ShortId no longer found, new ShortId found
        Assert.Empty(details.GetMessagesByShortId(1));
        Assert.Single(details.GetMessagesByShortId(2));
    }
}
