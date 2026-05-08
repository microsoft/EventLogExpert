// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Providers;

namespace EventLogExpert.Eventing.Tests.Providers;

public sealed class ProviderDetailsTests
{
    [Fact]
    public void IsEmpty_WhenAllCollectionsEmptyAndNoFallback_ReturnsTrue()
    {
        // Arrange
        var details = new ProviderDetails { ProviderName = "Provider-Name" };

        // Act + Assert
        Assert.True(details.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenEventsPopulated_ReturnsFalse()
    {
        // Arrange
        var details = new ProviderDetails
        {
            ProviderName = "Provider-Name",
            Events = [new EventModel { Id = 1, LogName = "Application", Keywords = [] }]
        };

        // Act + Assert
        Assert.False(details.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenKeywordsPopulated_ReturnsFalse()
    {
        // Arrange
        var details = new ProviderDetails
        {
            ProviderName = "Provider-Name",
            Keywords = new Dictionary<long, string> { { 1, "kw" } }
        };

        // Act + Assert
        Assert.False(details.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenMessagesPopulated_ReturnsFalse()
    {
        // Arrange
        var details = new ProviderDetails
        {
            ProviderName = "Provider-Name",
            Messages = [new MessageModel { ShortId = 1, RawId = 1, Text = "x" }]
        };

        // Act + Assert
        Assert.False(details.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenOpcodesPopulated_ReturnsFalse()
    {
        // Arrange
        var details = new ProviderDetails
        {
            ProviderName = "Provider-Name",
            Opcodes = new Dictionary<int, string> { { 1, "op" } }
        };

        // Act + Assert
        Assert.False(details.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenParametersPopulated_ReturnsFalse()
    {
        // Arrange
        var details = new ProviderDetails
        {
            ProviderName = "Provider-Name",
            Parameters = [new MessageModel { ShortId = 1, RawId = 1, Text = "p" }]
        };

        // Act + Assert
        Assert.False(details.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenResolvedFromOwningPublisherSet_ReturnsFalse()
    {
        // Arrange
        var details = new ProviderDetails
        {
            ProviderName = "Channel/Operational",
            ResolvedFromOwningPublisher = "Microsoft-Windows-OwningPublisher"
        };

        // Act + Assert
        Assert.False(details.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenTasksPopulated_ReturnsFalse()
    {
        // Arrange
        var details = new ProviderDetails
        {
            ProviderName = "Provider-Name",
            Tasks = new Dictionary<int, string> { { 1, "task" } }
        };

        // Act + Assert
        Assert.False(details.IsEmpty);
    }
}
