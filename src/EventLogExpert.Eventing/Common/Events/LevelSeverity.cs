// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Common.Events;

public static class LevelSeverity
{
    public static SeverityLevel? FromLevelName(string? level) => level switch
    {
        nameof(SeverityLevel.Critical) => SeverityLevel.Critical,
        nameof(SeverityLevel.Error) => SeverityLevel.Error,
        nameof(SeverityLevel.Warning) => SeverityLevel.Warning,
        nameof(SeverityLevel.Information) => SeverityLevel.Information,
        nameof(SeverityLevel.Verbose) => SeverityLevel.Verbose,
        _ => null
    };
}
