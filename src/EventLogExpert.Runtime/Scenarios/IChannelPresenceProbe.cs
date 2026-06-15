// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Scenarios;

/// <summary>Reports which event-log channels exist on this host, cached per session.</summary>
internal interface IChannelPresenceProbe
{
    /// <summary>Present channel names; empty if the channel set could not be read.</summary>
    IReadOnlySet<string> GetPresentChannels();

    bool IsPresent(string logName);

    /// <summary>Warms the cache on a background thread.</summary>
    Task PrimeAsync();
}
