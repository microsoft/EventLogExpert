// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Database;

public static class DatabaseStatusLabels
{
    public static string GetDisplayLabel(DatabaseStatus status) => status switch
    {
        DatabaseStatus.Ready => "Ready",
        DatabaseStatus.NotClassified => "Classifying\u2026",
        DatabaseStatus.UpgradeRequired => "Upgrade required",
        DatabaseStatus.UpgradeFailed => "Upgrade failed",
        DatabaseStatus.UnrecognizedSchema => "Unrecognized",
        DatabaseStatus.ObsoleteSchema => "Obsolete",
        DatabaseStatus.ClassificationFailed => "Classification failed",
        _ => status.ToString()
    };

    public static string GetRowBadgeLabel(DatabaseEntry entry) => entry.BackupExists
        ? "Recovery required"
        : GetDisplayLabel(entry.Status);
}
