// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Readers;

namespace EventLogExpert.Eventing.IntegrationTests.Readers;

public sealed class ChannelConfigReaderIntegrationTests
{
    [Fact]
    public void ReadConfig_WhenApplicationLogExists_ReturnsEnabled()
    {
        using EventLogChannelConfigReader reader = new();

        var config = reader.ReadConfig(LogChannelNames.ApplicationLog);

        Assert.True(config.Enabled);
    }
}
