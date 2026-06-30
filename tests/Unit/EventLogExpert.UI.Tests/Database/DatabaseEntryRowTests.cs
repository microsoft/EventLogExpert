// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Schema;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.Database.Upgrade;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.UI.Database;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Database;

public sealed class DatabaseEntryRowTests : BunitContext
{
    private readonly IMenuService _menuService = Substitute.For<IMenuService>();

    public DatabaseEntryRowTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(Substitute.For<ITraceLogger>());
        Services.AddSingleton(_menuService);
    }

    [Fact]
    public async Task CancelButton_Click_InvokesProgressCancelDelegate()
    {
        var entry = MakeEntry(DatabaseStatus.UpgradeRequired);
        int cancelCount = 0;
        var progress = MakeProgress(currentEntryName: "a.db", cancel: () => cancelCount++);

        var component = RenderRow(entry, upgradeProgress: progress);
        await component.Find(".db-entry-cancel-btn").ClickAsync(new MouseEventArgs());

        Assert.Equal(1, cancelCount);
    }

    [Fact]
    public void Checkbox_InNormalMode_IsCollapsedAndAriaHidden()
    {
        var entry = MakeEntry(DatabaseStatus.Ready);

        var component = RenderRow(entry);

        var wrapper = component.Find(".db-entry-checkbox");
        Assert.DoesNotContain("db-entry-checkbox--visible", wrapper.GetAttribute("class") ?? string.Empty);
        Assert.Equal("true", wrapper.GetAttribute("aria-hidden"));
    }

    [Fact]
    public void Checkbox_InNormalMode_IsDisabled()
    {
        var entry = MakeEntry(DatabaseStatus.Ready);

        var component = RenderRow(entry);

        var checkbox = component.Find(".db-entry-row input[type='checkbox']");
        Assert.True(checkbox.HasAttribute("disabled"));
    }

    [Fact]
    public void Checkbox_InSelectionMode_IsVisible_NoAriaHidden()
    {
        var entry = MakeEntry(DatabaseStatus.Ready);

        var component = Render<DatabaseEntryRow>(parameters => parameters
            .Add(p => p.Entry, entry)
            .Add(p => p.IsSelectionModeActive, true));

        var wrapper = component.Find(".db-entry-checkbox");
        Assert.Contains("db-entry-checkbox--visible", wrapper.GetAttribute("class") ?? string.Empty);
        Assert.False(wrapper.HasAttribute("aria-hidden"));
    }

    [Fact]
    public void Checkbox_RenderedWithAriaLabelInRow()
    {
        var entry = MakeEntry(DatabaseStatus.Ready, "MyDb.evtx");

        var component = RenderRow(entry);

        var checkbox = component.Find(".db-entry-row input[type='checkbox']");
        Assert.Equal("Select MyDb.evtx", checkbox.GetAttribute("aria-label"));
    }

    [Fact]
    public async Task CheckboxChange_InSelectionMode_InvokesOnSelectionToggle()
    {
        var entry = MakeEntry(DatabaseStatus.Ready);
        int invocationCount = 0;
        var component = Render<DatabaseEntryRow>(parameters => parameters
            .Add(p => p.Entry, entry)
            .Add(p => p.IsSelectionModeActive, true)
            .Add(p => p.OnSelectionToggle, () => invocationCount++));

        await component.Find(".db-entry-row input[type='checkbox']")
            .ChangeAsync(new ChangeEventArgs { Value = true });

        Assert.Equal(1, invocationCount);
    }

    [Fact]
    public async Task ContextMenu_InNormalMode_OpensMenuWithRemoveItem()
    {
        var entry = MakeEntry(DatabaseStatus.Ready);

        var component = RenderRow(entry);

        await component.Find(".db-entry-row").TriggerEventAsync("oncontextmenu",
            new MouseEventArgs { ClientX = 100, ClientY = 200 });

        _menuService.Received(1).OpenAt(100, 200, Arg.Is<IReadOnlyList<MenuItem>>(items => items.Count == 1));
    }

    [Fact]
    public async Task ContextMenu_InSelectionMode_DoesNotOpenMenu()
    {
        var entry = MakeEntry(DatabaseStatus.Ready);

        var component = Render<DatabaseEntryRow>(parameters => parameters
            .Add(p => p.Entry, entry)
            .Add(p => p.IsSelectionModeActive, true));

        await component.Find(".db-entry-row").TriggerEventAsync("oncontextmenu",
            new MouseEventArgs { ClientX = 50, ClientY = 50 });

        _menuService.DidNotReceive().OpenAt(
            Arg.Any<double>(),
            Arg.Any<double>(),
            Arg.Any<IReadOnlyList<MenuItem>>(),
            Arg.Any<bool>(),
            Arg.Any<bool>());
    }

    [Fact]
    public async Task ContextMenu_RemoveItem_HasRemoveLabel()
    {
        var entry = MakeEntry(DatabaseStatus.Ready, "MyProvider.db");
        IReadOnlyList<MenuItem>? capturedItems = null;
        _menuService.When(s => s.OpenAt(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<IReadOnlyList<MenuItem>>(), Arg.Any<bool>(), Arg.Any<bool>()))
            .Do(call => capturedItems = call.Arg<IReadOnlyList<MenuItem>>());

        var component = RenderRow(entry);
        await component.Find(".db-entry-row").TriggerEventAsync("oncontextmenu", new MouseEventArgs());

        Assert.NotNull(capturedItems);
        var item = Assert.Single(capturedItems!);
        Assert.Equal("Remove", item.Label);
    }

    [Fact]
    public void OsStamp_AboveDisplayCap_RendersOverflowCount()
    {
        // Overflow uses "9+" because the classifier only reads cap + 1 distinct stamps.
        var entry = MakeEntry(DatabaseStatus.Ready) with { OsStamps = MakeDistinctStamps(10) };

        var component = RenderRow(entry);

        Assert.Equal("Mixed (9+ OS versions)", component.Find(".db-entry-osstamp").TextContent.Trim());
    }

    [Fact]
    public void OsStamp_AllNullFields_DoesNotRenderSecondaryText()
    {
        var entry = MakeEntry(DatabaseStatus.Ready) with
        {
            OsStamps = [new ProviderDatabaseOsStamp(null, null, null, null)]
        };

        var component = RenderRow(entry);

        Assert.Empty(component.FindAll(".db-entry-osstamp"));
    }

    [Fact]
    public void OsStamp_AtDisplayCap_RendersExactCount()
    {
        var entry = MakeEntry(DatabaseStatus.Ready) with { OsStamps = MakeDistinctStamps(9) };

        var component = RenderRow(entry);

        Assert.Equal("Mixed (9 OS versions)", component.Find(".db-entry-osstamp").TextContent.Trim());
    }

    [Fact]
    public void OsStamp_MultipleDistinctStamps_RendersMixedCount()
    {
        var entry = MakeEntry(DatabaseStatus.Ready) with
        {
            OsStamps =
            [
                new ProviderDatabaseOsStamp(26100, 1234, "ServerDatacenter", "24H2"),
                new ProviderDatabaseOsStamp(22631, 3447, "Professional", "23H2")
            ]
        };

        var component = RenderRow(entry);

        Assert.Equal("Mixed (2 OS versions)", component.Find(".db-entry-osstamp").TextContent.Trim());
    }

    [Fact]
    public void OsStamp_NoStamps_DoesNotRenderSecondaryText()
    {
        var entry = MakeEntry(DatabaseStatus.Ready);

        var component = RenderRow(entry);

        Assert.Empty(component.FindAll(".db-entry-osstamp"));
    }

    [Fact]
    public void OsStamp_OmitsNullSegments()
    {
        var entry = MakeEntry(DatabaseStatus.Ready) with
        {
            OsStamps = [new ProviderDatabaseOsStamp(26100, null, null, "24H2")]
        };

        var component = RenderRow(entry);

        Assert.Equal("24H2 \u00B7 26100", component.Find(".db-entry-osstamp").TextContent.Trim());
    }

    [Fact]
    public void OsStamp_SingleStamp_RendersFormattedSecondaryTextWithAriaLabel()
    {
        var entry = MakeEntry(DatabaseStatus.Ready) with
        {
            OsStamps = [new ProviderDatabaseOsStamp(26100, 1234, "ServerDatacenter", "24H2")]
        };

        var component = RenderRow(entry);

        var stamp = component.Find(".db-entry-osstamp");
        Assert.Equal("ServerDatacenter \u00B7 24H2 \u00B7 26100.1234", stamp.TextContent.Trim());
        Assert.Equal("Source OS: ServerDatacenter \u00B7 24H2 \u00B7 26100.1234", stamp.GetAttribute("aria-label"));
    }

    [Fact]
    public void Render_BackgroundUpgrade_UpgradeFailed_NoDoubleRenderBadgeAndSpinner()
    {
        var entry = MakeEntry(DatabaseStatus.UpgradeFailed);
        var progress = MakeProgress(currentEntryName: "a.db", scope: UpgradeProgressScope.Background);

        var component = RenderRow(entry, upgradeProgress: progress);

        Assert.Single(component.FindAll(".db-entry-spinner"));
        Assert.Empty(component.FindAll(".db-entry-badge"));
    }

    [Fact]
    public void Render_BackupExists_AND_BackgroundUpgrade_StillShowsBadge()
    {
        var entry = MakeEntry(DatabaseStatus.UpgradeRequired, backupExists: true);
        var progress = MakeProgress(currentEntryName: "a.db", scope: UpgradeProgressScope.Background);

        var component = RenderRow(entry, upgradeProgress: progress);

        Assert.Single(component.FindAll(".db-entry-badge"));
        Assert.Single(component.FindAll(".db-entry-restore-btn"));
        Assert.Empty(component.FindAll(".db-entry-spinner"));
    }

    [Fact]
    public void Render_BackupExistsAndIsUpgrading_StillShowsRecoveryBadge()
    {
        var entry = MakeEntry(DatabaseStatus.Ready, backupExists: true);

        var component = RenderRow(entry, isUpgrading: true);

        var badge = component.Find(".db-entry-badge");
        Assert.Equal("Recovery required", badge.TextContent);
        Assert.Equal("Recovery", badge.GetAttribute("data-badge"));

        Assert.Empty(component.FindAll(".db-entry-upgrading"));
        Assert.Empty(component.FindAll(".db-entry-upgrade-btn"));
        Assert.Empty(component.FindAll(".option-select"));
        Assert.Empty(component.FindAll(".db-entry-remove-btn"));
    }

    [Fact]
    public void Render_BackupExistsEntry_OverridesReadyStatus()
    {
        var entry = MakeEntry(DatabaseStatus.Ready, backupExists: true);

        var component = RenderRow(entry);

        var badge = component.Find(".db-entry-badge");
        Assert.Equal("Recovery required", badge.TextContent);
        Assert.Empty(component.FindAll(".option-select"));
        Assert.Empty(component.FindAll(".db-entry-remove-btn"));
    }

    [Fact]
    public void Render_BackupExistsEntry_RestoreButton_DisabledWhenBlocked()
    {
        var entry = MakeEntry(DatabaseStatus.UpgradeRequired, backupExists: true);

        var component = RenderRow(entry, isUpgradeBlocked: true);

        var restoreBtn = component.Find(".db-entry-restore-btn");
        Assert.True(restoreBtn.HasAttribute("disabled"));
    }

    [Fact]
    public void Render_BackupExistsEntry_ShowsRecoveryRequiredBadge_NoTrash()
    {
        var entry = MakeEntry(DatabaseStatus.UpgradeRequired, backupExists: true);

        var component = RenderRow(entry);

        var badge = component.Find(".db-entry-badge");
        Assert.Equal("Recovery required", badge.TextContent);
        Assert.Equal("Recovery", badge.GetAttribute("data-badge"));

        Assert.Empty(component.FindAll(".db-entry-upgrade-btn"));
        Assert.Empty(component.FindAll(".option-select"));
        Assert.Empty(component.FindAll(".db-entry-upgrading"));

        Assert.Empty(component.FindAll(".db-entry-remove-btn"));
    }

    [Fact]
    public void Render_BackupExistsEntry_ShowsRestoreButton()
    {
        var entry = MakeEntry(DatabaseStatus.UpgradeRequired, backupExists: true);

        var component = RenderRow(entry);

        var restoreBtn = component.Find(".db-entry-restore-btn");
        Assert.Equal("Restore database a.db from backup", restoreBtn.GetAttribute("aria-label"));
        Assert.Contains("Restore", restoreBtn.TextContent);
    }

    [Fact]
    public void Render_ClassificationFailedEntry_ShowsRetryClassificationButton()
    {
        var entry = MakeEntry(DatabaseStatus.ClassificationFailed);

        var component = RenderRow(entry);

        var retryBtn = component.Find(".db-entry-retry-classification-btn");
        Assert.Equal("Retry classification of database a.db", retryBtn.GetAttribute("aria-label"));
        Assert.Contains("Retry classification", retryBtn.TextContent);
    }

    [Fact]
    public void Render_DefaultIsSelectedFalse_CheckboxUnchecked()
    {
        var entry = MakeEntry(DatabaseStatus.Ready);

        var component = RenderRow(entry);

        var checkbox = component.Find(".db-entry-row input[type='checkbox']");
        Assert.False(checkbox.HasAttribute("checked"));
    }

    [Fact]
    public void Render_DisabledEntries_NoTrashButton()
    {
        foreach (var status in Enum.GetValues<DatabaseStatus>())
        {
            var entry = MakeEntry(status);

            var component = RenderRow(entry);
            Assert.Empty(component.FindAll(".db-entry-remove-btn"));
        }
    }

    [Fact]
    public void Render_FileName_AppearsInRow()
    {
        var entry = MakeEntry(DatabaseStatus.Ready, "MyProvider.db");

        var component = RenderRow(entry);

        Assert.Equal("MyProvider.db", component.Find(".db-entry-name").TextContent);
    }

    [Fact]
    public void Render_IsSelectedTrue_CheckboxChecked()
    {
        var entry = MakeEntry(DatabaseStatus.Ready);

        var component = RenderRow(entry, isSelected: true);

        var checkbox = component.Find(".db-entry-row input[type='checkbox']");
        Assert.True(checkbox.HasAttribute("checked"));
    }

    [Fact]
    public void Render_IsUpgradeBlocked_DisablesRetryUpgradeButton()
    {
        var entry = MakeEntry(DatabaseStatus.UpgradeFailed);

        var component = RenderRow(entry, isUpgradeBlocked: true);

        var button = component.Find(".db-entry-upgrade-btn");
        Assert.True(button.HasAttribute("disabled"));
    }

    [Fact]
    public void Render_IsUpgradeBlocked_DisablesUpgradeButton()
    {
        var entry = MakeEntry(DatabaseStatus.UpgradeRequired);

        var component = RenderRow(entry, isUpgradeBlocked: true);

        var button = component.Find(".db-entry-upgrade-btn");
        Assert.True(button.HasAttribute("disabled"));
    }

    [Fact]
    public void Render_IsUpgrading_ShowsSpinner_AndHidesBadge()
    {
        var entry = MakeEntry(DatabaseStatus.UpgradeRequired);

        var component = RenderRow(entry, isUpgrading: true);

        var upgrading = component.Find(".db-entry-upgrading");
        Assert.Contains("Upgrading", upgrading.TextContent);
        Assert.Single(component.FindAll(".db-entry-upgrading .db-entry-spinner"));

        Assert.Empty(component.FindAll(".db-entry-upgrade-btn"));
        Assert.Empty(component.FindAll(".db-entry-badge"));

        Assert.Empty(component.FindAll(".db-entry-remove-btn"));
    }

    [Fact]
    public void Render_ManageUpgrade_TransitionalWindow_RoleStatusPresent()
    {
        // Covers the manage-upgrade window before first per-file progress arrives.
        var entry = MakeEntry(DatabaseStatus.UpgradeRequired);

        var component = RenderRow(entry, isUpgrading: true);

        var upgrading = component.Find(".db-entry-upgrading");
        Assert.Equal("status", upgrading.GetAttribute("role"));
        Assert.Contains("Upgrading", upgrading.TextContent);
    }

    [Fact]
    public void Render_NonReadyEnabledEntry_NoTrashButton()
    {
        // Non-ready enabled entries are not resolver-loaded, so removal remains safe.
        var entry = MakeEntry(DatabaseStatus.UpgradeFailed, isEnabled: true);

        var component = RenderRow(entry);

        Assert.Empty(component.FindAll(".db-entry-remove-btn"));
    }

    [Fact]
    public void Render_NotClassifiedEntry_ShowsDisabledToggle_AndClassifyingBadge()
    {
        var entry = MakeEntry(DatabaseStatus.NotClassified);

        var component = RenderRow(entry);

        var radios = component.FindAll(".option-select input[type='radio']");
        Assert.NotEmpty(radios);
        Assert.All(radios, r => Assert.True(r.HasAttribute("disabled")));

        var badge = component.Find(".db-entry-badge");
        Assert.Equal("Classifying\u2026", badge.TextContent);
        Assert.Equal("NotClassified", badge.GetAttribute("data-badge"));
    }

    [Fact]
    public void Render_PendingToggle_AnnouncesPendingViaAriaDescribedBy()
    {
        var entry = MakeEntry(DatabaseStatus.Ready, "provider-y.db");

        var component = RenderRow(entry, effectiveEnabled: false, isTogglePending: true);

        var radiogroup = component.Find(".db-entry-actions [role='radiogroup']");
        var describedById = radiogroup.GetAttribute("aria-describedby");
        Assert.False(string.IsNullOrEmpty(describedById),
            "Pending toggle must have aria-describedby pointing at the pending-status span.");

        var pendingSpan = component.Find($"#{describedById}");
        Assert.Equal("(pending toggle, unsaved)", pendingSpan.TextContent.Trim());
        Assert.Contains("visually-hidden", pendingSpan.GetAttribute("class") ?? string.Empty);
    }

    [Fact]
    public void Render_PendingToggle_AppliesIndicatorClass()
    {
        var entry = MakeEntry(DatabaseStatus.Ready);

        var component = RenderRow(entry, effectiveEnabled: false, isTogglePending: true);

        var actions = component.Find(".db-entry-actions");
        Assert.Contains("db-entry-actions--pending", actions.GetAttribute("class") ?? string.Empty);
    }

    [Fact]
    public void Render_PendingToggleOnDisabledToggle_DoesNotShowIndicator()
    {
        // Disabled toggles must suppress pending announcements because the user cannot act on them.
        var entry = MakeEntry(DatabaseStatus.NotClassified, "provider-z.db");

        var component = RenderRow(entry, isTogglePending: true);

        var actions = component.Find(".db-entry-actions");
        Assert.DoesNotContain("db-entry-actions--pending", actions.GetAttribute("class") ?? string.Empty);

        var radiogroup = component.Find(".db-entry-actions [role='radiogroup']");
        Assert.True(string.IsNullOrEmpty(radiogroup.GetAttribute("aria-describedby")),
            "Disabled toggle should NOT carry the pending-status description.");
        Assert.Empty(component.FindAll("span.visually-hidden"));
    }

    [Fact]
    public void Render_ReadyEnabledEntry_NoTrashButton()
    {
        var entry = MakeEntry(DatabaseStatus.Ready, isEnabled: true);

        var component = RenderRow(entry, effectiveEnabled: true);

        Assert.Empty(component.FindAll(".db-entry-remove-btn"));
    }

    [Fact]
    public void Render_ReadyEnabledEntry_OptimisticToggleOff_NoTrashButton()
    {
        var entry = MakeEntry(DatabaseStatus.Ready, isEnabled: true);

        var component = RenderRow(entry, effectiveEnabled: false);

        Assert.Empty(component.FindAll(".db-entry-remove-btn"));
    }

    [Fact]
    public void Render_ReadyEntry_ShowsToggle_AndNoBadge()
    {
        var entry = MakeEntry(DatabaseStatus.Ready);

        var component = RenderRow(entry);

        Assert.Single(component.FindAll(".option-select"));
        Assert.Empty(component.FindAll(".db-entry-badge"));
        Assert.Empty(component.FindAll(".db-entry-upgrade-btn"));
        Assert.Empty(component.FindAll(".db-entry-upgrading"));
    }

    [Fact]
    public void Render_ReadyEntryWithClassificationPending_ShowsDisabledToggle()
    {
        var entry = MakeEntry(DatabaseStatus.Ready);

        var component = RenderRow(entry, true);

        var radios = component.FindAll(".option-select input[type='radio']");
        Assert.NotEmpty(radios);
        Assert.All(radios, r => Assert.True(r.HasAttribute("disabled")));
    }

    [Theory]
    [InlineData(DatabaseStatus.UnrecognizedSchema, "Unrecognized")]
    [InlineData(DatabaseStatus.ObsoleteSchema, "Obsolete")]
    public void Render_TerminalStatus_ShowsBadge_NoTrash(DatabaseStatus status, string expectedLabel)
    {
        // ClassificationFailed is excluded because it renders the retry classification button.
        var entry = MakeEntry(status);

        var component = RenderRow(entry);

        var badge = component.Find(".db-entry-badge");
        Assert.Equal(expectedLabel, badge.TextContent);
        Assert.Equal(status.ToString(), badge.GetAttribute("data-badge"));

        Assert.Empty(component.FindAll(".db-entry-upgrade-btn"));
        Assert.Empty(component.FindAll(".option-select"));
        Assert.Empty(component.FindAll(".db-entry-remove-btn"));
    }

    [Fact]
    public void Render_Toggle_UsesAriaLabelledByPointingAtFilenameButton()
    {
        var entry = MakeEntry(DatabaseStatus.Ready, "provider-x.db");

        var component = RenderRow(entry, effectiveEnabled: false);

        var radiogroup = component.Find(".db-entry-actions [role='radiogroup']");
        var labelledById = radiogroup.GetAttribute("aria-labelledby");
        Assert.False(string.IsNullOrEmpty(labelledById),
            "Toggle must have aria-labelledby referencing the filename button.");

        var nameButton = component.Find($"#{labelledById}");
        Assert.Equal("db-entry-name", nameButton.GetAttribute("class"));
        Assert.Equal("provider-x.db", nameButton.TextContent.Trim());

        Assert.True(string.IsNullOrEmpty(radiogroup.GetAttribute("aria-label")),
            "Toggle must not fall back to a synthesized aria-label when aria-labelledby is set.");
    }

    [Fact]
    public void Render_UpgradeFailedEntry_ShowsRetryButton_AndRedBadge()
    {
        var entry = MakeEntry(DatabaseStatus.UpgradeFailed);

        var component = RenderRow(entry);

        var button = component.Find(".db-entry-upgrade-btn");
        Assert.Equal("Retry Upgrade", button.TextContent.Trim());
        Assert.Contains("button-red", button.GetAttribute("class") ?? string.Empty);

        var badge = component.Find(".db-entry-badge");
        Assert.Equal("Upgrade failed", badge.TextContent);
        Assert.Equal("UpgradeFailed", badge.GetAttribute("data-badge"));
    }

    [Fact]
    public void Render_UpgradeProgress_EmptyEntryName_DoesNotEmitFilenameSrSuffix()
    {
        var entry = MakeEntry(DatabaseStatus.UpgradeRequired);
        var progress = MakeProgress(currentEntryName: string.Empty, currentBatchSize: 2);

        var component = RenderRow(entry, upgradeProgress: progress);

        Assert.Empty(component.FindAll(".db-entry-upgrading .visually-hidden"));
    }

    [Fact]
    public void Render_UpgradeProgress_EmptyEntryName_RendersPreparingMessage()
    {
        var entry = MakeEntry(DatabaseStatus.UpgradeRequired);
        var progress = MakeProgress(
            currentEntryName: string.Empty,
            currentBatchSize: 3);

        var component = RenderRow(entry, upgradeProgress: progress);

        var text = component.Find(".db-entry-upgrading-text");
        Assert.Contains("Preparing upgrade of 3 databases", text.TextContent);
    }

    [Fact]
    public void Render_UpgradeProgress_EmptyEntryName_SingleDb_UsesSingularLabel()
    {
        var entry = MakeEntry(DatabaseStatus.UpgradeRequired);
        var progress = MakeProgress(currentEntryName: string.Empty, currentBatchSize: 1);

        var component = RenderRow(entry, upgradeProgress: progress);

        var text = component.Find(".db-entry-upgrading-text");
        Assert.Contains("Preparing upgrade of 1 database", text.TextContent);
        Assert.DoesNotContain("databases", text.TextContent);
    }

    [Fact]
    public void Render_UpgradeProgress_RendersRichProgressMarkupWithVerbAndPosition()
    {
        var entry = MakeEntry(DatabaseStatus.UpgradeRequired);
        var progress = MakeProgress(
            currentEntryName: "a.db",
            currentPhase: UpgradePhase.MigratingSchema,
            currentBatchPosition: 2,
            currentBatchSize: 5);

        var component = RenderRow(entry, upgradeProgress: progress);

        var text = component.Find(".db-entry-upgrading-text");
        Assert.Equal("Migrating schema 2 of 5", text.TextContent);
    }

    [Fact]
    public void Render_UpgradeProgress_RoleStatusOnInnerSpan_WithFilenameSrSuffix()
    {
        var entry = MakeEntry(DatabaseStatus.UpgradeRequired, "MyProvider.db");
        var progress = MakeProgress(currentEntryName: "MyProvider.db");

        var component = RenderRow(entry, upgradeProgress: progress);

        var statusSpan = component.Find(".db-entry-upgrading-status");
        Assert.Equal("status", statusSpan.GetAttribute("role"));

        var srSuffix = component.Find(".db-entry-upgrading .visually-hidden");
        Assert.Contains("MyProvider.db", srSuffix.TextContent);
    }

    [Fact]
    public void Render_UpgradeProgress_SingleFileBatch_OmitsBatchPosition()
    {
        var entry = MakeEntry(DatabaseStatus.UpgradeRequired);
        var progress = MakeProgress(
            currentEntryName: "a.db",
            currentPhase: UpgradePhase.Verifying,
            currentBatchPosition: 1,
            currentBatchSize: 1);

        var component = RenderRow(entry, upgradeProgress: progress);

        var text = component.Find(".db-entry-upgrading-text");
        Assert.Equal("Verifying", text.TextContent);
    }

    [Theory]
    [InlineData(UpgradePhase.BackingUp, "Backing up")]
    [InlineData(UpgradePhase.MigratingSchema, "Migrating schema")]
    [InlineData(UpgradePhase.Verifying, "Verifying")]
    public void Render_UpgradeProgress_VerbPerPhase(UpgradePhase phase, string expectedVerb)
    {
        var entry = MakeEntry(DatabaseStatus.UpgradeRequired);
        var progress = MakeProgress(currentEntryName: "a.db", currentPhase: phase);

        var component = RenderRow(entry, upgradeProgress: progress);

        var text = component.Find(".db-entry-upgrading-text");
        Assert.Contains(expectedVerb, text.TextContent);
    }

    [Fact]
    public void Render_UpgradeRequiredEntry_NoTrashButton()
    {
        var entry = MakeEntry(DatabaseStatus.UpgradeRequired);

        var component = RenderRow(entry);

        Assert.Empty(component.FindAll(".db-entry-remove-btn"));
    }

    [Fact]
    public void Render_UpgradeRequiredEntry_ShowsUpgradeButton_AndNoBadge()
    {
        var entry = MakeEntry(DatabaseStatus.UpgradeRequired);

        var component = RenderRow(entry);

        var button = component.Find(".db-entry-upgrade-btn");
        Assert.Equal("Upgrade", button.TextContent.Trim());
        Assert.False(button.HasAttribute("disabled"));
        Assert.DoesNotContain("button-red", button.GetAttribute("class") ?? string.Empty);

        Assert.Empty(component.FindAll(".db-entry-badge"));
        Assert.Empty(component.FindAll(".option-select"));
    }

    [Fact]
    public async Task RestoreButtonClick_InvokesOnRestoreFromBackup()
    {
        var entry = MakeEntry(DatabaseStatus.UpgradeRequired, backupExists: true);
        int invocationCount = 0;
        var component = Render<DatabaseEntryRow>(parameters => parameters
            .Add(p => p.Entry, entry)
            .Add(p => p.OnRestoreFromBackup, () => invocationCount++));

        await component.Find(".db-entry-restore-btn").ClickAsync(new MouseEventArgs());

        Assert.Equal(1, invocationCount);
    }

    [Fact]
    public async Task RestoreButtonClick_WhenIsUpgradeBlocked_DoesNotInvokeOnRestoreFromBackup()
    {
        var entry = MakeEntry(DatabaseStatus.UpgradeRequired, backupExists: true);
        int invocationCount = 0;
        var component = Render<DatabaseEntryRow>(parameters => parameters
            .Add(p => p.Entry, entry)
            .Add(p => p.IsUpgradeBlocked, true)
            .Add(p => p.OnRestoreFromBackup, () => invocationCount++));

        await component.Find(".db-entry-restore-btn").ClickAsync(new MouseEventArgs());

        Assert.Equal(0, invocationCount);
    }

    [Fact]
    public async Task RestoreButtonClick_WhenUpgradeProgressMatchesRow_DoesNotInvokeOnRestoreFromBackup()
    {
        // Backup restore clicks must honor per-row background upgrade progress even without a spinner.
        var entry = MakeEntry(DatabaseStatus.UpgradeRequired, backupExists: true);
        int invocationCount = 0;
        var progress = new BannerProgressEntry(
            UpgradeBatchId.Create(),
            UpgradeProgressScope.Background,
            CurrentBatchPosition: 1,
            CurrentBatchSize: 1,
            CurrentEntryName: entry.FileName,
            CurrentPhase: UpgradePhase.MigratingSchema,
            QueuedBatchesAfter: 0,
            Cancel: () => { });
        var component = Render<DatabaseEntryRow>(parameters => parameters
            .Add(p => p.Entry, entry)
            .Add(p => p.IsUpgradeBlocked, false)
            .Add(p => p.UpgradeProgress, progress)
            .Add(p => p.OnRestoreFromBackup, () => invocationCount++));

        var restoreBtn = component.Find(".db-entry-restore-btn");
        Assert.True(restoreBtn.HasAttribute("disabled"));

        await restoreBtn.ClickAsync(new MouseEventArgs());

        Assert.Equal(0, invocationCount);
    }

    [Fact]
    public async Task RetryButtonClick_InvokesOnUpgrade()
    {
        var entry = MakeEntry(DatabaseStatus.UpgradeFailed);
        int invocationCount = 0;
        var component = Render<DatabaseEntryRow>(parameters => parameters
            .Add(p => p.Entry, entry)
            .Add(p => p.OnUpgrade, () => invocationCount++));

        await component.Find(".db-entry-upgrade-btn").ClickAsync(new MouseEventArgs());

        Assert.Equal(1, invocationCount);
    }

    [Fact]
    public async Task RetryClassificationButtonClick_InvokesOnRetryClassification()
    {
        var entry = MakeEntry(DatabaseStatus.ClassificationFailed);
        int invocationCount = 0;
        var component = Render<DatabaseEntryRow>(parameters => parameters
            .Add(p => p.Entry, entry)
            .Add(p => p.OnRetryClassification, () => invocationCount++));

        await component.Find(".db-entry-retry-classification-btn").ClickAsync(new MouseEventArgs());

        Assert.Equal(1, invocationCount);
    }

    [Fact]
    public void Row_NoAriaSelectedAttribute()
    {
        var entry = MakeEntry(DatabaseStatus.Ready);

        var component = RenderRow(entry, isSelected: true);

        var row = component.Find(".db-entry-row");
        Assert.False(row.HasAttribute("aria-selected"));
    }

    [Fact]
    public async Task TogglingTrueRadio_InvokesOnToggle_OnReadyEntry()
    {
        var entry = MakeEntry(DatabaseStatus.Ready);
        int invocationCount = 0;
        var component = Render<DatabaseEntryRow>(parameters => parameters
            .Add(p => p.Entry, entry)
            .Add(p => p.EffectiveEnabled, false)
            .Add(p => p.OnToggle, () => invocationCount++));

        var enableRadio = component.FindAll(".option-select input[type='radio']")[1];
        await enableRadio.ChangeAsync(new ChangeEventArgs { Value = "true" });

        Assert.Equal(1, invocationCount);
    }

    [Fact]
    public void TrashButton_NotRendered_InNormalMode()
    {
        var entry = MakeEntry(DatabaseStatus.Ready);

        var component = RenderRow(entry);

        Assert.Empty(component.FindAll(".db-entry-remove-btn"));
    }

    [Fact]
    public async Task UpgradeButtonClick_InvokesOnUpgrade()
    {
        var entry = MakeEntry(DatabaseStatus.UpgradeRequired);
        int invocationCount = 0;
        var component = Render<DatabaseEntryRow>(parameters => parameters
            .Add(p => p.Entry, entry)
            .Add(p => p.OnUpgrade, () => invocationCount++));

        await component.Find(".db-entry-upgrade-btn").ClickAsync(new MouseEventArgs());

        Assert.Equal(1, invocationCount);
    }

    [Fact]
    public async Task UpgradeButtonClick_WhenDisabled_DoesNotInvokeOnUpgrade()
    {
        var entry = MakeEntry(DatabaseStatus.UpgradeRequired);
        int invocationCount = 0;
        var component = Render<DatabaseEntryRow>(parameters => parameters
            .Add(p => p.Entry, entry)
            .Add(p => p.IsUpgradeBlocked, true)
            .Add(p => p.OnUpgrade, () => invocationCount++));

        await component.Find(".db-entry-upgrade-btn").ClickAsync(new MouseEventArgs());

        Assert.Equal(0, invocationCount);
    }

    private static IReadOnlyList<ProviderDatabaseOsStamp> MakeDistinctStamps(int count) =>
        [.. Enumerable.Range(0, count).Select(index =>
            new ProviderDatabaseOsStamp(20000 + index, index, $"Edition{index}", $"Ver{index}"))];

    private static DatabaseEntry MakeEntry(
        DatabaseStatus status,
        string fileName = "a.db",
        bool isEnabled = false,
        bool backupExists = false) =>
        new(fileName, $@"C:\dbs\{fileName}", isEnabled, status, backupExists);

    private static BannerProgressEntry MakeProgress(
        string currentEntryName = "a.db",
        UpgradePhase currentPhase = UpgradePhase.MigratingSchema,
        int currentBatchPosition = 1,
        int currentBatchSize = 1,
        int queuedBatchesAfter = 0,
        UpgradeProgressScope scope = UpgradeProgressScope.ManageDatabasesTriggered,
        Action? cancel = null) =>
        new(
            UpgradeBatchId.Create(),
            scope,
            currentBatchPosition,
            currentBatchSize,
            currentEntryName,
            currentPhase,
            queuedBatchesAfter,
            cancel ?? (() => { }));

    private IRenderedComponent<DatabaseEntryRow> RenderRow(
        DatabaseEntry entry,
        bool isClassificationPending = false,
        bool isUpgrading = false,
        bool isUpgradeBlocked = false,
        bool effectiveEnabled = false,
        bool isTogglePending = false,
        bool isSelected = false,
        BannerProgressEntry? upgradeProgress = null) =>
        Render<DatabaseEntryRow>(parameters => parameters
            .Add(p => p.Entry, entry)
            .Add(p => p.IsClassificationPending, isClassificationPending)
            .Add(p => p.IsUpgrading, isUpgrading)
            .Add(p => p.IsUpgradeBlocked, isUpgradeBlocked)
            .Add(p => p.EffectiveEnabled, effectiveEnabled)
            .Add(p => p.IsTogglePending, isTogglePending)
            .Add(p => p.IsSelected, isSelected)
            .Add(p => p.UpgradeProgress, upgradeProgress));
}
