// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Database;
using EventLogExpert.UI.Database;
using EventLogExpert.UI.Tests.Localization;
using EventLogExpert.UI.Tests.TestUtils;
using System.Xml.Linq;

namespace EventLogExpert.UI.Tests.Database;

public sealed class DatabaseFailureReasonLocalizerTests
{
    private readonly MarkerLocalizer _localizer = new();

    public static TheoryData<DatabaseFailureReason, string> KeyedReasons() => new()
    {
        { new DatabaseFailureReason.EntryNotFound(), "[[Db_Fail_EntryNotFound]]" },
        { new DatabaseFailureReason.RecoveryRequiredResolveFirst(), "[[Db_Fail_RecoveryRequiredResolveFirst]]" },
        { new DatabaseFailureReason.CannotUpgradeStatus(DatabaseStatus.ObsoleteSchema), "[[Db_Fail_CannotUpgradeStatus([[Db_StatusToken_ObsoleteSchema]])]]" },
        { new DatabaseFailureReason.RecoveryRequiredBackupExists(), "[[Db_Fail_RecoveryRequiredBackupExists]]" },
        { new DatabaseFailureReason.RecoveryRequiredBakAlreadyPresent(), "[[Db_Fail_RecoveryRequiredBakAlreadyPresent]]" },
        { new DatabaseFailureReason.RecoveryRequiredBakAppearedDuringBackup(), "[[Db_Fail_RecoveryRequiredBakAppearedDuringBackup]]" },
        { new DatabaseFailureReason.UpgradeVerificationFailed(), "[[Db_Fail_UpgradeVerificationFailed]]" },
        { new DatabaseFailureReason.UpgradeCleanupFailed(), "[[Db_Fail_UpgradeCleanupFailed]]" },
        { new DatabaseFailureReason.CancellationRollbackFailed("a.db"), "[[Db_Fail_CancellationRollbackFailed(a.db)]]" },
        { new DatabaseFailureReason.MigrationRollbackFailed("a.db", "native detail"), "[[Db_Fail_MigrationRollbackFailed(a.db|native detail)]]" },
        { new DatabaseFailureReason.VerificationOrCleanupRollbackFailed("a.db"), "[[Db_Fail_VerificationOrCleanupRollbackFailed(a.db)]]" },
        { new DatabaseFailureReason.ImportOpenArchiveFailed("bad zip"), "[[Db_Fail_ImportOpenArchiveFailed(bad zip)]]" }
    };

    [Fact]
    public void Describe_HandlesEveryConcreteReasonLeaf()
    {
        foreach (var leafType in typeof(DatabaseFailureReason).GetNestedTypes().Where(type => !type.IsAbstract))
        {
            var reason = CreateReason(leafType);
            var actual = DatabaseFailureReasonLocalizer.Describe(_localizer, reason);

            if (leafType == typeof(DatabaseFailureReason.NativeDetail))
            {
                Assert.Equal("native detail", actual);
            }
            else
            {
                Assert.StartsWith($"[[Db_Fail_{leafType.Name}", actual, StringComparison.Ordinal);
                Assert.EndsWith("]]", actual, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Describe_NativeDetail_PassesDetailThroughVerbatim() =>
        Assert.Equal("native detail", DatabaseFailureReasonLocalizer.Describe(_localizer, new DatabaseFailureReason.NativeDetail("native detail")));

    [Theory]
    [MemberData(nameof(KeyedReasons))]
    public void Describe_RoutesEveryKeyedReasonToExpectedKey(DatabaseFailureReason reason, string expected) =>
        Assert.Equal(expected, DatabaseFailureReasonLocalizer.Describe(_localizer, reason));

    [Fact]
    public void NeutralReasonValue_KeepRecoveryDashByteIdentity() =>
        Assert.Equal(
            "Recovery required — .upgrade.bak already present",
            NeutralValue("Db_Fail_RecoveryRequiredBakAlreadyPresent"));

    private static DatabaseFailureReason CreateReason(Type leafType) =>
        leafType == typeof(DatabaseFailureReason.CannotUpgradeStatus) ? new DatabaseFailureReason.CannotUpgradeStatus(DatabaseStatus.ObsoleteSchema) :
        leafType == typeof(DatabaseFailureReason.CancellationRollbackFailed) ? new DatabaseFailureReason.CancellationRollbackFailed("a.db") :
        leafType == typeof(DatabaseFailureReason.EntryNotFound) ? new DatabaseFailureReason.EntryNotFound() :
        leafType == typeof(DatabaseFailureReason.ImportOpenArchiveFailed) ? new DatabaseFailureReason.ImportOpenArchiveFailed("bad zip") :
        leafType == typeof(DatabaseFailureReason.MigrationRollbackFailed) ? new DatabaseFailureReason.MigrationRollbackFailed("a.db", "native detail") :
        leafType == typeof(DatabaseFailureReason.NativeDetail) ? new DatabaseFailureReason.NativeDetail("native detail") :
        leafType == typeof(DatabaseFailureReason.RecoveryRequiredBakAlreadyPresent) ? new DatabaseFailureReason.RecoveryRequiredBakAlreadyPresent() :
        leafType == typeof(DatabaseFailureReason.RecoveryRequiredBakAppearedDuringBackup) ? new DatabaseFailureReason.RecoveryRequiredBakAppearedDuringBackup() :
        leafType == typeof(DatabaseFailureReason.RecoveryRequiredBackupExists) ? new DatabaseFailureReason.RecoveryRequiredBackupExists() :
        leafType == typeof(DatabaseFailureReason.RecoveryRequiredResolveFirst) ? new DatabaseFailureReason.RecoveryRequiredResolveFirst() :
        leafType == typeof(DatabaseFailureReason.UpgradeCleanupFailed) ? new DatabaseFailureReason.UpgradeCleanupFailed() :
        leafType == typeof(DatabaseFailureReason.UpgradeVerificationFailed) ? new DatabaseFailureReason.UpgradeVerificationFailed() :
        leafType == typeof(DatabaseFailureReason.VerificationOrCleanupRollbackFailed) ? new DatabaseFailureReason.VerificationOrCleanupRollbackFailed("a.db") :
        throw new InvalidOperationException($"No test fixture for {leafType.FullName}.");

    private static string NeutralValue(string key) =>
        XDocument.Load(LocalizationSourceScan.ResxPath)
            .Root!
            .Elements("data")
            .Single(element => (string?)element.Attribute("name") == key)
            .Element("value")!
            .Value;
}
