// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Common.Channels;

public static class LogChannelNames
{
    public const string ApplicationLog = "Application";
    public const string SecurityLog = "Security";
    public const string StateLog = "State";
    public const string SystemLog = "System";

    private static readonly string[] s_securityScopedChannels = [SecurityLog, StateLog];

    /// <summary>Legacy protected live channels retained for catalog validation and export contracts.</summary>
    public static IReadOnlySet<string> AdminOnlyLiveChannels { get; } =
        new HashSet<string>(s_securityScopedChannels, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlySet<string> RegistrySkipChannels { get; } =
        new HashSet<string>(s_securityScopedChannels, StringComparer.OrdinalIgnoreCase);
}
