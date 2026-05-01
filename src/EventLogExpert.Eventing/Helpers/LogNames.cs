// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Helpers;

public static class LogNames
{
    public const string ApplicationLog = "Application";
    public const string SecurityLog = "Security";
    public const string StateLog = "State";
    public const string SystemLog = "System";

    /// <summary>Live event log names that require process elevation to read.</summary>
    public static IReadOnlySet<string> AdminOnlyLiveLogNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        SecurityLog,
        StateLog,
    };
}
