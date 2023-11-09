// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Helpers;

public enum SeverityLevel
{
    Information,
    Warning,
    Error
}

public static class Severity
{
    public static string GetString(byte? level) => level switch
    {
        0 => nameof(SeverityLevel.Information),
        2 => nameof(SeverityLevel.Error),
        3 => nameof(SeverityLevel.Warning),
        4 => nameof(SeverityLevel.Information),
        _ => level?.ToString() ?? string.Empty
    };
}
