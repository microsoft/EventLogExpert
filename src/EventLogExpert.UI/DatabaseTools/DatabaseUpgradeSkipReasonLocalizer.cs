// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.DatabaseTools;

internal static class DatabaseUpgradeSkipReasonLocalizer
{
    internal static string Describe(IStringLocalizer<SharedResource> localizer, DatabaseUpgradeSkipReason reason) => reason switch
    {
        DatabaseUpgradeSkipReason.BackupRequired => localizer["DatabaseUpgradeSkipReason_BackupRequired"],
        DatabaseUpgradeSkipReason.UpgradeInProgress => localizer["DatabaseUpgradeSkipReason_UpgradeInProgress"],
        DatabaseUpgradeSkipReason.AlreadyUpToDate => localizer["DatabaseUpgradeSkipReason_AlreadyUpToDate"],
        DatabaseUpgradeSkipReason.ClassificationPending => localizer["DatabaseUpgradeSkipReason_ClassificationPending"],
        DatabaseUpgradeSkipReason.UnrecognizedSchema => localizer["DatabaseUpgradeSkipReason_UnrecognizedSchema"],
        DatabaseUpgradeSkipReason.ObsoleteSchema => localizer["DatabaseUpgradeSkipReason_ObsoleteSchema"],
        DatabaseUpgradeSkipReason.ClassificationFailed => localizer["DatabaseUpgradeSkipReason_ClassificationFailed"],
        DatabaseUpgradeSkipReason.NotEligible => localizer["DatabaseUpgradeSkipReason_NotEligible"],
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
    };
}
