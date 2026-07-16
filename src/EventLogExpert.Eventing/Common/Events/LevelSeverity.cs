// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Common.Events;

public static class LevelSeverity
{
    /// <summary>
    ///     Dense severity slots: slot 0 for an unrecognized/absent level plus the five <see cref="SeverityLevel" />
    ///     values (1-5).
    /// </summary>
    public const int SlotCount = 6;

    public static SeverityLevel? FromLevelName(string? level) => level switch
    {
        nameof(SeverityLevel.Critical) => SeverityLevel.Critical,
        nameof(SeverityLevel.Error) => SeverityLevel.Error,
        nameof(SeverityLevel.Warning) => SeverityLevel.Warning,
        nameof(SeverityLevel.Information) => SeverityLevel.Information,
        nameof(SeverityLevel.Verbose) => SeverityLevel.Verbose,
        _ => null
    };

    /// <summary>The dense histogram slot for <paramref name="severity" />: 0 when absent, else the level's 1-5 value.</summary>
    public static int Slot(SeverityLevel? severity) => severity is { } value ? (int)value : 0;
}
