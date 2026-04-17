// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Helpers;

/// <summary>Severity levels with values matching ETW standard levels (1–5).</summary>
public enum SeverityLevel
{
    Critical = 1,
    Error = 2,
    Warning = 3,
    Information = 4,
    Verbose = 5
}

public static class Severity
{
    /// <summary>Maps an ETW level byte to its display string.</summary>
    /// <remarks>
    /// ETW level 0 is technically "LogAlways", but the Windows Event Viewer MMC and
    /// wevtutil both render it as "Information". We match that behavior intentionally.
    /// </remarks>
    public static string GetString(byte? level) => level switch
    {
        0 => nameof(SeverityLevel.Information), // LogAlways — rendered as Information by Windows
        1 => nameof(SeverityLevel.Critical),
        2 => nameof(SeverityLevel.Error),
        3 => nameof(SeverityLevel.Warning),
        4 => nameof(SeverityLevel.Information),
        5 => nameof(SeverityLevel.Verbose),
        _ => level?.ToString() ?? string.Empty
    };
}
