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
        var details = new ProviderDetails { ProviderName = "Provider-Name" };

        Assert.True(details.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenEventsPopulated_ReturnsFalse()
    {
        var details = new ProviderDetails
        {
            ProviderName = "Provider-Name",
            Events = [new EventModel { Id = 1, LogName = "Application", Keywords = [] }]
        };

        Assert.False(details.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenKeywordsPopulated_ReturnsFalse()
    {
        var details = new ProviderDetails
        {
            ProviderName = "Provider-Name",
            Keywords = new Dictionary<long, string> { { 1, "kw" } }
        };

        Assert.False(details.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenMessagesPopulated_ReturnsFalse()
    {
        var details = new ProviderDetails
        {
            ProviderName = "Provider-Name",
            Messages = [new MessageModel { ShortId = 1, RawId = 1, Text = "x" }]
        };

        Assert.False(details.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenOpcodesPopulated_ReturnsFalse()
    {
        var details = new ProviderDetails
        {
            ProviderName = "Provider-Name",
            Opcodes = new Dictionary<int, string> { { 1, "op" } }
        };

        Assert.False(details.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenParametersPopulated_ReturnsFalse()
    {
        var details = new ProviderDetails
        {
            ProviderName = "Provider-Name",
            Parameters = [new MessageModel { ShortId = 1, RawId = 1, Text = "p" }]
        };

        Assert.False(details.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenResolvedFromOwningPublisherSet_ReturnsFalse()
    {
        // A non-null ResolvedFromOwningPublisher records that a channel-owner fallback
        // succeeded for this row. Even when the collections are empty, the row is NOT
        // empty in the "provider not found" sense — fallback resolution did locate a
        // publisher. This guards the resolver from re-attempting fallback on a row that
        // has already been fallback-resolved (avoids redundant lookups and keeps the
        // diagnostic field's semantics consistent with IsEmpty).
        var details = new ProviderDetails
        {
            ProviderName = "Channel/Operational",
            ResolvedFromOwningPublisher = "Microsoft-Windows-OwningPublisher"
        };

        Assert.False(details.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenTasksPopulated_ReturnsFalse()
    {
        var details = new ProviderDetails
        {
            ProviderName = "Provider-Name",
            Tasks = new Dictionary<int, string> { { 1, "task" } }
        };

        Assert.False(details.IsEmpty);
    }
}
