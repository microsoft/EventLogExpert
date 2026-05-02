// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI;

/// <summary>
///     Maps a <see cref="DatabaseStatus" /> to the user-facing label shown next to a database entry. Returns a label
///     for every member (including <see cref="DatabaseStatus.Ready" />); whether to render the badge for a given
///     status is a per-screen suppression decision left to the caller.
/// </summary>
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
}
