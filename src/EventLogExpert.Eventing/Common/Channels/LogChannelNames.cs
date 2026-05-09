// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Common.Channels;

public static class LogChannelNames
{
    public const string ApplicationLog = "Application";
    public const string SecurityLog = "Security";
    public const string StateLog = "State";
    public const string SystemLog = "System";

    /// <summary>Live event log channels that require process elevation to read.</summary>
    public static IReadOnlySet<string> AdminOnlyLiveChannels { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        SecurityLog,
        StateLog,
    };
}
