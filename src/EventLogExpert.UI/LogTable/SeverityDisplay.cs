// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.UI.LogTable;

internal static class SeverityDisplay
{
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
