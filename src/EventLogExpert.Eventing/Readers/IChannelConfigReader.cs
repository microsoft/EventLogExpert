// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Readers;

public interface IChannelConfigReader
{
    ChannelConfig ReadConfig(string channelName);
}
