// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.UI.LogTable;

// Shared display vocabulary for a severity level (null = the Unknown/absent slot), so the coverage severity breakdown
// renders the same names the rest of the app uses without hardcoding a second copy.
internal static class SeverityDisplay
{
    public static string Label(SeverityLevel? level) => level?.ToString() ?? "Unknown";

    public static string Key(SeverityLevel? level) => level switch
    {
        SeverityLevel.Critical => "critical",
        SeverityLevel.Error => "error",
        SeverityLevel.Warning => "warning",
        SeverityLevel.Information => "information",
        SeverityLevel.Verbose => "verbose",
        _ => "unknown"
    };
}
