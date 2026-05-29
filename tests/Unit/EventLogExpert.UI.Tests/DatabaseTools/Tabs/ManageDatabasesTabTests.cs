// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.Database.Upgrade;
using EventLogExpert.UI.DatabaseTools.Tabs;
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
    private readonly IProgressBannerService _progressBannerService = Substitute.For<IProgressBannerService>();
    private readonly ITraceLogger _traceLogger = Substitute.For<ITraceLogger>();

    public ManageDatabasesTabTests()
    {
        _progressBannerService.ManageDatabasesProgress.Returns((BannerProgressEntry?)null);

        Services.AddSingleton(_announcementService);
        Services.AddSingleton(_coordinator);
        Services.AddSingleton<IDatabaseService>(_databaseService);
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
    public void Render_ClassificationPendingNotice_WhenClassificationInProgress()
    {
        _databaseService.Entries = [];
        _databaseService.InitialClassificationTask = new TaskCompletionSource().Task;

        var component = Render<ManageDatabasesTab>();

        Assert.Single(component.FindAll(".manage-status-banner--info"));
        Assert.Empty(component.FindAll(".manage-databases-empty"));
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

    private static DatabaseEntry Entry(string fileName, bool isEnabled, DatabaseStatus status, bool backupExists = false) =>
        new(fileName, $@"C:\dbs\{fileName}", isEnabled, status, backupExists);
}
