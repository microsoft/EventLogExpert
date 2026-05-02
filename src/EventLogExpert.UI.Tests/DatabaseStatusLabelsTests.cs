// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI;

namespace EventLogExpert.UI.Tests;

public sealed class DatabaseStatusLabelsTests
{
    [Fact]
    public void GetDisplayLabel_ClassificationFailed_ReturnsClassificationFailed()
    {
        Assert.Equal("Classification failed", DatabaseStatusLabels.GetDisplayLabel(DatabaseStatus.ClassificationFailed));
    }

    [Fact]
    public void GetDisplayLabel_EveryEnumValue_ReturnsNonEmptyString()
    {
        foreach (var value in Enum.GetValues<DatabaseStatus>())
        {
            var label = DatabaseStatusLabels.GetDisplayLabel(value);

            Assert.False(string.IsNullOrEmpty(label),
                $"DatabaseStatus.{value} should map to a non-empty label.");
        }
    }

    [Fact]
    public void GetDisplayLabel_NotClassified_ReturnsClassifyingWithEllipsis()
    {
        Assert.Equal("Classifying\u2026", DatabaseStatusLabels.GetDisplayLabel(DatabaseStatus.NotClassified));
    }

    [Fact]
    public void GetDisplayLabel_ObsoleteSchema_ReturnsObsolete()
    {
        Assert.Equal("Obsolete", DatabaseStatusLabels.GetDisplayLabel(DatabaseStatus.ObsoleteSchema));
    }

    [Fact]
    public void GetDisplayLabel_Ready_ReturnsReady()
    {
        Assert.Equal("Ready", DatabaseStatusLabels.GetDisplayLabel(DatabaseStatus.Ready));
    }

    [Fact]
    public void GetDisplayLabel_UndefinedEnumValue_ReturnsEnumString()
    {
        const DatabaseStatus undefined = (DatabaseStatus)999;

        Assert.Equal(undefined.ToString(), DatabaseStatusLabels.GetDisplayLabel(undefined));
    }

    [Fact]
    public void GetDisplayLabel_UnrecognizedSchema_ReturnsUnrecognized()
    {
        Assert.Equal("Unrecognized", DatabaseStatusLabels.GetDisplayLabel(DatabaseStatus.UnrecognizedSchema));
    }

    [Fact]
    public void GetDisplayLabel_UpgradeFailed_ReturnsUpgradeFailed()
    {
        Assert.Equal("Upgrade failed", DatabaseStatusLabels.GetDisplayLabel(DatabaseStatus.UpgradeFailed));
    }

    [Fact]
    public void GetDisplayLabel_UpgradeRequired_ReturnsUpgradeRequired()
    {
        Assert.Equal("Upgrade required", DatabaseStatusLabels.GetDisplayLabel(DatabaseStatus.UpgradeRequired));
    }
}
