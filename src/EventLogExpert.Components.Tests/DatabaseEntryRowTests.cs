// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.UI;
using EventLogExpert.UI.Models;

namespace EventLogExpert.Components.Tests;

public sealed class DatabaseEntryRowTests : BunitContext
{
    public DatabaseEntryRowTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task RemoveButtonClick_InvokesOnRemove()
    {
        // Arrange
        var entry = MakeEntry(DatabaseStatus.Ready);
        int invocationCount = 0;
        var component = Render<DatabaseEntryRow>(parameters => parameters
            .Add(p => p.Entry, entry)
            .Add(p => p.OnRemove, () => invocationCount++));

        // Act
        await component.Find(".db-entry-remove-btn").ClickAsync(new());

        // Assert
        Assert.Equal(1, invocationCount);
    }

    [Fact]
    public void Render_BackupExistsAndIsUpgrading_StillShowsRecoveryBadge()
    {
        // Arrange
        var entry = MakeEntry(DatabaseStatus.Ready, backupExists: true);

        // Act
        var component = RenderRow(entry, isUpgrading: true);

        // Assert
        var badge = component.Find(".db-entry-badge");
        Assert.Equal("Recovery required", badge.TextContent);
        Assert.Equal("Recovery", badge.GetAttribute("data-badge"));

        Assert.Empty(component.FindAll(".db-entry-upgrading"));
        Assert.Empty(component.FindAll(".db-entry-upgrade-btn"));
        Assert.Empty(component.FindAll(".toggle"));

        // BackupExists hides trash so the user is steered to the recovery dialog instead
        // of triggering a delete that would leave the .upgrade.bak orphaned.
        Assert.Empty(component.FindAll(".db-entry-remove-btn"));
    }

    [Fact]
    public void Render_BackupExistsEntry_OverridesReadyStatus()
    {
        // Arrange
        var entry = MakeEntry(DatabaseStatus.Ready, backupExists: true);

        // Act
        var component = RenderRow(entry);

        // Assert
        var badge = component.Find(".db-entry-badge");
        Assert.Equal("Recovery required", badge.TextContent);
        Assert.Empty(component.FindAll(".toggle"));
        Assert.Empty(component.FindAll(".db-entry-remove-btn"));
    }

    [Fact]
    public void Render_BackupExistsEntry_ShowsRecoveryRequiredBadge_AndHidesTrash()
    {
        // Arrange — BackupExists routes the user to the recovery dialog, so the row hides
        // trash to prevent a delete that would orphan the .upgrade.bak file.
        var entry = MakeEntry(DatabaseStatus.UpgradeRequired, backupExists: true);

        // Act
        var component = RenderRow(entry);

        // Assert
        var badge = component.Find(".db-entry-badge");
        Assert.Equal("Recovery required", badge.TextContent);
        Assert.Equal("Recovery", badge.GetAttribute("data-badge"));

        Assert.Empty(component.FindAll(".db-entry-upgrade-btn"));
        Assert.Empty(component.FindAll(".toggle"));
        Assert.Empty(component.FindAll(".db-entry-upgrading"));

        Assert.Empty(component.FindAll(".db-entry-remove-btn"));
    }

    [Fact]
    public void Render_DisabledEntries_ShowTrashButton()
    {
        foreach (var status in Enum.GetValues<DatabaseStatus>())
        {
            // UpgradeRequired hides trash to steer the user to Upgrade first; it has its
            // own dedicated test below.
            if (status == DatabaseStatus.UpgradeRequired) { continue; }

            // Arrange
            var entry = MakeEntry(status);

            // Act
            var component = RenderRow(entry);

            // Assert — non-BackupExists, non-Upgrading entries always render trash
            // (visibility is governed by CSS hover/focus reveal, not by markup).
            Assert.Single(component.FindAll(".db-entry-remove-btn"));
        }
    }

    [Fact]
    public void Render_FileName_AppearsInRow()
    {
        // Arrange
        var entry = MakeEntry(DatabaseStatus.Ready, fileName: "MyProvider.db");

        // Act
        var component = RenderRow(entry);

        // Assert
        Assert.Equal("MyProvider.db", component.Find(".db-entry-name").TextContent);
    }

    [Fact]
    public void Render_IsUpgradeBlocked_DisablesRetryButton()
    {
        // Arrange
        var entry = MakeEntry(DatabaseStatus.UpgradeFailed);

        // Act
        var component = RenderRow(entry, isUpgradeBlocked: true);

        // Assert
        var button = component.Find(".db-entry-upgrade-btn");
        Assert.True(button.HasAttribute("disabled"));
    }

    [Fact]
    public void Render_IsUpgradeBlocked_DisablesUpgradeButton()
    {
        // Arrange
        var entry = MakeEntry(DatabaseStatus.UpgradeRequired);

        // Act
        var component = RenderRow(entry, isUpgradeBlocked: true);

        // Assert
        var button = component.Find(".db-entry-upgrade-btn");
        Assert.True(button.HasAttribute("disabled"));
    }

