// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.Database;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Database;

internal static class DatabaseStatusLocalizer
{
    internal static string Describe(IStringLocalizer<SharedResource> localizer, DatabaseStatus status) => status switch
    {
        DatabaseStatus.NotClassified => localizer["Db_Status_NotClassified"],
        DatabaseStatus.Ready => localizer["Db_Status_Ready"],
        DatabaseStatus.UpgradeRequired => localizer["Db_Status_UpgradeRequired"],
        DatabaseStatus.UpgradeFailed => localizer["Db_Status_UpgradeFailed"],
        DatabaseStatus.UnrecognizedSchema => localizer["Db_Status_UnrecognizedSchema"],
        DatabaseStatus.ObsoleteSchema => localizer["Db_Status_ObsoleteSchema"],
        DatabaseStatus.ClassificationFailed => localizer["Db_Status_ClassificationFailed"],
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    internal static string RowBadge(IStringLocalizer<SharedResource> localizer, DatabaseEntry entry) =>
        entry.BackupExists ? localizer["Db_Badge_RecoveryRequired"] : Describe(localizer, entry.Status);

    internal static string Token(IStringLocalizer<SharedResource> localizer, DatabaseStatus status) => status switch
    {
        DatabaseStatus.NotClassified => localizer["Db_StatusToken_NotClassified"],
        DatabaseStatus.Ready => localizer["Db_StatusToken_Ready"],
        DatabaseStatus.UpgradeRequired => localizer["Db_StatusToken_UpgradeRequired"],
        DatabaseStatus.UpgradeFailed => localizer["Db_StatusToken_UpgradeFailed"],
        DatabaseStatus.UnrecognizedSchema => localizer["Db_StatusToken_UnrecognizedSchema"],
        DatabaseStatus.ObsoleteSchema => localizer["Db_StatusToken_ObsoleteSchema"],
        DatabaseStatus.ClassificationFailed => localizer["Db_StatusToken_ClassificationFailed"],
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };
}
