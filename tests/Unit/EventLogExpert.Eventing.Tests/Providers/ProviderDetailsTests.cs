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
        var details = EventUtils.CreateProvider(Constants.TestProviderLongName, events: [EventUtils.CreateEventModel(1, logName: Constants.ApplicationLogName)]);

        // Act + Assert
        Assert.False(details.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenKeywordsPopulated_ReturnsFalse()
    {
        // Arrange
        var details = EventUtils.CreateProvider(Constants.TestProviderLongName, keywords: new Dictionary<long, string> { { 1, "kw" } });

        // Act + Assert
        Assert.False(details.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenMessagesPopulated_ReturnsFalse()
    {
        // Arrange
        var details = EventUtils.CreateProvider(Constants.TestProviderLongName, messages: [new MessageModel { ShortId = 1, RawId = 1, Text = "x" }]);

        // Act + Assert
        Assert.False(details.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenOpcodesPopulated_ReturnsFalse()
    {
        // Arrange
        var details = EventUtils.CreateProvider(Constants.TestProviderLongName, opcodes: new Dictionary<int, string> { { 1, "op" } });

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
        var details = EventUtils.CreateProvider("Channel/Operational", resolvedFromOwningPublisher: "Microsoft-Windows-OwningPublisher");

        // Act + Assert
        Assert.False(details.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenTasksPopulated_ReturnsFalse()
    {
        // Arrange
        var details = EventUtils.CreateProvider(Constants.TestProviderLongName, tasks: new Dictionary<int, string> { { 1, "task" } });

        // Act + Assert
        Assert.False(details.IsEmpty);
    }
}
