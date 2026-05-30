// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Database;

namespace EventLogExpert.UI.Database;

public static class DatabaseEntryEligibility
{
    public static bool IsUpgradeEligible(DatabaseEntry entry, bool isUpgrading)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.BackupExists) { return false; }

        if (isUpgrading) { return false; }

        return entry.Status is DatabaseStatus.UpgradeRequired
            or DatabaseStatus.UpgradeFailed;
    }
}
