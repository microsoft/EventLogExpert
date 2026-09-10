// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.Database;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Database;

internal static class DatabaseFailureReasonLocalizer
{
    internal static string Describe(IStringLocalizer<SharedResource> localizer, DatabaseFailureReason reason) => reason switch
    {
        DatabaseFailureReason.EntryNotFound => localizer["Db_Fail_EntryNotFound"],
        DatabaseFailureReason.RecoveryRequiredResolveFirst => localizer["Db_Fail_RecoveryRequiredResolveFirst"],
        DatabaseFailureReason.CannotUpgradeStatus cannotUpgradeStatus =>
            localizer["Db_Fail_CannotUpgradeStatus", DatabaseStatusLocalizer.Token(localizer, cannotUpgradeStatus.Status)],
        DatabaseFailureReason.RecoveryRequiredBackupExists => localizer["Db_Fail_RecoveryRequiredBackupExists"],
        DatabaseFailureReason.RecoveryRequiredBakAlreadyPresent => localizer["Db_Fail_RecoveryRequiredBakAlreadyPresent"],
        DatabaseFailureReason.RecoveryRequiredBakAppearedDuringBackup => localizer["Db_Fail_RecoveryRequiredBakAppearedDuringBackup"],
        DatabaseFailureReason.UpgradeVerificationFailed => localizer["Db_Fail_UpgradeVerificationFailed"],
        DatabaseFailureReason.UpgradeCleanupFailed => localizer["Db_Fail_UpgradeCleanupFailed"],
        DatabaseFailureReason.CancellationRollbackFailed cancellationRollbackFailed =>
            localizer["Db_Fail_CancellationRollbackFailed", cancellationRollbackFailed.FileName],
        DatabaseFailureReason.MigrationRollbackFailed migrationRollbackFailed =>
            localizer["Db_Fail_MigrationRollbackFailed", migrationRollbackFailed.FileName, migrationRollbackFailed.Detail],
        DatabaseFailureReason.VerificationOrCleanupRollbackFailed verificationOrCleanupRollbackFailed =>
            localizer["Db_Fail_VerificationOrCleanupRollbackFailed", verificationOrCleanupRollbackFailed.FileName],
        DatabaseFailureReason.ImportOpenArchiveFailed importOpenArchiveFailed =>
            localizer["Db_Fail_ImportOpenArchiveFailed", importOpenArchiveFailed.Detail],
        DatabaseFailureReason.NativeDetail nativeDetail => nativeDetail.Detail,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason.GetType(), null)
    };
}
