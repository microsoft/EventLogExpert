// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Readers;

internal interface IChannelConfigPropertyReader
{
    ChannelConfigPropertySnapshot ReadProperties(string channelName);
}
