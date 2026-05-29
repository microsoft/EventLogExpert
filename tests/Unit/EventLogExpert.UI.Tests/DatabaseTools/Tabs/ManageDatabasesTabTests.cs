// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.Database.Upgrade;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.UI.DatabaseTools;
using EventLogExpert.UI.DatabaseTools.Tabs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Reflection;
using TestContext = Xunit.TestContext;

namespace EventLogExpert.UI.Tests.DatabaseTools.Tabs;

public sealed class ManageDatabasesTabTests : BunitContext
{
    private readonly IAnnouncementService _announcementService = Substitute.For<IAnnouncementService>();
    private readonly IDatabaseOperationCoordinator _coordinator = Substitute.For<IDatabaseOperationCoordinator>();
    private readonly FakeDatabaseService _databaseService = new();
    private readonly ILogReloadCoordinator _logReloadCoordinator = Substitute.For<ILogReloadCoordinator>();
    private readonly IProgressBannerService _progressBannerService = Substitute.For<IProgressBannerService>();
    private readonly ITraceLogger _traceLogger = Substitute.For<ITraceLogger>();

    public ManageDatabasesTabTests()
    {
        _progressBannerService.ManageDatabasesProgress.Returns((BannerProgressEntry?)null);
        _logReloadCoordinator.HasActiveLogs.Returns(false);

        Services.AddSingleton(_announcementService);
        Services.AddSingleton(_coordinator);
        Services.AddSingleton<IDatabaseService>(_databaseService);
        Services.AddSingleton(_logReloadCoordinator);
        Services.AddSingleton(_progressBannerService);
        Services.AddSingleton(_traceLogger);

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task ApplyPendingTogglesAsync_WhenIsUpgradeBlocked_ReturnsFalseAndDoesNotInvokeCoordinator()
    {
        _coordinator.IsAnyUpgradeInFlight.Returns(true);
        var component = Render<ManageDatabasesTab>();

        var saved = await component.InvokeAsync(component.Instance.ApplyPendingTogglesAsync);

        Assert.False(saved);
        await _coordinator.DidNotReceive().ApplyPendingTogglesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyPendingTogglesAsync_WithNoPendingToggles_ReturnsTrue_NoAnnounce()
    {
        var component = Render<ManageDatabasesTab>();

        var saved = await component.InvokeAsync(component.Instance.ApplyPendingTogglesAsync);

        Assert.True(saved);
        _announcementService.DidNotReceive().Announce(Arg.Any<string>());
    }

    [Fact]
    public async Task BulkRemove_ConfirmationAccepted_InvokesCoordinatorPerFile()
    {
        _databaseService.Entries = [
            Entry("a.db", isEnabled: false, status: DatabaseStatus.Ready),
            Entry("b.db", isEnabled: false, status: DatabaseStatus.Ready)];
        _coordinator.RemoveDatabaseAsync(
                Arg.Any<string>(),
                Arg.Any<Func<bool, CancellationToken, Task<bool>>>(),
                Arg.Any<CancellationToken>())
            .Returns(new RemoveOutcome(RemoveOutcomeStatus.Confirmed, true, false));
        var alertSurface = new FakeInlineAlertSurface { Result = new InlineAlertResult(true, null) };
        var component = RenderWithAlertSurface(alertSurface);

        var checkboxes = component.FindAll(".db-entry-row input[type='checkbox']");
        await component.InvokeAsync(() => checkboxes[0].ChangeAsync(new ChangeEventArgs { Value = true }));
        await component.InvokeAsync(() => checkboxes[1].ChangeAsync(new ChangeEventArgs { Value = true }));

        var bulkRemove = component.FindAll(".manage-databases-bulk-strip button")[1];
        await component.InvokeAsync(() => bulkRemove.Click());

        await _coordinator.Received(1).RemoveDatabaseAsync(
            "a.db",
            Arg.Any<Func<bool, CancellationToken, Task<bool>>>(),
            Arg.Any<CancellationToken>());
        await _coordinator.Received(1).RemoveDatabaseAsync(
            "b.db",
            Arg.Any<Func<bool, CancellationToken, Task<bool>>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BulkRemove_ConfirmationDeclined_DoesNotInvokeCoordinator()
    {
        _databaseService.Entries = [
            Entry("a.db", isEnabled: false, status: DatabaseStatus.Ready),
            Entry("b.db", isEnabled: false, status: DatabaseStatus.Ready)];
        var alertSurface = new FakeInlineAlertSurface { Result = new InlineAlertResult(false, null) };
        var component = RenderWithAlertSurface(alertSurface);

        var checkboxes = component.FindAll(".db-entry-row input[type='checkbox']");
        await component.InvokeAsync(() => checkboxes[0].ChangeAsync(new ChangeEventArgs { Value = true }));
        await component.InvokeAsync(() => checkboxes[1].ChangeAsync(new ChangeEventArgs { Value = true }));

        var bulkRemove = component.FindAll(".manage-databases-bulk-strip button")[1];
        await component.InvokeAsync(() => bulkRemove.Click());

        await _coordinator.DidNotReceive().RemoveDatabaseAsync(
            Arg.Any<string>(),
            Arg.Any<Func<bool, CancellationToken, Task<bool>>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BulkRemove_DuringBackgroundUpgrade_DualSignal_AcceptanceLabelMentionsCancel()
    {
        var entry = Entry("a.db", isEnabled: false, status: DatabaseStatus.Ready);
        _databaseService.Entries = [entry];

        _coordinator.IsUpgradeInFlight("a.db").Returns(false);
        _progressBannerService.BackgroundProgress.Returns(MakeProgress(
            currentEntryName: "a.db",
            scope: UpgradeProgressScope.Background));

        var alertSurface = new FakeInlineAlertSurface { Result = new InlineAlertResult(false, null) };
        var component = RenderWithAlertSurface(alertSurface);

        await component.InvokeAsync(() => component.Find(".db-entry-row input[type='checkbox']").ChangeAsync(new ChangeEventArgs { Value = true }));
        var bulkRemove = component.FindAll(".manage-databases-bulk-strip button")[1];
        await component.InvokeAsync(() => bulkRemove.Click());

        var captured = Assert.Single(alertSurface.Requests);
        Assert.Contains("Cancel", captured.AcceptLabel ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("upgrade", captured.AcceptLabel ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("a.db", captured.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BulkRemove_LogsReopened_CallsConsumeReopenedAsBaseline()
    {
        _databaseService.Entries = [
            Entry("a.db", isEnabled: true, status: DatabaseStatus.Ready),
            Entry("b.db", isEnabled: true, status: DatabaseStatus.Ready)];

        _coordinator.RemoveDatabaseAsync(
                Arg.Any<string>(),
                Arg.Any<Func<bool, CancellationToken, Task<bool>>>(),
                Arg.Any<CancellationToken>())
            .Returns(new RemoveOutcome(RemoveOutcomeStatus.Confirmed, true, true));
        var alertSurface = new FakeInlineAlertSurface { Result = new InlineAlertResult(true, null) };
        var component = RenderWithAlertSurface(alertSurface);

        _databaseService.RaiseUpgradeBatchCompleted(
            new UpgradeBatchCompletedEventArgs(
                UpgradeBatchId.Create(),
                new UpgradeBatchResult(["a.db"], [], []),
                wasCancelled: false));
        Assert.True(component.Instance.HasDatabaseStateChanged);

        var checkboxes = component.FindAll(".db-entry-row input[type='checkbox']");
        await component.InvokeAsync(() => checkboxes[0].ChangeAsync(new ChangeEventArgs { Value = true }));
        await component.InvokeAsync(() => checkboxes[1].ChangeAsync(new ChangeEventArgs { Value = true }));

        var bulkRemove = component.FindAll(".manage-databases-bulk-strip button")[1];
        await component.InvokeAsync(() => bulkRemove.Click());

        Assert.False(component.Instance.HasDatabaseStateChanged);
    }

    [Fact]
    public async Task BulkRemove_PerFileFailure_LogsToTraceLoggerAndAnnouncesFirstFailureDetail()
    {
        _databaseService.Entries = [
            Entry("a.db", isEnabled: false, status: DatabaseStatus.Ready),
            Entry("b.db", isEnabled: false, status: DatabaseStatus.Ready)];
        _coordinator.RemoveDatabaseAsync("a.db", Arg.Any<Func<bool, CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns<RemoveOutcome>(_ => throw new InvalidOperationException("disk full"));
        _coordinator.RemoveDatabaseAsync("b.db", Arg.Any<Func<bool, CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(new RemoveOutcome(RemoveOutcomeStatus.Confirmed, true, false));
        var alertSurface = new FakeInlineAlertSurface { Result = new InlineAlertResult(true, null) };
        var component = RenderWithAlertSurface(alertSurface);

        var checkboxes = component.FindAll(".db-entry-row input[type='checkbox']");
        await component.InvokeAsync(() => checkboxes[0].ChangeAsync(new ChangeEventArgs { Value = true }));
        await component.InvokeAsync(() => checkboxes[1].ChangeAsync(new ChangeEventArgs { Value = true }));

        var bulkRemove = component.FindAll(".manage-databases-bulk-strip button")[1];
        await component.InvokeAsync(() => bulkRemove.Click());

        _announcementService.Received().Announce(Arg.Is<string>(s => s.Contains("a.db") && s.Contains("disk full")));
    }

    [Fact]
    public async Task ClearSelection_EmptiesSet_HidesBulkStrip()
    {
        _databaseService.Entries = [
            Entry("a.db", isEnabled: false, status: DatabaseStatus.Ready),
            Entry("b.db", isEnabled: false, status: DatabaseStatus.Ready)];
        var component = Render<ManageDatabasesTab>();

        var checkboxes = component.FindAll(".db-entry-row input[type='checkbox']");
        await component.InvokeAsync(() => checkboxes[0].ChangeAsync(new ChangeEventArgs { Value = true }));
        await component.InvokeAsync(() => checkboxes[1].ChangeAsync(new ChangeEventArgs { Value = true }));
        Assert.Contains("2 selected", component.Find(".manage-databases-bulk-count").TextContent);

        var clearBtn = component.FindAll(".manage-databases-bulk-strip button")[0];
        await component.InvokeAsync(() => clearBtn.Click());

        Assert.False(component.Instance.HasSelectedForRemoval);
        Assert.Empty(component.FindAll(".manage-databases-bulk-strip"));
    }

    [Fact]
    public async Task ClearSelection_UpdatesAriaLiveAnnouncement()
    {
        _databaseService.Entries = [Entry("a.db", isEnabled: false, status: DatabaseStatus.Ready)];
        var component = Render<ManageDatabasesTab>();

        await component.InvokeAsync(() => component.Find(".db-entry-row input[type='checkbox']").ChangeAsync(new ChangeEventArgs { Value = true }));
        var liveRegion = component.Find(".manage-databases-tab > span[role='status'][aria-live='polite']");
        Assert.NotEqual(string.Empty, liveRegion.TextContent.Trim());

        var clearBtn = component.FindAll(".manage-databases-bulk-strip button")[0];
        await component.InvokeAsync(() => clearBtn.Click());

        liveRegion = component.Find(".manage-databases-tab > span[role='status'][aria-live='polite']");
        Assert.DoesNotContain("selected", liveRegion.TextContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfirmationMessage_WhenNoActiveLogs_DoesNotIncludeCloseReopenWarning()
    {
        _databaseService.Entries = [Entry("a.db", isEnabled: true, status: DatabaseStatus.Ready)];
        _logReloadCoordinator.HasActiveLogs.Returns(false);

        var alertSurface = new FakeInlineAlertSurface { Result = new InlineAlertResult(false, null) };
        var component = RenderWithAlertSurface(alertSurface);

        var removeBtn = component.Find(".db-entry-row .db-entry-remove-btn");
        await component.InvokeAsync(() => removeBtn.Click());

        var captured = Assert.Single(alertSurface.Requests);
        Assert.DoesNotContain("close and reopen", captured.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfirmationMessage_WhenRemovingActiveDbWithOpenLogs_IncludesCloseReopenWarning()
    {
        _databaseService.Entries = [Entry("a.db", isEnabled: true, status: DatabaseStatus.Ready)];
        _logReloadCoordinator.HasActiveLogs.Returns(true);

        var alertSurface = new FakeInlineAlertSurface { Result = new InlineAlertResult(false, null) };
        var component = RenderWithAlertSurface(alertSurface);

        var removeBtn = component.Find(".db-entry-row .db-entry-remove-btn");
        await component.InvokeAsync(() => removeBtn.Click());

        var captured = Assert.Single(alertSurface.Requests);
        Assert.Contains("close and reopen", captured.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfirmationMessage_WhenRemovingDisabledDb_DoesNotIncludeCloseReopenWarning()
    {
        _databaseService.Entries = [Entry("a.db", isEnabled: false, status: DatabaseStatus.Ready)];
        _logReloadCoordinator.HasActiveLogs.Returns(true);

        var alertSurface = new FakeInlineAlertSurface { Result = new InlineAlertResult(false, null) };
        var component = RenderWithAlertSurface(alertSurface);

        var removeBtn = component.Find(".db-entry-row .db-entry-remove-btn");
        await component.InvokeAsync(() => removeBtn.Click());

        var captured = Assert.Single(alertSurface.Requests);
        Assert.DoesNotContain("close and reopen", captured.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisposeAsync_UnsubscribesAllEvents()
    {
        var component = Render<ManageDatabasesTab>();

        Assert.Equal(1, _databaseService.EntriesChangedHandlerCount);
        Assert.Equal(1, _databaseService.UpgradeBatchCompletedHandlerCount);

        await component.InvokeAsync(() => component.Instance.DisposeAsync().AsTask());

        Assert.Equal(0, _databaseService.EntriesChangedHandlerCount);
        Assert.Equal(0, _databaseService.UpgradeBatchCompletedHandlerCount);
    }

    [Fact]
    public void HasDatabaseStateChanged_AfterEnabledEntryRemovedFromActiveSet_True()
    {
        // Active set was {a}; mutate underlying entries so set becomes {}; flag computed via diff.
        var entry = Entry("a.db", isEnabled: true, status: DatabaseStatus.Ready);
        _databaseService.Entries = [entry];
        var component = Render<ManageDatabasesTab>();

        _databaseService.Entries = [Entry("a.db", isEnabled: false, status: DatabaseStatus.Ready)];

        Assert.True(component.Instance.HasDatabaseStateChanged);
    }

    [Fact]
    public async Task HasDatabaseStateChanged_AfterImportNoChanges_False()
    {
        _databaseService.Entries = [Entry("a.db", isEnabled: true, status: DatabaseStatus.Ready)];
        _coordinator.ImportAsync(
                Arg.Any<Func<string, CancellationToken, Task<bool>>>(),
                Arg.Any<CancellationToken>())
            .Returns(ImportOutcome.None);
        var component = Render<ManageDatabasesTab>();

        var importBtn = component.Find("#manage-import-button");
        await component.InvokeAsync(() => importBtn.ClickAsync(new MouseEventArgs()));

        Assert.False(component.Instance.HasDatabaseStateChanged);
    }

    [Fact]
    public async Task HasDatabaseStateChanged_AfterImportSuccess_True_ViaStickyFlag()
    {
        _databaseService.Entries = [Entry("a.db", isEnabled: true, status: DatabaseStatus.Ready)];
        _coordinator.ImportAsync(
                Arg.Any<Func<string, CancellationToken, Task<bool>>>(),
                Arg.Any<CancellationToken>())
            .Returns(new ImportOutcome(ImportedCount: 1, Failures: [], UpgradeFailures: []));
        var component = Render<ManageDatabasesTab>();

        var importBtn = component.Find("#manage-import-button");
        await component.InvokeAsync(() => importBtn.ClickAsync(new MouseEventArgs()));

        Assert.True(component.Instance.HasDatabaseStateChanged);
    }

    [Fact]
    public void HasDatabaseStateChanged_AfterUpgradeCancelledWithNoSucceeded_False()
    {
        _databaseService.Entries = [Entry("a.db", isEnabled: true, status: DatabaseStatus.Ready)];
        var component = Render<ManageDatabasesTab>();

        _databaseService.RaiseUpgradeBatchCompleted(
            new UpgradeBatchCompletedEventArgs(UpgradeBatchId.Create(), new UpgradeBatchResult([], ["a.db"], []), wasCancelled: true));

        Assert.False(component.Instance.HasDatabaseStateChanged);
    }

    [Fact]
    public void HasDatabaseStateChanged_AfterUpgradeFailedWithNoSucceeded_False()
    {
        _databaseService.Entries = [Entry("a.db", isEnabled: true, status: DatabaseStatus.Ready)];
        var component = Render<ManageDatabasesTab>();

        _databaseService.RaiseUpgradeBatchCompleted(
            new UpgradeBatchCompletedEventArgs(
                UpgradeBatchId.Create(),
                new UpgradeBatchResult([], [], [new UpgradeFailure("a.db", "boom")]),
                wasCancelled: false));

        Assert.False(component.Instance.HasDatabaseStateChanged);
    }

    [Fact]
    public void HasDatabaseStateChanged_AfterUpgradeSuccess_True_ViaStickyFlag()
    {
        var entry = Entry("a.db", isEnabled: true, status: DatabaseStatus.Ready);
        _databaseService.Entries = [entry];
        var component = Render<ManageDatabasesTab>();

        // Schema migrated in place — entry stays in the active set, but the underlying data changed.
        _databaseService.RaiseUpgradeBatchCompleted(
            new UpgradeBatchCompletedEventArgs(UpgradeBatchId.Create(), new UpgradeBatchResult(["a.db"], [], []), wasCancelled: false));

        Assert.True(component.Instance.HasDatabaseStateChanged);
    }

    [Fact]
    public void HasDatabaseStateChanged_InitialState_False()
    {
        _databaseService.Entries = [Entry("a.db", isEnabled: true, status: DatabaseStatus.Ready)];

        var component = Render<ManageDatabasesTab>();

        Assert.False(component.Instance.HasDatabaseStateChanged);
    }

    [Fact]
    public void HasDatabaseStateChanged_PartiallySucceededWasCancelled_True()
    {
        // Multi-file batch: A succeeds, B is cancelled mid-flight. A's schema is permanently migrated;
        // flag must fire even though args.WasCancelled is true.
        _databaseService.Entries = [Entry("a.db", isEnabled: true, status: DatabaseStatus.Ready)];
        var component = Render<ManageDatabasesTab>();

        _databaseService.RaiseUpgradeBatchCompleted(
            new UpgradeBatchCompletedEventArgs(
                UpgradeBatchId.Create(),
                new UpgradeBatchResult(["a.db"], ["b.db"], []),
                wasCancelled: true));

        Assert.True(component.Instance.HasDatabaseStateChanged);
    }

    [Fact]
    public void HasDatabaseStateChanged_UpgradeSucceededForDisabledEntry_False()
    {
        // Background-scope upgrades of disabled (non-active) DBs must not trigger the reload prompt:
        // the file isn't in the active resolver set, so open logs aren't affected.
        _databaseService.Entries = [Entry("a.db", isEnabled: false, status: DatabaseStatus.Ready)];
        var component = Render<ManageDatabasesTab>();

        _databaseService.RaiseUpgradeBatchCompleted(
            new UpgradeBatchCompletedEventArgs(UpgradeBatchId.Create(), new UpgradeBatchResult(["a.db"], [], []), wasCancelled: false));

        Assert.False(component.Instance.HasDatabaseStateChanged);
    }

    [Fact]
    public void IsUpgradeInFlight_TracksCoordinator()
    {
        _coordinator.IsAnyUpgradeInFlight.Returns(true);
        var component = Render<ManageDatabasesTab>();

        Assert.True(component.Instance.IsUpgradeInFlight);
    }

    [Fact]
    public async Task RebaselineActiveSnapshotOnly_PreservesStickyFlags()
    {
        // Modal opens mid-classification (empty snapshot), user-initiated upgrade succeeds during the
        // wait (_schemaUpgradeOccurred=true), classification completes. The snapshot rebaselines but
        // the sticky flag must survive so the close-time reload prompt still fires.
        var tcs = new TaskCompletionSource();
        _databaseService.InitialClassificationTask = tcs.Task;
        _databaseService.Entries = [Entry("a.db", isEnabled: true, status: DatabaseStatus.Ready)];
        var component = Render<ManageDatabasesTab>();

        _databaseService.RaiseUpgradeBatchCompleted(
            new UpgradeBatchCompletedEventArgs(UpgradeBatchId.Create(), new UpgradeBatchResult(["a.db"], [], []), wasCancelled: false));

        Assert.True(component.Instance.HasDatabaseStateChanged);

        tcs.SetResult();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.True(component.Instance.HasDatabaseStateChanged);
    }

    [Fact]
    public void Render_BulkStrip_HiddenWhenNoSelection()
    {
        _databaseService.Entries = [Entry("a.db", isEnabled: false, status: DatabaseStatus.Ready)];

        var component = Render<ManageDatabasesTab>();

        Assert.Empty(component.FindAll(".manage-databases-bulk-strip"));
        Assert.False(component.Instance.HasSelectedForRemoval);
    }

    [Fact]
    public void Render_ClassificationPendingNotice_WhenClassificationInProgress()
    {
        _databaseService.Entries = [];
        _databaseService.InitialClassificationTask = new TaskCompletionSource().Task;

        var component = Render<ManageDatabasesTab>();

        Assert.Single(component.FindAll(".manage-status-banner--info"));
        Assert.Empty(component.FindAll(".manage-databases-empty"));
    }

    [Fact]
    public void Render_DoesNotRenderRemovedSharedProgressBanner()
    {
        _databaseService.Entries = [Entry("a.db", isEnabled: false, status: DatabaseStatus.UpgradeRequired)];
        _progressBannerService.ManageDatabasesProgress.Returns(MakeProgress(currentEntryName: "a.db"));

        var component = Render<ManageDatabasesTab>();

        Assert.Empty(component.FindAll("aside.manage-databases-upgrade-banner"));
    }

    [Fact]
    public void Render_EmptyState_WhenNoEntries_AndClassificationComplete()
    {
        _databaseService.Entries = [];
        _databaseService.InitialClassificationTask = Task.CompletedTask;

        var component = Render<ManageDatabasesTab>();

        Assert.Single(component.FindAll(".manage-databases-empty"));
    }

    [Fact]
    public void Render_HasPendingChanges_False_HidesSaveStrip()
    {
        var component = Render<ManageDatabasesTab>();

        Assert.Empty(component.FindAll(".manage-databases-save-strip"));
    }

    [Fact]
    public void Render_ImportButton_Present()
    {
        var component = Render<ManageDatabasesTab>();

        Assert.Single(component.FindAll("#manage-import-button"));
    }

    [Fact]
    public void Render_PersistentAriaLiveRegion_PresentRegardlessOfSelection()
    {
        _databaseService.Entries = [Entry("a.db", isEnabled: false, status: DatabaseStatus.Ready)];

        var component = Render<ManageDatabasesTab>();

        var statusRegion = component.Find(".manage-databases-tab > span[role='status'][aria-live='polite']");
        Assert.NotNull(statusRegion);
        Assert.Equal("true", statusRegion.GetAttribute("aria-atomic"));
    }

    [Fact]
    public async Task RestoreFromBackup_ServiceReturnsFalse_DoesNotSetRestorationFlag()
    {
        _databaseService.Entries = [Entry("a.db", isEnabled: true, status: DatabaseStatus.UpgradeRequired, backupExists: true)];
        _databaseService.RestoreFromBackupReturnValue = false;
        var component = Render<ManageDatabasesTab>();

        await component.InvokeAsync(() =>
            ((Task)component.Instance.GetType()
                .GetMethod("RestoreFromBackup", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(component.Instance, [_databaseService.Entries[0]])!));

        Assert.Equal(1, _databaseService.RestoreFromBackupCalls);
        Assert.False(component.Instance.HasDatabaseStateChanged);
        _announcementService.Received(1).Announce(Arg.Is<string>(s => s.Contains("Could not restore")));
        _announcementService.DidNotReceive().Announce(Arg.Is<string>(s => s.StartsWith("Restored ")));
    }

    [Fact]
    public async Task RestoreFromBackup_ServiceReturnsTrue_SetsStickyFlag_AndAnnouncesSuccess()
    {
        _databaseService.Entries = [Entry("a.db", isEnabled: true, status: DatabaseStatus.UpgradeRequired, backupExists: true)];
        _databaseService.RestoreFromBackupReturnValue = true;
        var component = Render<ManageDatabasesTab>();

        await component.InvokeAsync(() =>
            ((Task)component.Instance.GetType()
                .GetMethod("RestoreFromBackup", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(component.Instance, [_databaseService.Entries[0]])!));

        Assert.Equal(1, _databaseService.RestoreFromBackupCalls);
        Assert.True(component.Instance.HasDatabaseStateChanged);
        _announcementService.Received(1).Announce(Arg.Is<string>(s => s.StartsWith("Restored ")));
    }

    [Fact]
    public async Task RestoreFromBackup_WhileUpgradeInFlight_DoesNotInvokeService()
    {
        // Prevents the race where DatabaseRecoveryService.RestoreFromBackupAsync triggers
        // ClassifyEntriesAsync, deleting a .upgrade.bak that an in-flight upgrade still needs.
        _databaseService.Entries = [Entry("a.db", isEnabled: true, status: DatabaseStatus.UpgradeRequired, backupExists: true)];
        _coordinator.IsAnyUpgradeInFlight.Returns(true);
        var component = Render<ManageDatabasesTab>();

        await component.InvokeAsync(() =>
            ((Task)component.Instance.GetType()
                .GetMethod("RestoreFromBackup", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(component.Instance, [_databaseService.Entries[0]])!));

        Assert.Equal(0, _databaseService.RestoreFromBackupCalls);
        _announcementService.Received(1).Announce(Arg.Is<string>(s => s.Contains("Cannot restore")));
    }

    [Fact]
    public void RowReceivesNullUpgradeProgress_WhenNeitherSlotMatchesFileName()
    {
        var entry = Entry("a.db", isEnabled: false, status: DatabaseStatus.UpgradeRequired);
        _databaseService.Entries = [entry];
        _progressBannerService.ManageDatabasesProgress.Returns(MakeProgress(currentEntryName: "different.db"));
        _progressBannerService.BackgroundProgress.Returns(MakeProgress(
            currentEntryName: "another.db",
            scope: UpgradeProgressScope.Background));

        var component = Render<ManageDatabasesTab>();

        Assert.Empty(component.FindAll(".db-entry-upgrading-text"));
        Assert.Empty(component.FindAll(".db-entry-cancel-btn"));
    }

    [Fact]
    public void RowReceivesUpgradeProgress_WhenBackgroundSlotMatches()
    {
        var entry = Entry("b.db", isEnabled: false, status: DatabaseStatus.UpgradeRequired);
        _databaseService.Entries = [entry];
        _progressBannerService.BackgroundProgress.Returns(MakeProgress(
            currentEntryName: "b.db",
            currentBatchSize: 3,
            currentBatchPosition: 2,
            scope: UpgradeProgressScope.Background));
        _coordinator.IsUpgradeInFlight("b.db").Returns(false);

        var component = Render<ManageDatabasesTab>();

        var text = component.Find(".db-entry-upgrading-text");
        Assert.Contains("Migrating schema 2 of 3", text.TextContent);
    }

    [Fact]
    public void RowReceivesUpgradeProgress_WhenManageDatabasesSlotMatches()
    {
        var entry = Entry("a.db", isEnabled: false, status: DatabaseStatus.UpgradeRequired);
        _databaseService.Entries = [entry];
        _progressBannerService.ManageDatabasesProgress.Returns(MakeProgress(
            currentEntryName: "a.db",
            currentBatchSize: 2,
            currentBatchPosition: 1));
        _coordinator.IsUpgradeInFlight("a.db").Returns(true);

        var component = Render<ManageDatabasesTab>();

        var text = component.Find(".db-entry-upgrading-text");
        Assert.Contains("Migrating schema 1 of 2", text.TextContent);
    }

    [Fact]
    public async Task SingleRowRemove_DuringCoordinatorUpgrade_DualSignal_AcceptanceLabelMentionsCancel()
    {
        var entry = Entry("a.db", isEnabled: false, status: DatabaseStatus.UpgradeRequired);
        _databaseService.Entries = [entry];

        _coordinator.IsUpgradeInFlight("a.db").Returns(true);
        _progressBannerService.ManageDatabasesProgress.Returns(MakeProgress(
            currentEntryName: "a.db",
            scope: UpgradeProgressScope.ManageDatabasesTriggered));

        var alertSurface = new FakeInlineAlertSurface { Result = new InlineAlertResult(false, null) };
        var component = RenderWithAlertSurface(alertSurface);

        await component.InvokeAsync(() => component.Find(".db-entry-remove-btn").Click());

        var captured = Assert.Single(alertSurface.Requests);
        Assert.Contains("Cancel", captured.AcceptLabel ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("upgrade", captured.AcceptLabel ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ToggleSelection_AddsToSelection_RevealsBulkStrip()
    {
        _databaseService.Entries = [Entry("a.db", isEnabled: false, status: DatabaseStatus.Ready)];
        var component = Render<ManageDatabasesTab>();

        await component.InvokeAsync(() => component.Find(".db-entry-row input[type='checkbox']").ChangeAsync(new ChangeEventArgs { Value = true }));

        Assert.True(component.Instance.HasSelectedForRemoval);
        Assert.Single(component.FindAll(".manage-databases-bulk-strip"));
        Assert.Contains("1 selected", component.Find(".manage-databases-bulk-count").TextContent);
    }

    [Fact]
    public async Task ToggleSelection_OnRemovedEntry_PrunedFromSelection()
    {
        _databaseService.Entries = [
            Entry("a.db", isEnabled: false, status: DatabaseStatus.Ready),
            Entry("b.db", isEnabled: false, status: DatabaseStatus.Ready)];
        var component = Render<ManageDatabasesTab>();

        var checkboxes = component.FindAll(".db-entry-row input[type='checkbox']");
        await component.InvokeAsync(() => checkboxes[0].ChangeAsync(new ChangeEventArgs { Value = true }));
        await component.InvokeAsync(() => checkboxes[1].ChangeAsync(new ChangeEventArgs { Value = true }));
        Assert.Contains("2 selected", component.Find(".manage-databases-bulk-count").TextContent);

        _databaseService.Entries = [Entry("a.db", isEnabled: false, status: DatabaseStatus.Ready)];
        _databaseService.RaiseEntriesChanged();
        await component.InvokeAsync(() => { });

        Assert.True(component.Instance.HasSelectedForRemoval);
        Assert.Contains("1 selected", component.Find(".manage-databases-bulk-count").TextContent);
    }

    [Fact]
    public async Task ToggleSelection_TwiceOnSameRow_RemovesFromSelection()
    {
        _databaseService.Entries = [Entry("a.db", isEnabled: false, status: DatabaseStatus.Ready)];
        var component = Render<ManageDatabasesTab>();

        await component.InvokeAsync(() => component.Find(".db-entry-row input[type='checkbox']").ChangeAsync(new ChangeEventArgs { Value = true }));
        Assert.True(component.Instance.HasSelectedForRemoval);

        await component.InvokeAsync(() => component.Find(".db-entry-row input[type='checkbox']").ChangeAsync(new ChangeEventArgs { Value = true }));

        Assert.False(component.Instance.HasSelectedForRemoval);
        Assert.Empty(component.FindAll(".manage-databases-bulk-strip"));
    }

    [Fact]
    public async Task ToggleSelection_UpdatesAriaLiveAnnouncement()
    {
        _databaseService.Entries = [
            Entry("a.db", isEnabled: false, status: DatabaseStatus.Ready),
            Entry("b.db", isEnabled: false, status: DatabaseStatus.Ready)];
        var component = Render<ManageDatabasesTab>();

        var liveRegion = component.Find(".manage-databases-tab > span[role='status'][aria-live='polite']");
        Assert.Equal(string.Empty, liveRegion.TextContent.Trim());

        var checkboxes = component.FindAll(".db-entry-row input[type='checkbox']");
        await component.InvokeAsync(() => checkboxes[0].ChangeAsync(new ChangeEventArgs { Value = true }));
        liveRegion = component.Find(".manage-databases-tab > span[role='status'][aria-live='polite']");
        Assert.Contains("1", liveRegion.TextContent);
        Assert.Contains("selected", liveRegion.TextContent, StringComparison.OrdinalIgnoreCase);

        await component.InvokeAsync(() => checkboxes[1].ChangeAsync(new ChangeEventArgs { Value = true }));
        liveRegion = component.Find(".manage-databases-tab > span[role='status'][aria-live='polite']");
        Assert.Contains("2", liveRegion.TextContent);
    }

    private static DatabaseEntry Entry(string fileName, bool isEnabled, DatabaseStatus status, bool backupExists = false) =>
        new(fileName, $@"C:\dbs\{fileName}", isEnabled, status, backupExists);

    private static BannerProgressEntry MakeProgress(
        string currentEntryName = "a.db",
        UpgradePhase currentPhase = UpgradePhase.MigratingSchema,
        int currentBatchPosition = 1,
        int currentBatchSize = 1,
        int queuedBatchesAfter = 0,
        UpgradeProgressScope scope = UpgradeProgressScope.ManageDatabasesTriggered) =>
        new(
            UpgradeBatchId.Create(),
            scope,
            currentBatchPosition,
            currentBatchSize,
            currentEntryName,
            currentPhase,
            queuedBatchesAfter,
            () => { });

    private IRenderedComponent<ManageDatabasesTab> RenderWithAlertSurface(IInlineAlertSurface alertSurface) =>
        Render<ManageDatabasesTab>(parameters => parameters
            .AddCascadingValue(alertSurface));
}
