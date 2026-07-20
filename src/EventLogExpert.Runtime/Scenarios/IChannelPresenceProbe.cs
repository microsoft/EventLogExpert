// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Scenarios;

internal interface IChannelPresenceProbe
{
    IReadOnlySet<string> GetPresentChannels();

    bool IsPresent(string logName);

    Task PrimeAsync();

    IReadOnlySet<string>? TryGetPresentChannels();
}
