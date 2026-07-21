// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Scenarios;

namespace EventLogExpert.Runtime.Tests.Scenarios;

public sealed class LivePresenceTests
{
    [Fact]
    public void FromReadiness_ExcludesAbsentChannelsFromPresent()
    {
        var presence = LivePresence.FromReadiness(
        [
            new ChannelReadiness("System", ChannelPresence.Present, ChannelEnablement.Enabled),
            new ChannelReadiness("Application", ChannelPresence.Absent, ChannelEnablement.Unknown)
        ]);

        Assert.True(presence.Known);
        Assert.Contains("System", presence.Present);
        Assert.DoesNotContain("Application", presence.Present);
    }

    [Fact]
    public void FromReadiness_WhenAnyChannelUnknown_IsUnknownWithNoChannels()
    {
        var presence = LivePresence.FromReadiness(
            [new ChannelReadiness("System", ChannelPresence.Unknown, ChannelEnablement.Unknown)]);

        Assert.False(presence.Known);
        Assert.Empty(presence.Present);
    }

    [Fact]
    public void FromReadiness_WhenChannelsReadable_IsKnownWithPresentChannels()
    {
        var presence = LivePresence.FromReadiness(
            [new ChannelReadiness("System", ChannelPresence.Present, ChannelEnablement.Enabled)]);

        Assert.True(presence.Known);
        Assert.Contains("System", presence.Present);
    }
}
