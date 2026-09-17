// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.DatabaseTools;

internal enum DatabaseUpgradeSkipReason
{
    BackupRequired,
    UpgradeInProgress,
    AlreadyUpToDate,
    ClassificationPending,
    UnrecognizedSchema,
    ObsoleteSchema,
    ClassificationFailed,
    NotEligible
}
