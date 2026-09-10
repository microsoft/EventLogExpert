// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Database;
using EventLogExpert.UI.Database;
using EventLogExpert.UI.Tests.Localization;
using EventLogExpert.UI.Tests.TestUtils;
using System.Xml.Linq;

namespace EventLogExpert.UI.Tests.Database;

public sealed class DatabaseStatusLocalizerTests
{
    private readonly MarkerLocalizer _localizer = new();

    public static TheoryData<DatabaseStatus, string> StatusKeys()
    {
        TheoryData<DatabaseStatus, string> data = new();

        foreach (DatabaseStatus status in Enum.GetValues<DatabaseStatus>())
        {
            data.Add(status, $"Db_Status_{status}");
        }

        return data;
    }

    public static TheoryData<DatabaseStatus, string> TokenKeys()
    {
        TheoryData<DatabaseStatus, string> data = new();

        foreach (DatabaseStatus status in Enum.GetValues<DatabaseStatus>())
        {
            data.Add(status, $"Db_StatusToken_{status}");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(StatusKeys))]
    public void Describe_RoutesEveryDatabaseStatusToMemberNamedKey(DatabaseStatus status, string expectedKey) =>
        Assert.Equal($"[[{expectedKey}]]", DatabaseStatusLocalizer.Describe(_localizer, status));

    [Fact]
    public void Describe_UnknownStatus_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => DatabaseStatusLocalizer.Describe(_localizer, (DatabaseStatus)999));

    [Theory]
    [MemberData(nameof(TokenKeys))]
    public void NeutralStatusToken_KeepsEnumNameByteIdentity(DatabaseStatus status, string expectedKey) =>
        Assert.Equal(status.ToString(), NeutralValue(expectedKey));

    [Fact]
    public void NeutralStatusValue_KeepClassifyingEllipsisByteIdentity() =>
        Assert.Equal("Classifying…", NeutralValue("Db_Status_NotClassified"));

    [Fact]
    public void RowBadge_BackupExists_UsesRecoveryRequiredBadgeBeforeStatus()
    {
        DatabaseEntry entry = new(
            FileName: "a.db",
            FullPath: @"C:\db\a.db",
            IsEnabled: true,
            Status: DatabaseStatus.Ready,
            BackupExists: true);

        Assert.Equal("[[Db_Badge_RecoveryRequired]]", DatabaseStatusLocalizer.RowBadge(_localizer, entry));
    }

    [Theory]
    [MemberData(nameof(StatusKeys))]
    public void RowBadge_NoBackup_UsesStatusDescription(DatabaseStatus status, string expectedKey)
    {
        DatabaseEntry entry = new(
            FileName: "a.db",
            FullPath: @"C:\db\a.db",
            IsEnabled: true,
            Status: status,
            BackupExists: false);

        Assert.Equal($"[[{expectedKey}]]", DatabaseStatusLocalizer.RowBadge(_localizer, entry));
    }

    [Theory]
    [MemberData(nameof(TokenKeys))]
    public void Token_RoutesEveryDatabaseStatusToTokenKey(DatabaseStatus status, string expectedKey) =>
        Assert.Equal($"[[{expectedKey}]]", DatabaseStatusLocalizer.Token(_localizer, status));

    [Fact]
    public void Token_UnknownStatus_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => DatabaseStatusLocalizer.Token(_localizer, (DatabaseStatus)999));

    private static string NeutralValue(string key) =>
        XDocument.Load(LocalizationSourceScan.ResxPath)
            .Root!
            .Elements("data")
            .Single(element => (string?)element.Attribute("name") == key)
            .Element("value")!
            .Value;
}