    [Fact]
    public void Render_IsUpgrading_ShowsSpinner_AndHidesBadge()
    {
        // Arrange
        var entry = MakeEntry(DatabaseStatus.UpgradeRequired);

        // Act
        var component = RenderRow(entry, isUpgrading: true);

        // Assert
        var upgrading = component.Find(".db-entry-upgrading");
        Assert.Contains("Upgrading", upgrading.TextContent);
        Assert.Single(component.FindAll(".db-entry-upgrading .db-entry-spinner"));

        Assert.Empty(component.FindAll(".db-entry-upgrade-btn"));
        Assert.Empty(component.FindAll(".db-entry-badge"));

        // IsUpgrading hides trash so a delete cannot race with the in-flight upgrade
        // (which is sitting on a per-file reservation inside DatabaseService).
        Assert.Empty(component.FindAll(".db-entry-remove-btn"));
    }

    [Fact]
    public void Render_NonReadyEnabledEntry_ShowsTrashButton()
    {
        // Arrange — non-Ready entries are not loaded by the resolver regardless of IsEnabled,
        // so the file is not locked and removal is safe.
        var entry = MakeEntry(DatabaseStatus.UpgradeFailed, isEnabled: true);

        // Act
        var component = RenderRow(entry);

        // Assert
        Assert.Single(component.FindAll(".db-entry-remove-btn"));
    }

    [Fact]
    public void Render_NotClassifiedEntry_ShowsDisabledToggle_AndClassifyingBadge()
    {
        // Arrange
        var entry = MakeEntry(DatabaseStatus.NotClassified);

        // Act
        var component = RenderRow(entry);

        // Assert
        var radios = component.FindAll(".toggle input[type='radio']");
        Assert.NotEmpty(radios);
        Assert.All(radios, r => Assert.True(r.HasAttribute("disabled")));

        var badge = component.Find(".db-entry-badge");
        Assert.Equal("Classifying\u2026", badge.TextContent);
        Assert.Equal("NotClassified", badge.GetAttribute("data-badge"));
    }

    [Fact]
    public void Render_ReadyEnabledEntry_OptimisticToggleOff_StillRendersTrashButton()
    {
        // Arrange — toggle change is optimistic and not yet committed; trash is in the DOM
        // either way under the new contract.
        var entry = MakeEntry(DatabaseStatus.Ready, isEnabled: true);

        // Act
        var component = RenderRow(entry, effectiveEnabled: false);

        // Assert
        Assert.Single(component.FindAll(".db-entry-remove-btn"));
    }

    [Fact]
    public void Render_ReadyEnabledEntry_RendersTrashButton()
    {
        // Arrange — Ready+Enabled is now safe to delete because RemoveAsync coordinates
        // closing and reopening any open log views around the file delete. The trash is
        // rendered (just visually faded by CSS until the row is hovered or focused).
        var entry = MakeEntry(DatabaseStatus.Ready, isEnabled: true);

        // Act
        var component = RenderRow(entry, effectiveEnabled: true);

        // Assert
        Assert.Single(component.FindAll(".db-entry-remove-btn"));
    }

    [Fact]
    public void Render_ReadyEntry_ShowsToggle_AndNoBadge()
    {
        // Arrange
        var entry = MakeEntry(DatabaseStatus.Ready);

        // Act
        var component = RenderRow(entry);

        // Assert
        Assert.Single(component.FindAll(".toggle"));
        Assert.Empty(component.FindAll(".db-entry-badge"));
        Assert.Empty(component.FindAll(".db-entry-upgrade-btn"));
        Assert.Empty(component.FindAll(".db-entry-upgrading"));
    }

    [Fact]
    public void Render_ReadyEntryWithClassificationPending_ShowsDisabledToggle()
    {
        // Arrange
        var entry = MakeEntry(DatabaseStatus.Ready);

        // Act
        var component = RenderRow(entry, isClassificationPending: true);

        // Assert
        var radios = component.FindAll(".toggle input[type='radio']");
        Assert.NotEmpty(radios);
        Assert.All(radios, r => Assert.True(r.HasAttribute("disabled")));
    }

    [Fact]
    public void Render_RemoveButton_HasAriaLabel()
    {
        // Arrange
        var entry = MakeEntry(DatabaseStatus.Ready, fileName: "MyProvider.db");

        // Act
        var component = RenderRow(entry);

        // Assert
        var button = component.Find(".db-entry-remove-btn");
        Assert.Equal("Remove database MyProvider.db", button.GetAttribute("aria-label"));
    }

    [Theory]
    [InlineData(DatabaseStatus.UnrecognizedSchema, "Unrecognized")]
    [InlineData(DatabaseStatus.ObsoleteSchema, "Obsolete")]
    [InlineData(DatabaseStatus.ClassificationFailed, "Classification failed")]
    public void Render_TerminalStatus_ShowsBadge_AndOnlyTrash(DatabaseStatus status, string expectedLabel)
    {
        // Arrange
        var entry = MakeEntry(status);

        // Act
        var component = RenderRow(entry);

        // Assert
        var badge = component.Find(".db-entry-badge");
        Assert.Equal(expectedLabel, badge.TextContent);
        Assert.Equal(status.ToString(), badge.GetAttribute("data-badge"));

        Assert.Empty(component.FindAll(".db-entry-upgrade-btn"));
        Assert.Empty(component.FindAll(".toggle"));
        Assert.Single(component.FindAll(".db-entry-remove-btn"));
    }

