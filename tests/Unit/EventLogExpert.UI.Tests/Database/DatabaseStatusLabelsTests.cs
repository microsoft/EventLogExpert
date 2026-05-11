// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Database;

namespace EventLogExpert.UI.Tests.Database;

public sealed class DatabaseStatusLabelsTests
{
    [Fact]
    public void GetDisplayLabel_ClassificationFailed_ReturnsClassificationFailed()
    {
        // Act + Assert
        Assert.Equal("Classification failed", DatabaseStatusLabels.GetDisplayLabel(DatabaseStatus.ClassificationFailed));
    }

    [Fact]
    public void GetDisplayLabel_EveryEnumValue_ReturnsNonEmptyString()
    {
        foreach (var value in Enum.GetValues<DatabaseStatus>())
        {
            // Act
            var label = DatabaseStatusLabels.GetDisplayLabel(value);

            // Assert
            Assert.False(string.IsNullOrEmpty(label),
                $"DatabaseStatus.{value} should map to a non-empty label.");
        }
    }

    [Fact]
    public void GetDisplayLabel_NotClassified_ReturnsClassifyingWithEllipsis()
    {
        // Act + Assert
        Assert.Equal("Classifying\u2026", DatabaseStatusLabels.GetDisplayLabel(DatabaseStatus.NotClassified));
    }

    [Fact]
    public void GetDisplayLabel_ObsoleteSchema_ReturnsObsolete()
    {
        // Act + Assert
        Assert.Equal("Obsolete", DatabaseStatusLabels.GetDisplayLabel(DatabaseStatus.ObsoleteSchema));
    }

    [Fact]
    public void GetDisplayLabel_Ready_ReturnsReady()
    {
        // Act + Assert
        Assert.Equal("Ready", DatabaseStatusLabels.GetDisplayLabel(DatabaseStatus.Ready));
    }

    [Fact]
    public void GetDisplayLabel_UndefinedEnumValue_ReturnsEnumString()
    {
        // Arrange
        const DatabaseStatus undefined = (DatabaseStatus)999;

        // Act + Assert
        Assert.Equal(undefined.ToString(), DatabaseStatusLabels.GetDisplayLabel(undefined));
    }

    [Fact]
    public void GetDisplayLabel_UnrecognizedSchema_ReturnsUnrecognized()
    {
        // Act + Assert
        Assert.Equal("Unrecognized", DatabaseStatusLabels.GetDisplayLabel(DatabaseStatus.UnrecognizedSchema));
    }

    [Fact]
    public void GetDisplayLabel_UpgradeFailed_ReturnsUpgradeFailed()
    {
        // Act + Assert
        Assert.Equal("Upgrade failed", DatabaseStatusLabels.GetDisplayLabel(DatabaseStatus.UpgradeFailed));
    }

    [Fact]
    public void GetDisplayLabel_UpgradeRequired_ReturnsUpgradeRequired()
    {
        // Act + Assert
        Assert.Equal("Upgrade required", DatabaseStatusLabels.GetDisplayLabel(DatabaseStatus.UpgradeRequired));
    }

    [Fact]
    public void GetRowBadgeLabel_BackupExistsFalse_DelegatesToGetDisplayLabel()
    {
        foreach (var status in Enum.GetValues<DatabaseStatus>())
        {
            // Arrange
            var entry = new DatabaseEntry("a.db", @"C:\dbs\a.db", IsEnabled: false, status);

            // Act + Assert
            Assert.Equal(
                DatabaseStatusLabels.GetDisplayLabel(status),
                DatabaseStatusLabels.GetRowBadgeLabel(entry));
        }
    }

    [Fact]
    public void GetRowBadgeLabel_BackupExistsTrue_ReturnsRecoveryRequired_RegardlessOfStatus()
    {
        foreach (var status in Enum.GetValues<DatabaseStatus>())
        {
            // Arrange
            var entry = new DatabaseEntry("a.db", @"C:\dbs\a.db", IsEnabled: false, status, BackupExists: true);

            // Act + Assert
            Assert.Equal("Recovery required", DatabaseStatusLabels.GetRowBadgeLabel(entry));
        }
    }

    [Fact]
    public void GetRowBadgeLabel_ReadyAndBackupExists_PreferRecoveryRequired_OverReady()
    {
        // Arrange
        var entry = new DatabaseEntry("a.db", @"C:\dbs\a.db", IsEnabled: false, DatabaseStatus.Ready, BackupExists: true);

        // Act + Assert
        Assert.Equal("Recovery required", DatabaseStatusLabels.GetRowBadgeLabel(entry));
    }
}
