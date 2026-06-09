// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Common.Events;

public static class SeverityFormatter
{
    /// <summary>Maps an ETW level byte to its display string.</summary>
    /// <remarks>
    ///     ETW level 0 is technically "LogAlways", but the Windows Event Viewer MMC and wevtutil both render it as
    ///     "Information". We match that behavior intentionally.
    /// </remarks>
    public static string Format(byte? level) => level switch
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
