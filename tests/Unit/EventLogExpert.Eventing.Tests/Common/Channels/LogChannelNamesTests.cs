// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;

namespace EventLogExpert.Eventing.Tests.Common.Channels;

public sealed class LogChannelNamesTests
{
    [Fact]
    public void AdminOnlyLiveChannels_ContainsExpectedNames()
    {
        Assert.Contains(LogChannelNames.SecurityLog, LogChannelNames.AdminOnlyLiveChannels);
        Assert.Contains(LogChannelNames.StateLog, LogChannelNames.AdminOnlyLiveChannels);
        Assert.Equal(2, LogChannelNames.AdminOnlyLiveChannels.Count);
    }

    [Theory]
    [InlineData("security")]
    [InlineData("SECURITY")]
    [InlineData("state")]
    [InlineData("STATE")]
    public void AdminOnlyLiveChannels_ShouldMatchCaseInsensitively(string input)
    {
        Assert.Contains(input, LogChannelNames.AdminOnlyLiveChannels);
    }

    [Fact]
    public void Constants_HaveExpectedValues()
    {
        Assert.Equal("Application", LogChannelNames.ApplicationLog);
        Assert.Equal("Security", LogChannelNames.SecurityLog);
        Assert.Equal("State", LogChannelNames.StateLog);
        Assert.Equal("System", LogChannelNames.SystemLog);
    }
}
