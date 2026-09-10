// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Database;

public abstract record DatabaseFailureReason
{
    private protected DatabaseFailureReason() { }

    public sealed record CannotUpgradeStatus(DatabaseStatus Status) : DatabaseFailureReason;

    public sealed record CancellationRollbackFailed(string FileName) : DatabaseFailureReason;

    public sealed record EntryNotFound : DatabaseFailureReason;

    public sealed record ImportOpenArchiveFailed(string Detail) : DatabaseFailureReason;

    public sealed record MigrationRollbackFailed(string FileName, string Detail) : DatabaseFailureReason;

    public sealed record NativeDetail(string Detail) : DatabaseFailureReason;

    public sealed record RecoveryRequiredBakAlreadyPresent : DatabaseFailureReason;

    public sealed record RecoveryRequiredBakAppearedDuringBackup : DatabaseFailureReason;

    public sealed record RecoveryRequiredBackupExists : DatabaseFailureReason;

    public sealed record RecoveryRequiredResolveFirst : DatabaseFailureReason;

    public sealed record UpgradeCleanupFailed : DatabaseFailureReason;

    public sealed record UpgradeVerificationFailed : DatabaseFailureReason;

    public sealed record VerificationOrCleanupRollbackFailed(string FileName) : DatabaseFailureReason;
}