    [Fact]
    public void Render_UpgradeFailedEntry_ShowsRetryButton_AndRedBadge()
    {
        // Arrange
        var entry = MakeEntry(DatabaseStatus.UpgradeFailed);

        // Act
        var component = RenderRow(entry);

        // Assert
        var button = component.Find(".db-entry-upgrade-btn");
        Assert.Equal("Retry Upgrade", button.TextContent.Trim());
        Assert.Contains("button-red", button.GetAttribute("class") ?? string.Empty);

        var badge = component.Find(".db-entry-badge");
        Assert.Equal("Upgrade failed", badge.TextContent);
        Assert.Equal("UpgradeFailed", badge.GetAttribute("data-badge"));
    }

    [Fact]
    public void Render_UpgradeRequiredEntry_HidesTrashButton()
    {
        // Arrange — UpgradeRequired hides trash so the user is steered to either Upgrade
        // (resolving the V3 → V4 transition) or — once they give up and the entry transitions
        // to UpgradeFailed — Remove. Allowing direct removal of an UpgradeRequired entry would
        // skip past the explicit upgrade decision the row UI is designed to surface.
        var entry = MakeEntry(DatabaseStatus.UpgradeRequired);

        // Act
        var component = RenderRow(entry);

        // Assert
        Assert.Empty(component.FindAll(".db-entry-remove-btn"));
    }

    [Fact]
    public void Render_UpgradeRequiredEntry_ShowsUpgradeButton_AndNoBadge()
    {
        // Arrange
        var entry = MakeEntry(DatabaseStatus.UpgradeRequired);

        // Act
        var component = RenderRow(entry);

        // Assert
        var button = component.Find(".db-entry-upgrade-btn");
        Assert.Equal("Upgrade", button.TextContent.Trim());
        Assert.False(button.HasAttribute("disabled"));
        Assert.DoesNotContain("button-red", button.GetAttribute("class") ?? string.Empty);

        Assert.Empty(component.FindAll(".db-entry-badge"));
        Assert.Empty(component.FindAll(".toggle"));
    }

    [Fact]
    public async Task RetryButtonClick_InvokesOnUpgrade()
    {
        // Arrange
        var entry = MakeEntry(DatabaseStatus.UpgradeFailed);
        int invocationCount = 0;
        var component = Render<DatabaseEntryRow>(parameters => parameters
            .Add(p => p.Entry, entry)
            .Add(p => p.OnUpgrade, () => invocationCount++));

        // Act
        await component.Find(".db-entry-upgrade-btn").ClickAsync(new());

        // Assert
        Assert.Equal(1, invocationCount);
    }

    [Fact]
    public async Task TogglingTrueRadio_InvokesOnToggle_OnReadyEntry()
    {
        // Arrange
        var entry = MakeEntry(DatabaseStatus.Ready);
        int invocationCount = 0;
        var component = Render<DatabaseEntryRow>(parameters => parameters
            .Add(p => p.Entry, entry)
            .Add(p => p.EffectiveEnabled, false)
            .Add(p => p.OnToggle, () => invocationCount++));

        // Act
        var enableRadio = component.FindAll(".toggle input[type='radio']")[1];
        await enableRadio.ChangeAsync(new() { Value = "true" });

        // Assert
        Assert.Equal(1, invocationCount);
    }

    [Fact]
    public async Task UpgradeButtonClick_InvokesOnUpgrade()
    {
        // Arrange
        var entry = MakeEntry(DatabaseStatus.UpgradeRequired);
        int invocationCount = 0;
        var component = Render<DatabaseEntryRow>(parameters => parameters
            .Add(p => p.Entry, entry)
            .Add(p => p.OnUpgrade, () => invocationCount++));

        // Act
        await component.Find(".db-entry-upgrade-btn").ClickAsync(new());

        // Assert
        Assert.Equal(1, invocationCount);
    }

    private static DatabaseEntry MakeEntry(
        DatabaseStatus status,
        string fileName = "a.db",
        bool isEnabled = false,
        bool backupExists = false) =>
        new(fileName, $@"C:\dbs\{fileName}", isEnabled, status, backupExists);

    private IRenderedComponent<DatabaseEntryRow> RenderRow(
        DatabaseEntry entry,
        bool isClassificationPending = false,
        bool isUpgrading = false,
        bool isUpgradeBlocked = false,
        bool effectiveEnabled = false) =>
        Render<DatabaseEntryRow>(parameters => parameters
            .Add(p => p.Entry, entry)
            .Add(p => p.IsClassificationPending, isClassificationPending)
            .Add(p => p.IsUpgrading, isUpgrading)
            .Add(p => p.IsUpgradeBlocked, isUpgradeBlocked)
            .Add(p => p.EffectiveEnabled, effectiveEnabled));
}
